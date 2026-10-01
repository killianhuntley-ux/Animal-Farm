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
    /// The Crossing (slice 05, event two): the ferry-dash made sport (GDD 2.8).
    /// The player sprints the entered spirit along a 20-unit track against two
    /// AI rivals by holding the run input and managing stamina - and, unlike
    /// the Boulder Trial, by TIMING: gold boost pads pop up and down along each
    /// lane, and crossing one while it is up grants a speed burst. Grace is the
    /// headline stat here (run speed and stamina pool). Releasing the input is
    /// a glide-stop - racers decelerate but never slide backwards.
    /// Everything (arena greybox, HUD) is built in code and torn down after.
    /// </summary>
    public class CrossingRaceEvent : MonoBehaviour
    {
        // ---- tuning constants ------------------------------------------------

        private const float LaneSpacing = 2.5f;      // lanes at y +2.5 / 0 / -2.5
        private const float TrackHalfLength = 10f;   // start x-10 .. finish x+10
        private const float LaneHeight = 1.4f;

        private const float PlayerRunSpeedBase = 1.6f; // u/s before Grace, at full stamina factor
        private const float GraceSpeedPerPoint = 0.09f;
        private const float GlideDecel = 4f;         // u/s^2 while not running (glide-stop)
        private const float OverexertedStopSeconds = 2.0f;
        private const float StaminaDrainPerSecond = 11f;
        private const float StaminaRegenPerSecond = 16f;
        private const float HungryDrainMultiplier = 1.15f; // Hunger01 > 0.5
        private const float MinStaminaFactor = 0.55f;

        private const int PadsPerLane = 4;
        private const float PadCycleSeconds = 1.6f;  // 0.8s up (bright), 0.8s down (dim)
        private const float PadUpSeconds = 0.8f;
        private const float PadBoostSpeed = 2.5f;    // u/s added while a burst is live
        private const float PadBoostSeconds = 0.8f;
        private const float PadUpScale = 1.25f;      // pop-up scale pulse
        private const float PadDownScale = 0.85f;
        private const float PadScaleLerpSpeed = 8f;

        private const float RaceTimeoutSeconds = 120f;
        private const float ResultHoldSeconds = 1.5f;
        private const float HintFadeStartSeconds = 4f;
        private const float HintFadeDurationSeconds = 1f;

        private static readonly Color LaneColor = new Color(0.13f, 0.17f, 0.22f); // river-dark
        private static readonly Color FinishGold = new Color(1f, 0.85f, 0.3f);
        private static readonly Color PadGoldUp = new Color(1f, 0.82f, 0.25f);
        private static readonly Color PadGoldDown = new Color(0.42f, 0.36f, 0.18f);
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

        // ---- per-pad state ---------------------------------------------------

        private class Pad
        {
            public Transform quad;
            public float x;
            public float laneY;
            public float phase;     // seconds of offset into the up/down cycle
            public bool consumed;   // one runner per lane, one burst per pad

            public bool IsUp(float now) =>
                Mathf.Repeat(now + phase, PadCycleSeconds) < PadUpSeconds;
        }

        // ---- per-rival state ---------------------------------------------------

        private class Rival
        {
            public Transform body;
            public float laneY;
            public float x;          // progress (world x)
            public float speed;      // current glide speed, u/s
            public float runSpeed;   // u/s while running, difficulty + variance
            public bool running;
            public float phaseTimer; // counts down to the next run/rest flip
            public float runMin, runMax, restMin, restMax;
            public float boostTimer;
            public List<Pad> pads;   // this rival's lane
        }

        // ---- state ---------------------------------------------------------------

        private CompetitionManager _manager;
        private SpiritAgent _spirit;
        private int _difficulty;

        private Vector3 _origin;
        private float _startX, _finishX;
        private Vector3 _storedSpiritPosition;

        private float _playerX;
        private float _playerSpeed;      // current glide speed, u/s (excludes burst)
        private float _playerBoostTimer;
        private float _stamina, _maxStamina;
        private float _playerRunSpeed;
        private bool _overexerted;
        private float _overexertedTimer;
        private List<Pad> _playerPads;

        private readonly List<Pad> _allPads = new List<Pad>();
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

        /// <summary>Builds the arena and runs the whole race; reports via manager.</summary>
        public void Run(CompetitionManager manager, SpiritAgent spirit, int difficulty)
        {
            _manager = manager;
            _spirit = spirit;
            _difficulty = Mathf.Clamp(difficulty, 0, 2);

            _origin = CompetitionManager.ArenaOrigin;
            _startX = _origin.x - TrackHalfLength;
            _finishX = _origin.x + TrackHalfLength;

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
            Vector3 startGlue = GluePosition(_playerX);
            spirit.transform.position = startGlue;
            spirit.EnterCeremony(startGlue);

            StartCoroutine(RunRace());
        }

        // ---- arena construction ----------------------------------------------------

        private void BuildArena()
        {
            float[] laneYs = { _origin.y + LaneSpacing, _origin.y, _origin.y - LaneSpacing };

            // Lane tracks: dark river-blue strips the full length of the course.
            for (int i = 0; i < laneYs.Length; i++)
            {
                MakeQuad("LaneTrack" + i,
                    new Vector3(_origin.x, laneYs[i], 0f),
                    new Vector3(TrackHalfLength * 2f, LaneHeight, 1f),
                    LaneColor, sortingOrder: -10);
            }

            // Finish line: gold strip down all three lanes.
            MakeQuad("FinishLine",
                new Vector3(_finishX, _origin.y, 0f),
                new Vector3(0.25f, LaneSpacing * 2f + LaneHeight, 1f),
                FinishGold, sortingOrder: -9);

            // Boost pads: 4 per lane, positions and phases seeded per difficulty
            // so a given tier always deals the same course. Save/restore the
            // global Random state so farm-side randomness is unaffected.
            var padXs = new float[laneYs.Length][];
            var padPhases = new float[laneYs.Length][];
            Random.State restoreState = Random.state;
            Random.InitState(7919 * (_difficulty + 1) + 101);
            float usableStart = _startX + 2.5f;                  // never on the start line
            float usableLength = (_finishX - 1.5f) - usableStart; // nor right at the finish
            float segment = usableLength / PadsPerLane;
            for (int lane = 0; lane < laneYs.Length; lane++)
            {
                padXs[lane] = new float[PadsPerLane];
                padPhases[lane] = new float[PadsPerLane];
                for (int p = 0; p < PadsPerLane; p++)
                {
                    padXs[lane][p] = usableStart + segment * p
                        + Random.Range(segment * 0.15f, segment * 0.85f);
                    padPhases[lane][p] = Random.Range(0f, PadCycleSeconds);
                }
            }
            Random.state = restoreState;

            var lanePads = new List<Pad>[laneYs.Length];
            for (int lane = 0; lane < laneYs.Length; lane++)
            {
                lanePads[lane] = new List<Pad>();
                for (int p = 0; p < PadsPerLane; p++)
                {
                    var pad = new Pad
                    {
                        x = padXs[lane][p],
                        laneY = laneYs[lane],
                        phase = padPhases[lane][p]
                    };
                    pad.quad = MakeQuad("Pad_L" + lane + "_" + p,
                        new Vector3(pad.x, pad.laneY, 0f),
                        new Vector3(0.9f, 0.9f, 1f),
                        PadGoldDown, sortingOrder: -5).transform;
                    lanePads[lane].Add(pad);
                    _allPads.Add(pad);
                }
            }

            // Player runs the middle lane; the spirit itself is the runner.
            _playerX = _startX;
            _playerPads = lanePads[1];

            // Two AI rivals, outer lanes. Difficulty tunes speed and rest discipline.
            float[] rivalSpeeds = { 1.7f, 2.0f, 2.3f };
            float[] restScales = { 1f, 0.8f, 0.62f };

            for (int i = 0; i < 2; i++)
            {
                int lane = i == 0 ? 0 : 2;
                var rival = new Rival
                {
                    laneY = laneYs[lane],
                    x = _startX,
                    runSpeed = rivalSpeeds[_difficulty] * Random.Range(0.93f, 1.07f),
                    runMin = 2.2f * Random.Range(0.9f, 1.1f),
                    runMax = 3.2f * Random.Range(0.9f, 1.1f),
                    restMin = 1.0f * restScales[_difficulty],
                    restMax = 1.6f * restScales[_difficulty],
                    running = false,
                    pads = lanePads[lane]
                };
                rival.phaseTimer = Random.Range(0.2f, 0.9f); // stagger their first sprint

                rival.body = MakeQuad("RivalSpirit" + i,
                    new Vector3(_startX, rival.laneY, 0f),
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

            _hudRoot = new GameObject("CrossingRaceHUD");
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
            _hintText = UIRoot.MakeText(hudRect, "RunHint", 22, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.9f));
            var hintRect = _hintText.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 88f);
            hintRect.sizeDelta = new Vector2(700f, 32f);
            _hintText.text = "hold SPACE / RT to run - time the glowing pads";
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

        private IEnumerator RunRace()
        {
            // Stamina pool: Grace carries the race the way Vigor carried the
            // boulder. Spirit 0..100 + Grace 2..9 -> max ~51..97.
            _maxStamina = 45f + _spirit.Spirit * 0.25f + _spirit.Grace * 3f;
            _stamina = _maxStamina;

            // Grace is the headline speed stat (1.78..2.41 u/s before pads).
            _playerRunSpeed = PlayerRunSpeedBase + _spirit.Grace * GraceSpeedPerPoint;

            // Countdown at arena centre, one beat per second (scaled time).
            Vector3 centre = new Vector3(_origin.x, _origin.y, 0f);
            string[] beats = { "3..", "2..", "1..", "GO!" };
            for (int i = 0; i < beats.Length; i++)
            {
                if (SpiritGone()) { AbortForLostSpirit(); yield break; }
                FloatingText.Show(centre, beats[i],
                    i == beats.Length - 1 ? FinishGold : Color.white);
                if (i < beats.Length - 1) yield return new WaitForSeconds(1f);
            }

            // Race.
            float elapsed = 0f;
            float hintElapsed = 0f;
            bool lineCrossed = false;

            while (!lineCrossed && elapsed < RaceTimeoutSeconds)
            {
                if (SpiritGone()) { AbortForLostSpirit(); yield break; }

                float dt = Time.deltaTime;
                elapsed += dt;
                hintElapsed += dt;

                TickPads(dt);
                TickPlayer(dt);
                for (int i = 0; i < _rivals.Count; i++) TickRival(_rivals[i], dt);

                // Glue the piloted spirit onto its lane. The ceremony lock stops
                // its own movement; we both re-aim the drift and set position
                // directly so it never lags the race.
                Vector3 glue = GluePosition(_playerX);
                _spirit.EnterCeremony(glue);
                _spirit.transform.position = glue;

                UpdateHud(hintElapsed);

                lineCrossed = _playerX >= _finishX;
                for (int i = 0; i < _rivals.Count && !lineCrossed; i++)
                    lineCrossed = _rivals[i].x >= _finishX;

                yield return null;
            }

            if (SpiritGone()) { AbortForLostSpirit(); yield break; }

            // Placement: rank by progress at first-line-crossing (or timeout).
            int placement = 1;
            for (int i = 0; i < _rivals.Count; i++)
                if (_rivals[i].x > _playerX) placement++;

            ShowResult(placement);
            yield return new WaitForSeconds(ResultHoldSeconds);

            Finish(placement, teleportSpiritBack: true);
        }

        // ---- simulation ------------------------------------------------------------------

        private static bool ReadRunInput()
        {
            // Gameplay input is blocked during events by design, so poll the
            // devices directly rather than going through GameInput.
            if (AnimalFarm.Core.UIInputLock.BlockDirectKeys) return false; // typing/menus never run
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > 0.4f) return true;
            return false;
        }

        /// <summary>Animate every pad's up/down pop: scale pulse + gold brightness.</summary>
        private void TickPads(float dt)
        {
            float now = Time.time;
            for (int i = 0; i < _allPads.Count; i++)
            {
                var pad = _allPads[i];
                if (pad.quad == null) continue;

                bool up = pad.IsUp(now);
                float targetScale = up ? PadUpScale : PadDownScale;
                float s = Mathf.MoveTowards(pad.quad.localScale.x, targetScale,
                    PadScaleLerpSpeed * dt);
                pad.quad.localScale = new Vector3(s, s, 1f);

                var sr = pad.quad.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = up ? PadGoldUp : PadGoldDown;
            }
        }

        private void TickPlayer(float dt)
        {
            bool wantsRun = ReadRunInput();

            if (_overexerted)
            {
                // Stumble: forced stop, stamina recovers, input ignored. Glide
                // to a halt - races do not roll backwards.
                _overexertedTimer -= dt;
                _stamina = Mathf.Min(_maxStamina, _stamina + StaminaRegenPerSecond * dt);
                _playerSpeed = Mathf.MoveTowards(_playerSpeed, 0f, GlideDecel * dt);
                if (_overexertedTimer <= 0f) _overexerted = false;
            }
            else if (wantsRun && _stamina > 0f)
            {
                float staminaFactor = MinStaminaFactor + (1f - MinStaminaFactor) * (_stamina / _maxStamina);
                _playerSpeed = _playerRunSpeed * staminaFactor;

                float drain = StaminaDrainPerSecond;
                if (_spirit != null && _spirit.Hunger01 > 0.5f) drain *= HungryDrainMultiplier;
                _stamina -= drain * dt;

                if (_stamina <= 0f)
                {
                    _stamina = 0f;
                    _overexerted = true;
                    _overexertedTimer = OverexertedStopSeconds;
                }
            }
            else
            {
                // Glide-stop: decelerate, recover stamina, hold ground.
                _stamina = Mathf.Min(_maxStamina, _stamina + StaminaRegenPerSecond * dt);
                _playerSpeed = Mathf.MoveTowards(_playerSpeed, 0f, GlideDecel * dt);
            }

            _playerBoostTimer = Mathf.Max(0f, _playerBoostTimer - dt);
            float burst = _playerBoostTimer > 0f ? PadBoostSpeed : 0f;

            float prevX = _playerX;
            _playerX = Mathf.Min(_finishX, _playerX + (_playerSpeed + burst) * dt);

            // Pad crossings: a pad only fires while UP - that is the whole skill.
            float now = Time.time;
            for (int i = 0; i < _playerPads.Count; i++)
            {
                var pad = _playerPads[i];
                if (pad.consumed || pad.x <= prevX || pad.x > _playerX) continue;
                pad.consumed = true;
                if (pad.IsUp(now))
                {
                    _playerBoostTimer = PadBoostSeconds;
                    FloatingText.Show(new Vector3(pad.x, pad.laneY + 0.6f, 0f), "whoosh", PadGoldUp);
                }
            }
        }

        private void TickRival(Rival r, float dt)
        {
            r.phaseTimer -= dt;
            if (r.phaseTimer <= 0f)
            {
                r.running = !r.running;
                r.phaseTimer = r.running
                    ? Random.Range(r.runMin, r.runMax)
                    : Random.Range(r.restMin, r.restMax);
            }

            r.speed = r.running
                ? r.runSpeed
                : Mathf.MoveTowards(r.speed, 0f, GlideDecel * dt);

            r.boostTimer = Mathf.Max(0f, r.boostTimer - dt);
            float burst = r.boostTimer > 0f ? PadBoostSpeed : 0f;

            float prevX = r.x;
            r.x = Mathf.Min(_finishX, r.x + (r.speed + burst) * dt);

            // Rivals do not time the pads - they barrel through blindly and get
            // the burst about half the time (a coin flip stands in for luck).
            for (int i = 0; i < r.pads.Count; i++)
            {
                var pad = r.pads[i];
                if (pad.consumed || pad.x <= prevX || pad.x > r.x) continue;
                pad.consumed = true;
                if (Random.value < 0.5f) r.boostTimer = PadBoostSeconds;
            }

            if (r.body != null) r.body.position = new Vector3(r.x, r.laneY, 0f);
        }

        private Vector3 GluePosition(float runnerX) =>
            new Vector3(runnerX, _origin.y, 0f);

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
                    // Stumble flash: alternate alpha a few times a second.
                    bool bright = Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
                    c.a = bright ? 1f : 0.3f;
                }
                _staminaFill.color = c;
            }

            if (_positionText != null)
            {
                int rank = 1;
                for (int i = 0; i < _rivals.Count; i++)
                    if (_rivals[i].x > _playerX) rank++;
                _positionText.text = Ordinal(rank);
                _positionText.color = rank == 1 ? FinishGold : Color.white;
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
                case 1: FloatingText.Show(centre, "1st!", FinishGold); break;
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
                    eventName = "The Crossing",
                    placement = placement,
                    entrants = 3
                });
            }

            // The whole arena greybox is parented under this GameObject.
            Destroy(gameObject);
        }
    }
}
