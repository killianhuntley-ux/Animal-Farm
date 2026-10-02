using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Sit &amp; rest (muscle 01 verb, "the game's signature moment"). Free from
    /// minute one (taught by the guide-light send-off line). The Rest action
    /// (Z / gamepad East, rebindable) toggles a seated state:
    /// - the shepherd holds still and squats (ShepherdVisual reads IsSeated);
    ///   tools are put away;
    /// - nearby HAPPY spirits drift in and settle in a loose ring around you
    ///   (SpiritAgent.BeginRestGather), and their mood slowly ticks up;
    /// - the sit-down theme fades in (RestTheme) over the ambient bed;
    /// - the camera roams free: the Move input pans it around the seat
    ///   (CameraFollow adds CameraPan) until you stand up again.
    /// Standing: press Rest again. Auto-stands if a competition starts or the
    /// shepherd is moved away (teleport/load). Transient: nothing is saved.
    /// Self-spawns onto the Player at scene load.
    /// </summary>
    public class ShepherdRest : MonoBehaviour
    {
        public static ShepherdRest Instance { get; private set; }

        /// <summary>True while the shepherd is seated resting.</summary>
        public static bool IsSeated { get; private set; }

        /// <summary>World-space camera offset from the shepherd while seated (zero otherwise).</summary>
        public static Vector2 CameraPan { get; private set; }

        // ---- tuning ---------------------------------------------------------
        private const float PanSpeed = 7f;            // world units / sec of free camera roam
        private const float PanMaxRadius = 16f;
        private const float PanMargin = 4f;           // matches CameraFollow.boundsMargin
        private const float GatherRadius = 8f;        // happy spirits within this come over
        private const int MaxGatherers = 6;           // cosy circle, not a crowd
        private const float RingBase = 2.0f;          // settle ring radius (alternates +0.55)
        private const float FollowerComfortRadius = 3.5f;
        private const float MoodPerSecond = 0.15f;    // ~ +9 Spirit per minute per settled spirit
        private const float MoodCap = 90f;            // rest alone never maxes a spirit (fulfilment needs care too)
        private const float EvalInterval = 1f;        // gather re-evaluation + mood tick cadence
        private const float StandAwayDistance = 0.75f;
        private const float HeartChance = 0.06f;

        private static readonly Color HeartPink = new Color(1f, 0.62f, 0.75f, 1f);

        private ShepherdController _ctrl;
        private RestTheme _theme;
        private Vector2 _sitPos;
        private Vector2 _pan;
        private float _nextEval;
        private float _seatedSince;

        private readonly Dictionary<SpiritAgent, int> _gathered = new Dictionary<SpiritAgent, int>();
        private readonly List<SpiritAgent> _scratch = new List<SpiritAgent>();

        private Text _hint;

        // ------------------------------------------------------- Lifecycle

        /// <summary>Self-spawn: finds the shepherd after the scene loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var player = GameObject.FindWithTag("Player");
            if (player == null) return;
            if (player.GetComponent<ShepherdRest>() == null)
                player.AddComponent<ShepherdRest>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            IsSeated = false;
            CameraPan = Vector2.zero;
            _ctrl = GetComponent<ShepherdController>();
            _theme = RestTheme.Ensure(gameObject);
        }

        private void Start()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.RestPressed += HandleRestPressed;
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.RestPressed -= HandleRestPressed;
            if (Instance == this)
            {
                ReleaseAll();
                IsSeated = false;
                CameraPan = Vector2.zero;
                Instance = null;
            }
            if (_hint != null) Destroy(_hint.gameObject);
        }

        private void OnDisable()
        {
            if (IsSeated && Instance == this) Stand(quiet: true);
        }

        // ------------------------------------------------------- Sit / stand

        private void HandleRestPressed()
        {
            if (IsSeated) Stand(quiet: false);
            else if (CanSit()) Sit();
        }

        /// <summary>Gates sitting: nothing modal, nothing committed, no event running.</summary>
        private bool CanSit()
        {
            if (UIInputLock.BlockDirectKeys) return false;
            if (Time.timeScale == 0f) return false;
            if (ToolController.MovementLocked) return false;
            if (PoutyMount.IsRiding) return false; // riding and sitting are mutually exclusive

            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return false;

            var sel = SelectionController.Instance;
            if (sel != null && sel.IsMoving) return false; // ghost placement owns the cursor
            return true;
        }

        private void Sit()
        {
            IsSeated = true;
            _sitPos = transform.position;
            _pan = Vector2.zero;
            CameraPan = Vector2.zero;
            _nextEval = 0f; // gather immediately
            _seatedSince = Time.time;
            if (_theme != null) _theme.Active = true;

            Bleeps.Play(BleepKind.Soothe, 0.35f);
            Puffs.Burst(transform.position + Vector3.down * 0.25f,
                new Color(0.76f, 0.68f, 0.54f, 0.6f), 3, 0.6f, 0.25f, 0.06f);
            ShowHint(true);
        }

        /// <summary>Stand up: spirits resume wandering, the theme fades, the camera eases home.</summary>
        private void Stand(bool quiet)
        {
            if (!IsSeated) return;
            IsSeated = false;
            CameraPan = Vector2.zero;
            _pan = Vector2.zero;
            if (_theme != null) _theme.Active = false;
            ReleaseAll();
            ShowHint(false);
            if (!quiet) Bleeps.Play(BleepKind.Click, 0.3f);
        }

        // ------------------------------------------------------- Per frame

        private void Update()
        {
            if (!IsSeated) return;

            // Auto-stand: an event took over, or the shepherd was moved (tp/load).
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if ((comp != null && comp.EventRunning)
                || ((Vector2)transform.position - _sitPos).sqrMagnitude
                   > StandAwayDistance * StandAwayDistance)
            {
                Stand(quiet: false);
                return;
            }

            UpdatePan();

            if (Time.time >= _nextEval)
            {
                _nextEval = Time.time + EvalInterval;
                EvaluateGathering();
                TickComfort();
            }
        }

        /// <summary>Move input roams the camera around the seat, boxed to the owned land.</summary>
        private void UpdatePan()
        {
            Vector2 move = GameInput.Instance != null ? GameInput.Instance.Move : Vector2.zero;
            _pan += move * (PanSpeed * Time.deltaTime);
            _pan = Vector2.ClampMagnitude(_pan, PanMaxRadius);

            // Keep the pan inside the same box CameraFollow clamps to, so
            // pushing into an edge never builds up an offset to unwind.
            var parcels = ParcelManager.Instance;
            if (parcels != null)
            {
                Rect b = parcels.OwnedBoundsWorld;
                if (b.width > 0f && b.height > 0f)
                {
                    float x = Mathf.Clamp(_sitPos.x + _pan.x, b.xMin - PanMargin, b.xMax + PanMargin);
                    float y = Mathf.Clamp(_sitPos.y + _pan.y, b.yMin - PanMargin, b.yMax + PanMargin);
                    _pan = new Vector2(x - _sitPos.x, y - _sitPos.y);
                }
            }

            CameraPan = _pan;
        }

        // ------------------------------------------------------- Spirits

        /// <summary>
        /// Picks up to MaxGatherers happy, awake visitors/residents within
        /// range (nearest first), sends new ones to ring slots, and releases
        /// any that stopped qualifying (mood dipped, fell asleep, wandered off).
        /// </summary>
        private void EvaluateGathering()
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) { ReleaseAll(); return; }

            Vector2 center = transform.position;
            _scratch.Clear();
            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !Qualifies(a)) continue;
                if (((Vector2)a.transform.position - center).sqrMagnitude > GatherRadius * GatherRadius)
                    continue;
                _scratch.Add(a);
            }
            _scratch.Sort((p, q) =>
                ((Vector2)p.transform.position - center).sqrMagnitude
                    .CompareTo(((Vector2)q.transform.position - center).sqrMagnitude));
            if (_scratch.Count > MaxGatherers) _scratch.RemoveRange(MaxGatherers, _scratch.Count - MaxGatherers);

            // Release anyone no longer in the circle (or destroyed).
            List<SpiritAgent> drop = null;
            foreach (var kv in _gathered)
            {
                if (kv.Key == null || !_scratch.Contains(kv.Key))
                    (drop ?? (drop = new List<SpiritAgent>())).Add(kv.Key);
            }
            if (drop != null)
                for (int i = 0; i < drop.Count; i++)
                {
                    if (drop[i] != null) drop[i].EndRestGather();
                    _gathered.Remove(drop[i]);
                }

            // Send newcomers to the lowest free ring slot.
            for (int i = 0; i < _scratch.Count; i++)
            {
                var a = _scratch[i];
                if (_gathered.ContainsKey(a)) continue;

                int slot = LowestFreeSlot();
                if (a.BeginRestGather(SlotPoint(center, slot)))
                    _gathered[a] = slot;
            }
        }

        /// <summary>Happy, awake, in-town spirit (visitor or resident).</summary>
        private static bool Qualifies(SpiritAgent a) =>
            (a.State == SpiritState.Visitor || a.State == SpiritState.Resident)
            && !a.IsSleeping
            && a.MoodBand == SpiritMoodBand.Happy;

        private int LowestFreeSlot()
        {
            for (int s = 0; s < MaxGatherers; s++)
            {
                bool used = false;
                foreach (var kv in _gathered)
                    if (kv.Value == s) { used = true; break; }
                if (!used) return s;
            }
            return 0;
        }

        /// <summary>Slots fan around the seat, starting at the lower-left so the face stays clear.</summary>
        private static Vector3 SlotPoint(Vector2 center, int slot)
        {
            float angle = (210f - slot * (360f / MaxGatherers)) * Mathf.Deg2Rad;
            float r = RingBase + (slot % 2 == 0 ? 0f : 0.55f);
            return new Vector3(center.x + Mathf.Cos(angle) * r, center.y + Mathf.Sin(angle) * r, 0f);
        }

        /// <summary>Slow comfort for settled spirits (and happy followers sitting close).</summary>
        private void TickComfort()
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            Vector2 center = transform.position;
            var all = mgr.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;

                bool comforted = a.IsRestSettled
                    || (a.IsFollowing && Qualifies(a)
                        && ((Vector2)a.transform.position - center).sqrMagnitude
                           <= FollowerComfortRadius * FollowerComfortRadius);
                if (!comforted) continue;

                a.RestComfort(MoodPerSecond * EvalInterval, MoodCap);
                if (Random.value < HeartChance)
                    FloatingText.Show(a.transform.position + Vector3.up * 0.9f, "<3", HeartPink);
            }
        }

        private void ReleaseAll()
        {
            foreach (var kv in _gathered)
                if (kv.Key != null) kv.Key.EndRestGather();
            _gathered.Clear();
        }

        // ------------------------------------------------------- Hint + debug

        private void ShowHint(bool show)
        {
            if (show && _hint == null)
            {
                var root = UIRoot.GetRoot();
                if (root != null)
                {
                    _hint = UIRoot.MakeText(root, "RestHint", 22, TextAnchor.MiddleCenter, UIStyle.Cream);
                    var rt = _hint.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, 150f);
                    rt.sizeDelta = new Vector2(900f, 34f);
                }
            }

            if (_hint == null) return;
            if (show) _hint.text = "Resting. Move to look around. " + RestKeyLabel() + " to stand.";
            _hint.gameObject.SetActive(show);
        }

        private static string RestKeyLabel()
        {
            var gi = GameInput.Instance;
            var map = gi != null && gi.Actions != null ? gi.Actions.FindActionMap("Player") : null;
            var action = map != null ? map.FindAction("Rest") : null;
            if (action == null) return "Z";
            string s = action.GetBindingDisplayString(default(InputBinding.DisplayStringOptions), "KeyboardMouse");
            return string.IsNullOrEmpty(s) ? "Z" : s;
        }

        /// <summary>Debug console: toggles sitting, bypassing the sit gates.</summary>
        public void Debug_Toggle()
        {
            if (IsSeated) Stand(quiet: false);
            else if (!PoutyMount.IsRiding) Sit();
        }

        /// <summary>Debug console: one-line status.</summary>
        public string Debug_Status() =>
            (IsSeated ? "seated " + Mathf.RoundToInt(Time.time - _seatedSince) + "s" : "standing")
            + ", gathered " + _gathered.Count + ", pan " + _pan.x.ToString("0.0") + "," + _pan.y.ToString("0.0");
    }
}
