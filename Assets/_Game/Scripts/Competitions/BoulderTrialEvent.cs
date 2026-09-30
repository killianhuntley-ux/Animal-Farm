using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AnimalFarm.Competitions
{
    /// <summary>
    /// The Boulder Trial (slice 05): Sisyphus as sport. The player pilots the
    /// entered spirit up a 16-unit hill against two AI rivals by holding the
    /// push input and managing stamina - rest BEFORE it hits zero, because an
    /// overexerted spirit is forced to rest while its boulder slides fast.
    /// Everything (arena greybox, HUD) is built in code and torn down after.
    /// </summary>
    public class BoulderTrialEvent : MonoBehaviour
    {
        // ---- tuning constants ------------------------------------------------

        private const float LaneSpacing = 3.5f;      // lanes at -3.5 / 0 / +3.5
        private const float HillHalfHeight = 8f;     // start y-8 .. summit y+8
        private const float LaneWidth = 1.6f;

        private const float PlayerPushSpeedBase = 1.8f; // u/s at full stamina factor, before Grace
        private const float SlideSpeed = 1.1f;       // u/s while resting
        private const float OverexertedSlideSpeed = 1.6f;
        private const float OverexertedRestSeconds = 2.5f;
        private const float StaminaDrainPerSecond = 14f;
        private const float StaminaRegenPerSecond = 20f;
        private const float HungryDrainMultiplier = 1.15f; // Hunger01 > 0.5
        private const float MinStaminaFactor = 0.55f;

        private const float RaceTimeoutSeconds = 120f;
        private const float ResultHoldSeconds = 1.5f;
        private const float HintFadeStartSeconds = 4f;
        private const float HintFadeDurationSeconds = 1f;
        private const float SpiritBelowBoulder = 0.85f; // spirit glued this far under its boulder

        private static readonly Color LaneColor = new Color(0.16f, 0.20f, 0.15f);
        private static readonly Color SummitGold = new Color(1f, 0.85f, 0.3f);
        private static readonly Color BoulderGrey = new Color(0.55f, 0.55f, 0.58f);
        private static readonly Color RivalGrey = new Color(0.75f, 0.75f, 0.78f, 0.85f);
        private static readonly Color BarGreen = new Color(0.25f, 0.8f, 0.3f);
        private static readonly Color BarRed = new Color(0.85f, 0.2f, 0.2f);

        // ---- 1x1 white sprite, generated once, shared by all greybox visuals --

        private static Sprite _whiteSprite;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    // 4 pixels per unit -> the 4x4 texture is exactly 1x1 world units.
                    _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                return _whiteSprite;
            }
        }

        // ---- per-rival state ---------------------------------------------------

        private class Rival
        {
            public Transform boulder;
            public Transform body;
            public float laneX;
            public float y;          // boulder height (world y)
            public float pushSpeed;  // u/s, difficulty + variance
            public bool pushing;
            public float phaseTimer; // counts down to the next push/rest flip
            public float pushMin, pushMax, restMin, restMax;
        }

        // ---- state ---------------------------------------------------------------

        private CompetitionManager _manager;
        private SpiritAgent _spirit;
        private int _difficulty;

        private Vector3 _origin;
        private float _startY, _summitY;
        private Vector3 _storedSpiritPosition;

        private Transform _playerBoulder;
        private float _playerY;
        private float _stamina, _maxStamina;
        private float _playerPushSpeed;
        private bool _overexerted;
        private float _overexertedTimer;

        private readonly List<Rival> _rivals = new List<Rival>();

        // HUD
        private GameObject _hudRoot;
        private Image _staminaFill;
        private RectTransform _staminaFillRect;
        private float _staminaBarWidth;
        private Text _positionText;
        private Text _hintText;

        private bool _finished;

        // ---- entry ---------------------------------------------------------------

        /// <summary>Builds the arena and runs the whole trial; reports via manager.</summary>
        public void Run(CompetitionManager manager, SpiritAgent spirit, int difficulty)
        {
            _manager = manager;
            _spirit = spirit;
            _difficulty = Mathf.Clamp(difficulty, 0, 2);

            _origin = CompetitionManager.ArenaOrigin;
            _startY = _origin.y - HillHalfHeight;
            _summitY = _origin.y + HillHalfHeight;

            if (spirit == null)
            {
                // Nothing to pilot; report a last-place scratch and vanish.
                Finish(3, teleportSpiritBack: false);
                return;
            }

            transform.position = _origin;
            BuildArena();
            BuildHud();

            // Take the spirit out of its normal life and stand it on the start line.
            _storedSpiritPosition = spirit.transform.position;
            Vector3 startGlue = GluePosition(_playerY);
            spirit.transform.position = startGlue;
            spirit.EnterCeremony(startGlue);

            StartCoroutine(RunTrial());
        }

        // ---- arena construction ----------------------------------------------------

        private void BuildArena()
        {
            float[] laneXs = { _origin.x - LaneSpacing, _origin.x, _origin.x + LaneSpacing };

            // Lane tracks: dark grey-green strips the full height of the hill.
            for (int i = 0; i < laneXs.Length; i++)
            {
                MakeQuad("LaneTrack" + i,
                    new Vector3(laneXs[i], _origin.y, 0f),
                    new Vector3(LaneWidth, HillHalfHeight * 2f, 1f),
                    LaneColor, sortingOrder: -10);
            }

            // Summit line: gold strip across all three lanes.
            MakeQuad("SummitLine",
                new Vector3(_origin.x, _summitY, 0f),
                new Vector3(8f, 0.25f, 1f),
                SummitGold, sortingOrder: -9);

            // Player boulder, middle lane.
            _playerY = _startY;
            _playerBoulder = MakeQuad("PlayerBoulder",
                new Vector3(_origin.x, _playerY, 0f),
                new Vector3(0.9f, 0.9f, 1f),
                BoulderGrey, sortingOrder: 5).transform;

            // Two AI rivals, outer lanes. Difficulty tunes speed and rest discipline.
            float[] rivalSpeeds = { 1.1f, 1.5f, 1.9f };
            float[] restScales = { 1f, 0.8f, 0.62f };

            for (int i = 0; i < 2; i++)
            {
                float laneX = i == 0 ? laneXs[0] : laneXs[2];
                var rival = new Rival
                {
                    laneX = laneX,
                    y = _startY,
                    pushSpeed = rivalSpeeds[_difficulty] * Random.Range(0.93f, 1.07f),
                    pushMin = 2.2f * Random.Range(0.9f, 1.1f),
                    pushMax = 3.2f * Random.Range(0.9f, 1.1f),
                    restMin = 1.0f * restScales[_difficulty],
                    restMax = 1.6f * restScales[_difficulty],
                    pushing = false
                };
                rival.phaseTimer = Random.Range(0.2f, 0.9f); // stagger their first push

                rival.boulder = MakeQuad("RivalBoulder" + i,
                    new Vector3(laneX, rival.y, 0f),
                    new Vector3(0.9f, 0.9f, 1f),
                    BoulderGrey, sortingOrder: 5).transform;

                rival.body = MakeQuad("RivalSpirit" + i,
                    new Vector3(laneX, rival.y - SpiritBelowBoulder, 0f),
                    new Vector3(0.7f, 0.7f, 1f),
                    RivalGrey, sortingOrder: 4).transform;

                _rivals.Add(rival);
            }
        }

        /// <summary>Tinted unit-square sprite, parented under this event's GameObject.</summary>
        private GameObject MakeQuad(string name, Vector3 pos, Vector3 scale, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            if (_manager != null && _manager.SpriteMaterial != null)
                sr.sharedMaterial = _manager.SpriteMaterial;

            return go;
        }

        // ---- HUD construction -------------------------------------------------------

        private void BuildHud()
        {
            var root = UIRoot.GetRoot();

            _hudRoot = new GameObject("BoulderTrialHUD");
            var hudRect = _hudRoot.AddComponent<RectTransform>();
            hudRect.SetParent(root, false);
            hudRect.anchorMin = Vector2.zero;
            hudRect.anchorMax = Vector2.one;
            hudRect.offsetMin = Vector2.zero;
            hudRect.offsetMax = Vector2.zero;

            // Stamina bar, bottom-centre: dark backing + coloured fill.
            _staminaBarWidth = 400f;
            var back = MakeHudImage("StaminaBack", hudRect, new Color(0.05f, 0.05f, 0.07f, 0.85f));
            var backRect = back.rectTransform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 48f);
            backRect.sizeDelta = new Vector2(_staminaBarWidth + 8f, 30f);

            _staminaFill = MakeHudImage("StaminaFill", backRect, BarGreen);
            _staminaFillRect = _staminaFill.rectTransform;
            _staminaFillRect.anchorMin = new Vector2(0f, 0.5f);
            _staminaFillRect.anchorMax = new Vector2(0f, 0.5f);
            _staminaFillRect.pivot = new Vector2(0f, 0.5f);
            _staminaFillRect.anchoredPosition = new Vector2(4f, 0f);
            _staminaFillRect.sizeDelta = new Vector2(_staminaBarWidth, 22f);

            // Live position readout, top-centre.
            _positionText = UIRoot.MakeText(hudRect, "PositionText", 40, TextAnchor.MiddleCenter, Color.white);
            var posRect = _positionText.rectTransform;
            posRect.anchorMin = posRect.anchorMax = new Vector2(0.5f, 1f);
            posRect.pivot = new Vector2(0.5f, 1f);
            posRect.anchoredPosition = new Vector2(0f, -36f);
            posRect.sizeDelta = new Vector2(400f, 60f);
            _positionText.text = "";

            // Control hint above the stamina bar; fades out after a few seconds.
            _hintText = UIRoot.MakeText(hudRect, "PushHint", 22, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.9f));
            var hintRect = _hintText.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 88f);
            hintRect.sizeDelta = new Vector2(600f, 32f);
            _hintText.text = "hold SPACE / RT to push";
        }

        private static Image MakeHudImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ---- main flow -----------------------------------------------------------------

        private IEnumerator RunTrial()
        {
            // Stamina pool: morale matters, but the individual's Vigor matters
            // more. Spirit 0..100 + Vigor 2..9 -> max ~48..106.
            _maxStamina = 40f + _spirit.Spirit * 0.3f + _spirit.Vigor * 4f;
            _stamina = _maxStamina;

            // Grace gives a small push-speed edge (1.88..2.16 u/s) - a preview
            // of the future race event, where it takes centre stage.
            _playerPushSpeed = PlayerPushSpeedBase + _spirit.Grace * 0.04f;

            // Countdown at arena centre, one beat per second (scaled time).
            Vector3 centre = new Vector3(_origin.x, _origin.y, 0f);
            string[] beats = { "3..", "2..", "1..", "PUSH!" };
            for (int i = 0; i < beats.Length; i++)
            {
                if (SpiritGone()) { AbortForLostSpirit(); yield break; }
                FloatingText.Show(centre, beats[i],
                    i == beats.Length - 1 ? SummitGold : Color.white);
                if (i < beats.Length - 1) yield return new WaitForSeconds(1f);
            }

            // Race.
            float elapsed = 0f;
            float hintElapsed = 0f;
            bool summitReached = false;

            while (!summitReached && elapsed < RaceTimeoutSeconds)
            {
                if (SpiritGone()) { AbortForLostSpirit(); yield break; }

                float dt = Time.deltaTime;
                elapsed += dt;
                hintElapsed += dt;

                TickPlayer(dt);
                for (int i = 0; i < _rivals.Count; i++) TickRival(_rivals[i], dt);

                // Glue the piloted spirit just under its boulder. The ceremony lock
                // stops its own movement; we both re-aim the drift and set position
                // directly so it never lags the boulder.
                Vector3 glue = GluePosition(_playerY);
                _spirit.EnterCeremony(glue);
                _spirit.transform.position = glue;

                UpdateHud(hintElapsed);

                summitReached = _playerY >= _summitY;
                for (int i = 0; i < _rivals.Count && !summitReached; i++)
                    summitReached = _rivals[i].y >= _summitY;

                yield return null;
            }

            if (SpiritGone()) { AbortForLostSpirit(); yield break; }

            // Placement: rank by boulder height at first-summit (or timeout).
            int placement = 1;
            for (int i = 0; i < _rivals.Count; i++)
                if (_rivals[i].y > _playerY) placement++;

            ShowResult(placement);
            yield return new WaitForSeconds(ResultHoldSeconds);

            Finish(placement, teleportSpiritBack: true);
        }

        // ---- simulation ------------------------------------------------------------------

        private static bool ReadPushInput()
        {
            // Gameplay input is blocked during events by design, so poll the
            // devices directly rather than going through GameInput.
            if (AnimalFarm.Core.UIInputLock.BlockDirectKeys) return false; // typing/menus never push
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > 0.4f) return true;
            return false;
        }

        private void TickPlayer(float dt)
        {
            bool wantsPush = ReadPushInput();

            if (_overexerted)
            {
                // Forced rest: fast slide, stamina recovers, input ignored.
                _overexertedTimer -= dt;
                _stamina = Mathf.Min(_maxStamina, _stamina + StaminaRegenPerSecond * dt);
                _playerY = Mathf.Max(_startY, _playerY - OverexertedSlideSpeed * dt);
                if (_overexertedTimer <= 0f) _overexerted = false;
            }
            else if (wantsPush && _stamina > 0f)
            {
                float staminaFactor = MinStaminaFactor + (1f - MinStaminaFactor) * (_stamina / _maxStamina);
                _playerY = Mathf.Min(_summitY, _playerY + _playerPushSpeed * staminaFactor * dt);

                float drain = StaminaDrainPerSecond;
                if (_spirit != null && _spirit.Hunger01 > 0.5f) drain *= HungryDrainMultiplier;
                _stamina -= drain * dt;

                if (_stamina <= 0f)
                {
                    _stamina = 0f;
                    _overexerted = true;
                    _overexertedTimer = OverexertedRestSeconds;
                }
            }
            else
            {
                _stamina = Mathf.Min(_maxStamina, _stamina + StaminaRegenPerSecond * dt);
                _playerY = Mathf.Max(_startY, _playerY - SlideSpeed * dt);
            }

            if (_playerBoulder != null)
                _playerBoulder.position = new Vector3(_origin.x, _playerY, 0f);
        }

        private void TickRival(Rival r, float dt)
        {
            r.phaseTimer -= dt;
            if (r.phaseTimer <= 0f)
            {
                r.pushing = !r.pushing;
                r.phaseTimer = r.pushing
                    ? Random.Range(r.pushMin, r.pushMax)
                    : Random.Range(r.restMin, r.restMax);
            }

            r.y = r.pushing
                ? Mathf.Min(_summitY, r.y + r.pushSpeed * dt)
                : Mathf.Max(_startY, r.y - SlideSpeed * dt);

            if (r.boulder != null) r.boulder.position = new Vector3(r.laneX, r.y, 0f);
            if (r.body != null) r.body.position = new Vector3(r.laneX, r.y - SpiritBelowBoulder, 0f);
        }

        private Vector3 GluePosition(float boulderY) =>
            new Vector3(_origin.x, boulderY - SpiritBelowBoulder, 0f);

        // ---- HUD updates ---------------------------------------------------------------------

        private void UpdateHud(float hintElapsed)
        {
            if (_staminaFill != null)
            {
                float frac = _maxStamina > 0f ? Mathf.Clamp01(_stamina / _maxStamina) : 0f;
                _staminaFillRect.sizeDelta = new Vector2(_staminaBarWidth * frac, 22f);

                Color c = Color.Lerp(BarRed, BarGreen, frac);
                if (_overexerted)
                {
                    // Flash: alternate alpha a few times a second.
                    bool bright = Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
                    c.a = bright ? 1f : 0.3f;
                }
                _staminaFill.color = c;
            }

            if (_positionText != null)
            {
                int rank = 1;
                for (int i = 0; i < _rivals.Count; i++)
                    if (_rivals[i].y > _playerY) rank++;
                _positionText.text = Ordinal(rank);
                _positionText.color = rank == 1 ? SummitGold : Color.white;
            }

            if (_hintText != null && hintElapsed > HintFadeStartSeconds)
            {
                float fadeT = Mathf.Clamp01((hintElapsed - HintFadeStartSeconds) / HintFadeDurationSeconds);
                var hc = _hintText.color;
                hc.a = 0.9f * (1f - fadeT);
                _hintText.color = hc;
                if (fadeT >= 1f) _hintText.gameObject.SetActive(false);
            }
        }

        private static string Ordinal(int rank)
        {
            switch (rank)
            {
                case 1: return "1st";
                case 2: return "2nd";
                default: return "3rd";
            }
        }

        private void ShowResult(int placement)
        {
            Vector3 centre = new Vector3(_origin.x, _origin.y, 0f);
            switch (placement)
            {
                case 1: FloatingText.Show(centre, "1st!", SummitGold); break;
                case 2: FloatingText.Show(centre, "2nd.", new Color(0.85f, 0.85f, 0.85f)); break;
                default: FloatingText.Show(centre, "3rd...", new Color(0.6f, 0.6f, 0.6f)); break;
            }
        }

        // ---- teardown ----------------------------------------------------------------------------

        private bool SpiritGone() => _spirit == null;

        /// <summary>The spirit was destroyed mid-event: tear down and report last place.</summary>
        private void AbortForLostSpirit() => Finish(3, teleportSpiritBack: false);

        private void Finish(int placement, bool teleportSpiritBack)
        {
            if (_finished) return;
            _finished = true;

            StopAllCoroutines();

            if (_hudRoot != null) Destroy(_hudRoot);

            if (teleportSpiritBack && _spirit != null)
            {
                _spirit.transform.position = _storedSpiritPosition;
                _spirit.ExitCeremony();
            }

            if (_manager != null)
            {
                _manager.ReportFinished(_spirit, new CompetitionResult
                {
                    eventName = "Boulder Trial",
                    placement = placement,
                    entrants = 3
                });
            }

            // The whole arena greybox is parented under this GameObject.
            Destroy(gameObject);
        }
    }
}
