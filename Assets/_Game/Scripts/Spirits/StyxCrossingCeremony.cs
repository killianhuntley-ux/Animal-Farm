using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// THE STYX CROSSING (muscle 04, verdict 4 — owner-specified beat by beat).
    /// Replaces the old light-column ascension. At the built Ascension Pad,
    /// when the player confirms with a fulfilled, following spirit:
    ///   a. gameplay blocks (modal law); the spirit drifts to the pad and
    ///      nearby residents gather into a loose ring to watch;
    ///   b. DARKNESS — the screen darkens all around EXCEPT the gathered
    ///      spirits and the pad (big black world sprite at sorting order 300;
    ///      actors and soft light pools hoisted above it);
    ///   c. A RIVER FLOWS IN — an animated water band slides in beside the
    ///      pad with CHARON on it, drifting to a stop; one low chord; he asks
    ///      for payment ("Payment, little light.");
    ///   d. PAYMENT — the departing spirit gleams out its spirit orbs: a
    ///      stream of small gold orbs arcs from spirit to Charon (~2.5s) with
    ///      soft coin bleeps. Its amassed Spirit IS the passage coin;
    ///   e. the spirit boards, the skiff slides back out into the dark and
    ///      fades; the gathered spirits chirp a staggered farewell chorus;
    ///   f. lighting restores and THE HEADSTONE DROPS at the pad — the player
    ///      is handed a free placement ghost to carry it where it should rest
    ///      (right-click keeps it at the pad; it can be moved again any time
    ///      via the stone's "Move stone" action).
    /// Total ~20-25s. Esc skips: the Pause press is intercepted (GameManager
    /// subscribed to PausePressed first, so its toggle is undone here) and the
    /// crossing jumps straight to the committed end state. No direct device
    /// reads — the skip rides the GameInput event, honoring UIInputLock law
    /// (TextInputActive still wins: typing in the console never skips).
    ///
    /// SAVE POLICY (chosen: "not started"): the ceremony itself is never
    /// saved. Every permanent mutation — headstone creation and
    /// SpiritManager.Ascend — commits in ONE block at the very end (or on
    /// skip). A save or quit at any earlier point restores as "ceremony never
    /// started": the spirit is simply a fulfilled resident standing near the
    /// pad, and nothing is lost or duplicated.
    /// </summary>
    public class StyxCrossingCeremony : MonoBehaviour
    {
        public static bool Running { get; private set; }

        // ---- timings (scaled seconds; pausing freezes the ceremony) ----------
        private const float ArriveTimeout = 5f;
        private const float ArriveRadius = 0.25f;
        private const float DarkFadeIn = 1.6f;
        private const float DarkAlpha = 0.88f;
        private const float RiverSlideIn = 2.2f;
        private const float CharonDriftIn = 2.6f;
        private const float PaymentHold = 1.6f;
        private const int OrbCount = 9;
        private const float OrbStagger = 0.2f;
        private const float OrbFlight = 0.85f;
        private const float BoardTimeout = 3f;
        private const float SailOut = 2.6f;
        private const float AfterChorusHold = 0.9f;
        private const float DarkFadeOut = 1.4f;

        // ---- sorting ladder (overlay world; FloatingText sits at 500) --------
        private const int OrderOverlay = 300;
        private const int OrderPool = 301;
        private const int OrderRiver = 302;   // +1 for the second water layer
        private const int OrderSkiff = 306;
        private const int OrderOrb = 307;
        private const int OrderActor = 310;

        private static readonly Color CharonText = new Color(0.70f, 0.75f, 0.85f, 1f);
        private static readonly Color OrbGold = new Color(1f, 0.88f, 0.5f, 1f);
        private static readonly Color PadPool = new Color(1f, 0.92f, 0.65f, 0.55f);
        private static readonly Color SpiritPool = new Color(0.8f, 0.88f, 1f, 0.45f);

        private AscensionPad _pad;
        private SpiritAgent _spirit;
        private readonly List<SpiritAgent> _gathered = new List<SpiritAgent>();
        private readonly List<(SpriteRenderer sr, int order)> _lifted =
            new List<(SpriteRenderer, int)>();
        private readonly List<GameObject> _pools = new List<GameObject>();

        private SpriteRenderer _overlay;
        private Transform _river;
        private SpriteRenderer _riverA, _riverB;
        private SpriteRenderer _skiff;
        private bool _skip;
        private bool _committed;
        private bool _finished;
        private bool _pauseHooked;
        private Headstone _stone;
        private int _witnessCount;

        private Vector3 PadCenter => _pad != null ? _pad.PlatformCenter : transform.position;
        private Vector3 RiverRest => PadCenter + new Vector3(0f, -1.9f, 0f);
        private Vector3 BoatStop => PadCenter + new Vector3(1.7f, -1.45f, 0f);

        /// <summary>Entry point: the pad validated the pair (fulfilled + near).</summary>
        public static void Begin(AscensionPad pad, SpiritAgent spirit)
        {
            if (Running || pad == null || spirit == null || !spirit.IsFulfilled) return;

            // Never two ceremonies at once (a naming or weave rite is playing).
            if (NamingCeremony.Running || WeaveRiteCeremony.Running)
            {
                Debug.Log("[Styx] Crossing refused: another ceremony is playing.");
                Bleeps.Play(BleepKind.Denied, 0.8f);
                FloatingText.Show(pad.PlatformCenter + Vector3.up * 1.4f,
                    "(not now)", UIStyle.Grey);
                return;
            }

            var go = new GameObject("StyxCrossing");
            go.transform.position = pad.PlatformCenter;
            var c = go.AddComponent<StyxCrossingCeremony>();
            c._pad = pad;
            c._spirit = spirit;
            c.StartCoroutine(c.Run());
        }

        // ---- the crossing -----------------------------------------------------

        private IEnumerator Run()
        {
            Running = true;

            // The crossing deserves a quiet bed: duck the ambient music for
            // roughly the ceremony's length.
            if (AnimalFarm.Core.AmbientMusic.Instance != null)
                AnimalFarm.Core.AmbientMusic.Instance.DuckFor(25f);

            // Modal law: block gameplay for the whole crossing.
            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null)
            {
                GameInput.Instance.SetGameplayBlocked(true);
                GameInput.Instance.PausePressed += OnPausePressed;
                _pauseHooked = true;
            }

            // a. The spirit drifts to the pad; witnesses gather in a loose ring.
            _spirit.SetFollowing(false);
            _spirit.EnterCeremony(PadCenter);
            GatherWitnesses();

            float deadline = Time.time + ArriveTimeout;
            while (!_skip && _spirit != null && Time.time < deadline
                   && (_spirit.transform.position - PadCenter).sqrMagnitude
                       > ArriveRadius * ArriveRadius)
                yield return null;

            if (_spirit == null) { Finish(); yield break; }

            // b. DARKNESS: everything goes black except the gathered and the pad.
            BuildDarkness();
            yield return FadeOverlay(0f, DarkAlpha, DarkFadeIn);
            if (_skip) { Finish(); yield break; }

            // c. A river flows in, Charon on it.
            BuildRiver();
            yield return RiverFlowsIn();
            if (_skip) { Finish(); yield break; }

            yield return CharonArrives();
            if (_skip) { Finish(); yield break; }

            if (_skiff != null)
                FloatingText.Show(_skiff.transform.position + Vector3.up * 1.6f,
                    "Payment, little light.", CharonText);
            yield return WaitBeat(PaymentHold);
            if (_skip) { Finish(); yield break; }

            // d. PAYMENT: the spirit gleams out its spirit orbs to Charon.
            yield return OrbStream();
            if (_skip || _spirit == null) { Finish(); yield break; }

            // e. Boarding, then the skiff slides back out into the dark.
            _spirit.EnterCeremony(BoatStop + new Vector3(-0.3f, 0.55f, 0f));
            deadline = Time.time + BoardTimeout;
            while (!_skip && _spirit != null && Time.time < deadline
                   && (_spirit.transform.position
                       - (BoatStop + new Vector3(-0.3f, 0.55f, 0f))).sqrMagnitude > 0.09f)
                yield return null;
            if (_skip) { Finish(); yield break; }

            StartCoroutine(FarewellChorus());
            yield return SailOff();
            if (_skip) { Finish(); yield break; }

            yield return WaitBeat(AfterChorusHold);

            // f. Lighting restores; the headstone drops (inside Finish).
            yield return FadeOverlay(DarkAlpha, 0f, DarkFadeOut);
            Finish();
        }

        /// <summary>Skip-aware scaled wait.</summary>
        private IEnumerator WaitBeat(float seconds)
        {
            for (float t = 0f; t < seconds && !_skip; t += Time.deltaTime)
                yield return null;
        }

        // ---- beat a: witnesses ------------------------------------------------

        /// <summary>
        /// Drifts the 3-4 nearest OTHER residents into a loose ring on the far
        /// side of the pad from the river (reuses EnterCeremony — they lock,
        /// watch, and ExitCeremony releases them at the end).
        /// </summary>
        private void GatherWitnesses()
        {
            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            var candidates = new List<SpiritAgent>();
            var spirits = mgr.AllSpirits;
            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || a == _spirit || a.State != SpiritState.Resident) continue;
                if ((a.transform.position - PadCenter).sqrMagnitude > 14f * 14f) continue;
                candidates.Add(a);
            }
            candidates.Sort((x, y) =>
                (x.transform.position - PadCenter).sqrMagnitude
                    .CompareTo((y.transform.position - PadCenter).sqrMagnitude));

            float[] angles = { 115f, 65f, 150f, 30f }; // upper arc, river comes in below
            int take = Mathf.Min(4, candidates.Count);
            for (int i = 0; i < take; i++)
            {
                var g = candidates[i];
                float ang = (angles[i] + Random.Range(-8f, 8f)) * Mathf.Deg2Rad;
                Vector3 ring = PadCenter + new Vector3(
                    Mathf.Cos(ang), Mathf.Sin(ang), 0f) * (2.3f + Random.Range(-0.2f, 0.3f));
                g.SetFollowing(false);
                g.EnterCeremony(ring);
                _gathered.Add(g);
            }
        }

        // ---- beat b: darkness ---------------------------------------------------

        private void BuildDarkness()
        {
            // Big black world sprite over everything ordinary (labels at 200,
            // actors at 0) but under the lifted actors and FloatingText (500).
            var go = new GameObject("Darkness");
            go.transform.SetParent(transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            _overlay.sprite = SquareSprite;
            _overlay.sortingOrder = OrderOverlay;
            _overlay.color = new Color(0.01f, 0.01f, 0.04f, 0f);
            go.transform.localScale = new Vector3(400f, 400f, 1f);

            // Soft light pools: pad (warm) + each gathered light (pale blue).
            MakePool(PadCenter, 4.4f, PadPool, null);
            LiftActor(_spirit, true);
            foreach (var g in _gathered) LiftActor(g, true);

            // The shepherd stands inside the gleam too.
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                foreach (var sr in player.GetComponentsInChildren<SpriteRenderer>())
                {
                    _lifted.Add((sr, sr.sortingOrder));
                    sr.sortingOrder = OrderActor + sr.sortingOrder;
                }
                MakePool(Vector3.zero, 2.6f, SpiritPool, player.transform);
            }

            if (_pad != null) _pad.SetCeremonyLift(true);
        }

        /// <summary>Hoists one spirit's body above the overlay + gives it a pool.</summary>
        private void LiftActor(SpiritAgent a, bool pool)
        {
            if (a == null || a.Renderer == null) return;
            _lifted.Add((a.Renderer, a.Renderer.sortingOrder));
            a.Renderer.sortingOrder = OrderActor;
            if (pool) MakePool(Vector3.zero, 2.3f, SpiritPool, a.transform);
        }

        private void MakePool(Vector3 pos, float scale, Color color, Transform follow)
        {
            var go = new GameObject("LightPool");
            if (follow != null)
            {
                go.transform.SetParent(follow, false);
                // Spirits run a 1.6x root scale — keep pools world-sized.
                float parentScale = Mathf.Max(0.01f, follow.lossyScale.x);
                scale /= parentScale;
            }
            else
            {
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
            }
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AscensionPad.MakeRadialGlowSprite(64, 32f);
            sr.sortingOrder = OrderPool;
            sr.color = color;
            go.transform.localScale = new Vector3(scale, scale * 0.8f, 1f);
            _pools.Add(go);
        }

        private IEnumerator FadeOverlay(float from, float to, float duration)
        {
            for (float t = 0f; t < duration && !_skip; t += Time.deltaTime)
            {
                SetOverlayAlpha(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetOverlayAlpha(to);
        }

        private void SetOverlayAlpha(float a)
        {
            if (_overlay == null) return;
            var c = _overlay.color;
            _overlay.color = new Color(c.r, c.g, c.b, a);
        }

        // ---- beat c: the river and Charon --------------------------------------

        private void BuildRiver()
        {
            var root = new GameObject("River");
            root.transform.SetParent(transform, false);
            root.transform.position = RiverRest;
            _river = root.transform;

            _riverA = MakeWaterLayer(root.transform, 0, 0f);
            _riverB = MakeWaterLayer(root.transform, 1, 0.5f);

            // Charon and his skiff ride the river root.
            var skiffGo = new GameObject("Charon");
            skiffGo.transform.SetParent(root.transform, false);
            skiffGo.transform.localPosition = new Vector3(-16f, 0.45f, 0f);
            _skiff = skiffGo.AddComponent<SpriteRenderer>();
            _skiff.sprite = SkiffSprite;
            _skiff.sortingOrder = OrderSkiff;
            _skiff.color = new Color(1f, 1f, 1f, 0f);
        }

        private SpriteRenderer MakeWaterLayer(Transform parent, int layer, float phase)
        {
            var go = new GameObject("Water" + layer);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(phase, layer * 0.12f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RiverBandSprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(30f, 2.3f - layer * 0.5f);
            sr.sortingOrder = OrderRiver + layer;
            sr.color = new Color(1f, 1f, 1f, 0f);
            return sr;
        }

        /// <summary>The water band slides in from the left while fading up.</summary>
        private IEnumerator RiverFlowsIn()
        {
            Vector3 from = RiverRest + new Vector3(-24f, 0f, 0f);
            for (float t = 0f; t < RiverSlideIn && !_skip; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / RiverSlideIn);
                if (_river != null) _river.position = Vector3.Lerp(from, RiverRest, k);
                SetWaterAlpha(k * 0.92f);
                AnimateWater();
                yield return null;
            }
            if (_river != null) _river.position = RiverRest;
            SetWaterAlpha(0.92f);
        }

        /// <summary>Charon drifts to a stop beside the pad; one low chord.</summary>
        private IEnumerator CharonArrives()
        {
            Vector3 fromLocal = new Vector3(-16f, 0.45f, 0f);
            Vector3 toLocal = _river != null
                ? _river.InverseTransformPoint(BoatStop) : Vector3.zero;

            for (float t = 0f; t < CharonDriftIn && !_skip; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / CharonDriftIn);
                if (_skiff != null)
                {
                    var p = Vector3.Lerp(fromLocal, toLocal, k);
                    p.y += Mathf.Sin(Time.time * 2.2f) * 0.05f; // gentle ride
                    _skiff.transform.localPosition = p;
                    _skiff.color = new Color(1f, 1f, 1f, Mathf.Min(1f, k * 2f));
                }
                AnimateWater();
                yield return null;
            }
            if (_skiff != null)
            {
                _skiff.transform.position = BoatStop;
                _skiff.color = Color.white;
            }
            if (!_skip) PlayCharonChord();
        }

        /// <summary>Cheap living water: the two layers breathe out of phase.</summary>
        private void AnimateWater()
        {
            if (_riverA != null)
                _riverA.transform.localPosition =
                    new Vector3(Mathf.Sin(Time.time * 0.7f) * 0.35f, 0f, 0f);
            if (_riverB != null)
                _riverB.transform.localPosition =
                    new Vector3(Mathf.Sin(Time.time * 0.9f + 2.1f) * 0.45f, 0.12f, 0f);
        }

        private void SetWaterAlpha(float a)
        {
            if (_riverA != null) _riverA.color = new Color(1f, 1f, 1f, a);
            if (_riverB != null) _riverB.color = new Color(1f, 1f, 1f, a * 0.8f);
        }

        // ---- beat d: payment ------------------------------------------------------

        /// <summary>
        /// A stream of small gold orbs arcs spirit -> Charon, staggered, each
        /// landing with a soft coin bleep. The spirit dims slightly as its
        /// amassed Spirit leaves it.
        /// </summary>
        private IEnumerator OrbStream()
        {
            if (_spirit == null || _skiff == null) yield break;

            Vector3 from = _spirit.transform.position + Vector3.up * 0.4f;
            Vector3 to = _skiff.transform.position + new Vector3(0.35f, 0.6f, 0f);
            Vector3 apex = (from + to) * 0.5f + Vector3.up * 1.4f;

            var orbs = new Transform[OrbCount];
            float total = (OrbCount - 1) * OrbStagger + OrbFlight;

            for (float t = 0f; t < total && !_skip; t += Time.deltaTime)
            {
                for (int i = 0; i < OrbCount; i++)
                {
                    float local = (t - i * OrbStagger) / OrbFlight;
                    if (local < 0f) continue;

                    if (local >= 1f)
                    {
                        if (orbs[i] != null)
                        {
                            Destroy(orbs[i].gameObject);
                            orbs[i] = null;
                            Bleeps.Play(BleepKind.Coin, 0.22f + 0.04f * (i % 3));
                        }
                        continue;
                    }

                    if (orbs[i] == null)
                    {
                        var go = new GameObject("SpiritOrb");
                        go.transform.SetParent(transform, false);
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = AscensionPad.MakeRadialGlowSprite(32, 16f);
                        sr.sortingOrder = OrderOrb;
                        sr.color = OrbGold;
                        go.transform.localScale = Vector3.one * 0.34f;
                        orbs[i] = go.transform;
                    }

                    // Quadratic bezier arc with a tiny per-orb wobble.
                    float k = Mathf.SmoothStep(0f, 1f, local);
                    Vector3 p = Bezier(from, apex, to, k);
                    p.y += Mathf.Sin((local + i) * 9f) * 0.06f;
                    orbs[i].position = p;
                }

                // The departing light dims a little as it pays.
                if (_spirit != null && _spirit.Renderer != null)
                {
                    var c = _spirit.Renderer.color;
                    c.a *= Mathf.Lerp(1f, 0.78f, t / total);
                    _spirit.Renderer.color = c;
                }
                yield return null;
            }

            for (int i = 0; i < OrbCount; i++)
                if (orbs[i] != null) Destroy(orbs[i].gameObject);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        // ---- beat e: sail-off -------------------------------------------------------

        /// <summary>The skiff (spirit aboard) slides back out and fades to dark.</summary>
        private IEnumerator SailOff()
        {
            Vector3 boatFrom = BoatStop;
            Vector3 boatTo = BoatStop + new Vector3(-20f, 0f, 0f);

            for (float t = 0f; t < SailOut && !_skip; t += Time.deltaTime)
            {
                float k = t / SailOut; // constant glide, accelerating fade
                float fade = Mathf.Clamp01(1f - (k - 0.35f) / 0.65f);

                if (_skiff != null)
                {
                    Vector3 p = Vector3.Lerp(boatFrom, boatTo, Mathf.SmoothStep(0f, 1f, k));
                    p.y += Mathf.Sin(Time.time * 2.2f) * 0.05f;
                    _skiff.transform.position = p;
                    _skiff.color = new Color(1f, 1f, 1f, fade);

                    // The spirit rides the boat (coroutines run after agent
                    // Updates, so this wins the frame — TheLoom pattern).
                    if (_spirit != null)
                    {
                        _spirit.transform.position = p + new Vector3(-0.3f, 0.55f, 0f);
                        if (_spirit.Renderer != null)
                        {
                            var c = _spirit.Renderer.color;
                            c.a *= fade;
                            _spirit.Renderer.color = c;
                        }
                    }
                }

                // The river ebbs away with them.
                SetWaterAlpha(0.92f * Mathf.Clamp01(1f - (k - 0.55f) / 0.45f));
                AnimateWater();
                yield return null;
            }
        }

        /// <summary>Staggered farewell chirps from the gathered watchers.</summary>
        private IEnumerator FarewellChorus()
        {
            for (int i = 0; i < _gathered.Count; i++)
            {
                if (_skip) yield break;
                var g = _gathered[i];
                if (g != null && g.Species != null)
                    SpiritVoice.Play(g.Species, VoiceIntent.Greet, 0.8f);
                yield return WaitBeat(Random.Range(0.18f, 0.32f));
            }
        }

        // ---- commit + cleanup -------------------------------------------------------

        /// <summary>
        /// THE one permanent-mutation block (save policy above): lay the
        /// headstone from the live agent, then Ascend (count bump + home
        /// release + despawn). Idempotent.
        /// </summary>
        private void CommitCrossing()
        {
            if (_committed) return;
            _committed = true;

            if (_spirit == null) return; // vanished mid-beat: nothing to commit

            Vector3 drop = PadCenter + new Vector3(0f, -1.05f, 0f);
            if (HeadstoneRegistry.Instance != null)
            {
                _stone = HeadstoneRegistry.Instance.CreateHeadstoneAt(_spirit, drop);
                if (_stone != null) _stone.Witnesses = _witnessCount;
            }

            if (SpiritManager.Instance != null)
                SpiritManager.Instance.Ascend(_spirit);
            else
                Destroy(_spirit.gameObject);
            _spirit = null;
        }

        /// <summary>
        /// End state (normal or skipped): commit, restore world + input, drop
        /// the stone into the player's hands. Safe to reach from any beat.
        /// </summary>
        private void Finish()
        {
            if (_finished) return;
            _finished = true;

            if (_pauseHooked && GameInput.Instance != null)
                GameInput.Instance.PausePressed -= OnPausePressed;

            // Restore sorting before anyone despawns.
            foreach (var (sr, order) in _lifted)
                if (sr != null) sr.sortingOrder = order;
            _lifted.Clear();
            foreach (var pool in _pools)
                if (pool != null) Destroy(pool);
            _pools.Clear();
            if (_pad != null) _pad.SetCeremonyLift(false);

            _witnessCount = _gathered.Count;
            foreach (var g in _gathered)
                if (g != null) g.ExitCeremony();
            _gathered.Clear();

            CommitCrossing();

            // Modal restore, pause-respecting (HomePickerUI pattern). Running goes
            // false first so the owner check below does not count this ceremony.
            Running = false;
            UIInputLock.ModalOpen = false;
            if (GameInput.Instance != null)
            {
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.NonModalOwnerHolds) GameInput.Instance.SetGameplayBlocked(false);
            }

            HeadstoneHandoff();
            NamingCeremony.TryStartPending(); // a naming queued behind the crossing goes now

            Destroy(gameObject); // overlay, river, Charon, orbs all ride this root
        }

        /// <summary>
        /// The headstone drops at the pad (already a real, saved object) and
        /// the player gets a free placement ghost to carry it to its resting
        /// place. Right-click simply leaves it at the pad; the stone's own
        /// "Move stone" action re-opens placement any time, so no save state
        /// can ever strand it.
        /// </summary>
        private void HeadstoneHandoff()
        {
            if (_stone == null) return;

            Puffs.Burst(_stone.transform.position, new Color(0.8f, 0.8f, 0.85f), 8, 1.2f, 0.4f, 0.12f);
            Bleeps.Play(BleepKind.Build, 0.8f);
            FloatingText.Show(_stone.transform.position + Vector3.up * 1.1f,
                "A stone remains. Choose where it rests.", UIStyle.Cream);

            var sc = AnimalFarm.Player.SelectionController.Instance;
            var sr = _stone.GetComponent<SpriteRenderer>();
            if (sc == null || sr == null || sr.sprite == null) return;

            var stone = _stone;
            sc.BeginPlaceBuilding(sr.sprite, stone.transform.localScale.x,
                AnimalFarm.Player.SelectionController.IsPlaceableCell,
                (cell, world) =>
                {
                    if (stone == null) return;
                    stone.transform.position = world;
                    stone.MarkPlaced(); // the garden's clock starts here
                    Bleeps.Play(BleepKind.Build, 0.6f);
                });
        }

        /// <summary>
        /// Esc = skip. GameManager's TogglePause subscribed first (scene
        /// start), so by the time this runs the game just paused — undo that
        /// and jump to the end state instead. Console typing never skips.
        /// </summary>
        private void OnPausePressed()
        {
            if (UIInputLock.TextInputActive) return;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused)
                GameManager.Instance.SetPaused(false);
            _skip = true;
        }

        private void OnDestroy()
        {
            // Hard teardown (scene unload mid-ceremony): never leave the
            // world locked. Nothing is committed here — see the save policy.
            if (!_finished)
            {
                if (_pauseHooked && GameInput.Instance != null)
                    GameInput.Instance.PausePressed -= OnPausePressed;
                foreach (var (sr, order) in _lifted)
                    if (sr != null) sr.sortingOrder = order;
                foreach (var g in _gathered)
                    if (g != null) g.ExitCeremony();
                if (_pad != null) _pad.SetCeremonyLift(false);
                Running = false;
                UIInputLock.ModalOpen = false;
                if (GameInput.Instance != null
                    && (GameManager.Instance == null || !GameManager.Instance.IsPaused)
                    && !UIInputLock.NonModalOwnerHolds)
                    GameInput.Instance.SetGameplayBlocked(false);
            }
        }

        // ---- generated sprites ---------------------------------------------------

        private static Sprite _square, _riverBand, _skiffSprite;

        /// <summary>1x1 world-unit white square (Puffs pattern).</summary>
        private static Sprite SquareSprite
        {
            get
            {
                if (_square == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _square.hideFlags = HideFlags.HideAndDontSave;
                }
                return _square;
            }
        }

        /// <summary>Dark water band with faint wavy lighter stripes (tiled).</summary>
        private static Sprite RiverBandSprite
        {
            get
            {
                if (_riverBand != null) return _riverBand;

                const int w = 48, h = 24;
                var deep = new Color(0.07f, 0.11f, 0.23f, 0.96f);
                var crest = new Color(0.15f, 0.22f, 0.40f, 0.96f);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                int[] stripeRows = { 5, 12, 19 };
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var c = deep;
                        for (int s = 0; s < stripeRows.Length; s++)
                        {
                            float wave = stripeRows[s]
                                + Mathf.Sin((x + s * 7) * (2f * Mathf.PI / w) * 2f) * 1.6f;
                            if (Mathf.Abs(y - wave) < 0.9f) { c = crest; break; }
                        }
                        tex.SetPixel(x, y, c);
                    }
                }
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.hideFlags = HideFlags.HideAndDontSave;
                _riverBand = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 12f);
                _riverBand.hideFlags = HideFlags.HideAndDontSave;
                return _riverBand;
            }
        }

        /// <summary>Charon placeholder: crescent hull, hooded figure, pole, one
        /// warm lantern dot at the prow (eerie but hopeful).</summary>
        private static Sprite SkiffSprite
        {
            get
            {
                if (_skiffSprite != null) return _skiffSprite;

                const int w = 64, h = 40;
                var hull = new Color(0.05f, 0.06f, 0.11f, 1f);
                var robe = new Color(0.07f, 0.08f, 0.14f, 1f);
                var pole = new Color(0.10f, 0.11f, 0.17f, 1f);
                var lantern = new Color(1f, 0.82f, 0.45f, 0.95f);

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var clear = new Color32[w * h];
                tex.SetPixels32(clear); // all transparent

                for (int x = 8; x <= 56; x++)
                {
                    // Hull: parabola, shallow amidships, rising at bow + stern.
                    float u = (x - 32f) / 24f;
                    int top = 15 + Mathf.RoundToInt(3f * u * u);
                    int bottom = 8 + Mathf.RoundToInt(5f * u * u);
                    for (int y = bottom; y <= top; y++)
                        if (y >= 0 && y < h) tex.SetPixel(x, y, hull);
                }

                // Hooded ferryman near the stern.
                for (int y = 16; y <= 25; y++)
                {
                    int halfW = Mathf.RoundToInt(Mathf.Lerp(5f, 2.4f, (y - 16f) / 9f));
                    for (int x = 40 - halfW; x <= 40 + halfW; x++)
                        tex.SetPixel(x, y, robe);
                }
                for (int y = 24; y <= 30; y++)       // the hood
                    for (int x = 37; x <= 43; x++)
                        if ((x - 40) * (x - 40) + (y - 27) * (y - 27) <= 12)
                            tex.SetPixel(x, y, robe);

                for (int y = 11; y <= 36; y++)       // the pole
                { tex.SetPixel(47, y, pole); tex.SetPixel(48, y, pole); }

                for (int y = 17; y <= 20; y++)       // prow lantern
                    for (int x = 11; x <= 14; x++)
                        if ((x - 12) * (x - 12) + (y - 18) * (y - 18) <= 3)
                            tex.SetPixel(x, y, lantern);

                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                _skiffSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.35f), 18f);
                _skiffSprite.hideFlags = HideFlags.HideAndDontSave;
                return _skiffSprite;
            }
        }

        // ---- Charon's chord --------------------------------------------------------

        private static AudioClip _chord;

        /// <summary>One low minor chord (A2 / C3 / E3), Bleeps house style:
        /// synthesized once, soft (sum peak ~0.26), slow swell and long tail.</summary>
        private static void PlayCharonChord()
        {
            if (Bleeps.Muted || !Application.isPlaying) return;
            if (!AudioGuard.TryPlay(AudioBus.Sfx, "charon_chord", 1f, 1f)) return; // central gate

            if (_chord == null)
            {
                const int rate = 44100;
                const float seconds = 1.7f;
                float[] freqs = { 110f, 130.8f, 164.8f };
                var d = new float[Mathf.CeilToInt(seconds * rate)];
                for (int n = 0; n < freqs.Length; n++)
                {
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)rate;
                        float attack = Mathf.Clamp01(t / 0.14f);
                        float release = Mathf.Exp(-6f * t / 1.5f);
                        d[i] += 0.085f * Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * attack * release;
                    }
                }
                _chord = AudioClip.Create("CharonChord", d.Length, 1, rate, false);
                _chord.SetData(d, 0);
            }

            var go = new GameObject("CharonChord");
            go.hideFlags = HideFlags.HideInHierarchy;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.PlayOneShot(_chord, 1f);
            Destroy(go, 2.5f);
        }
    }
}
