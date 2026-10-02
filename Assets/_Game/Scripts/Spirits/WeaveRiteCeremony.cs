using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// THE NIGHT LOOM RITE (muscle 06, verdict 1). Replaces the old menu-swirl
    /// weave. After the WeaveUI validates a pair:
    ///   a. gameplay blocks (modal law); both parents walk to either side of
    ///      the Loom;
    ///   b. the world goes to night - a deep blue-black overlay darkens
    ///      everything EXCEPT the parents, the shepherd and the Loom;
    ///   c. each parent UNRAVELS - the body thins and stretches away while a
    ///      thread in its species' palette color is pulled up toward the loom;
    ///   d. the Loom WEAVES the two threads in the air: a cloth unrolls from
    ///      the loom top as a glowing shuttle zigzags across it, the cloth
    ///      woven from both thread colors (it IS the tapestry banner's art);
    ///   e. the cloth completes (one soft chord) and the weave COMMITS
    ///      (essence, inheritance, the cryptid, the archive record + banner);
    ///   f. the new cryptid STEPS OUT of the finished cloth;
    ///   g. the cloth folds itself down into a banner and drifts to the pouch;
    ///   h. the light returns, and the NAMING CEREMONY (muscle 03 "light
    ///      descends") opens with a blended suggestion of the parents' names.
    /// Instant weave - no gestation. Total ~16s. Esc skips: the Pause press is
    /// intercepted exactly like the Styx crossing and the rite jumps to the
    /// committed end state.
    ///
    /// ASSUMPTION: the verdict says "night loom rite" but not that weaving is
    /// gated to night. The rite is allowed at any hour and always plays in a
    /// night look (the overlay), so no new gate or daylight exception exists.
    ///
    /// SAVE POLICY ("not started", like the Styx crossing): the rite itself is
    /// never saved. Every permanent mutation commits in ONE block
    /// (SpiritManager.Weave, via CommitWeave). A save or quit before that
    /// restores as "rite never started": both parents are simply full-spirit
    /// residents standing near the loom, and nothing is lost or duplicated.
    /// </summary>
    public class WeaveRiteCeremony : MonoBehaviour
    {
        public static bool Running { get; private set; }

        // ---- timings (scaled seconds; pausing freezes the rite) ------------------
        private const float ArriveTimeout = 6f;
        private const float ArriveRadius = 0.3f;
        private const float ApproachOffset = 1.2f;
        private const float DarkFadeIn = 1.5f;
        private const float DarkAlpha = 0.84f;
        private const float UnravelSeconds = 2.6f;
        private const float WeaveSeconds = 3.6f;
        private const float WholeHold = 0.9f;
        private const float StepOutSeconds = 2.2f;
        private const float FoldSeconds = 1.2f;
        private const float DarkFadeOut = 1.3f;

        // ---- cloth geometry (world units) ------------------------------------------
        private const float ClothScale = 1.9f;       // banner sprite is 1.0 x 1.5 at scale 1
        private const float ClothTopOffset = 2.7f;   // cloth rod height above the loom center
        private const float ClothUnitHeight = 1.5f;
        private const float ChildStartOffset = 0.9f; // child begins inside the cloth
        private const float ChildEndOffset = -1.7f;  // and ends below the loom

        // ---- sorting ladder (overlay world; FloatingText sits at 500) ----------------
        private const int OrderOverlay = 300;
        private const int OrderPool = 301;
        private const int OrderLoom = 304;
        private const int OrderChildBehind = 305;
        private const int OrderCloth = 306;
        private const int OrderThread = 308;
        private const int OrderMote = 309;
        private const int OrderActor = 310;
        private const int OrderPuff = 320;       // puffs must read over the night overlay

        private static readonly Color NightColor = new Color(0.02f, 0.02f, 0.08f, 1f);
        private static readonly Color LoomPool = new Color(0.78f, 0.70f, 1f, 0.5f);
        private static readonly Color SpiritPool = new Color(0.8f, 0.88f, 1f, 0.4f);
        private static readonly Color RiteText = new Color(0.82f, 0.78f, 0.95f, 1f);

        private TheLoom _loom;
        private SpiritAgent _a, _b, _child;
        private WeaveRecipe _recipe;
        private WeaveArchive.WeaveRecord _record;
        private string _nameA, _nameB;      // given names only (blank = unnamed) for the name blend
        private Color _colA, _colB;         // palette tints (banner colors)
        private Color _threadA, _threadB;   // brightened so they read in the dark

        private readonly List<(SpriteRenderer sr, int order)> _lifted =
            new List<(SpriteRenderer, int)>();
        private readonly List<GameObject> _pools = new List<GameObject>();

        private SpriteRenderer _overlay;
        private Transform _cloth;
        private SpriteRenderer _clothRenderer;
        private Thread _threadLineA, _threadLineB;
        private SpriteRenderer _moteA, _moteB, _shuttle;
        private SpriteRenderer _loomPool;

        private bool _skip;
        private bool _committed;
        private bool _finished;
        private bool _pauseHooked;
        private int _clothSeed = 1;

        private Vector3 Center => _loom != null ? _loom.transform.position : transform.position;
        private Vector3 PointA => Center + Vector3.left * ApproachOffset;
        private Vector3 PointB => Center + Vector3.right * ApproachOffset;
        private Vector3 ClothTop => Center + Vector3.up * ClothTopOffset;
        private Vector3 ChildFinalPos => Center + Vector3.up * ChildEndOffset;

        // ---- entry ----------------------------------------------------------------------

        /// <summary>Entry point (TheLoom.RunWeave). False = the rite did not start (nothing changed).</summary>
        public static bool Begin(TheLoom loom, SpiritAgent a, SpiritAgent b)
        {
            if (Running || loom == null || a == null || b == null || a == b) return false;
            if (NamingCeremony.Running || StyxCrossingCeremony.Running) return false;
            var competition = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (competition != null && competition.EventRunning) return false; // an event owns the world

            var mgr = SpiritManager.Instance;
            if (mgr == null) return false;
            if (!mgr.CanWeave(a, b, out var recipe, out string reason))
            {
                Bleeps.Play(BleepKind.Denied, 0.8f);
                FloatingText.Show(loom.transform.position + Vector3.up * 1.4f,
                    "(" + (reason ?? "the loom is silent") + ")", UIStyle.Grey);
                return false;
            }

            var go = new GameObject("WeaveRite");
            go.transform.position = loom.transform.position;
            var c = go.AddComponent<WeaveRiteCeremony>();
            c._loom = loom;
            c._a = a;
            c._b = b;
            c._recipe = recipe;
            c._nameA = a.GivenName;
            c._nameB = b.GivenName;
            c._colA = SpiritManager.ThreadColor(a.Species);
            c._colB = SpiritManager.ThreadColor(b.Species);
            c._threadA = Color.Lerp(c._colA, Color.white, 0.3f);
            c._threadB = Color.Lerp(c._colB, Color.white, 0.3f);
            c._clothSeed = WeaveArchive.Instance != null ? WeaveArchive.Instance.NextId : 1;
            Running = true; // claim now, before the coroutine's first frame
            c.StartCoroutine(c.Run());
            return true;
        }

        // ---- the rite ---------------------------------------------------------------------

        private IEnumerator Run()
        {
            try
            {
                // A quiet bed under the whole rite.
                if (AmbientMusic.Instance != null) AmbientMusic.Instance.DuckFor(24f);

                // Modal law: block gameplay for the whole rite.
                UIInputLock.ModalOpen = true;
                if (GameInput.Instance != null)
                {
                    GameInput.Instance.SetGameplayBlocked(true);
                    GameInput.Instance.PausePressed += OnPausePressed;
                    _pauseHooked = true;
                }

                // a. Both parents walk to their side of the loom.
                _a.SetFollowing(false);
                _b.SetFollowing(false);
                _a.EnterCeremony(PointA);
                _b.EnterCeremony(PointB);

                float deadline = Time.time + ArriveTimeout;
                while (!_skip && Time.time < deadline && _a != null && _b != null
                       && ((_a.transform.position - PointA).sqrMagnitude > ArriveRadius * ArriveRadius
                           || (_b.transform.position - PointB).sqrMagnitude > ArriveRadius * ArriveRadius))
                    yield return null;

                if (_a == null || _b == null) { Finish(); yield break; }
                if (_skip) { Finish(); yield break; }

                // b. Night falls on everything but the three of them and the loom.
                BuildDarkness();
                yield return FadeOverlay(0f, DarkAlpha, DarkFadeIn);
                if (_skip || _a == null || _b == null) { Finish(); yield break; }

                FloatingText.Show(Center + Vector3.up * 1.5f, "The threads come loose...", RiteText);
                Bleeps.Play(BleepKind.Weave, 0.7f);
                SpiritVoice.Play(_a.Species, VoiceIntent.Sleep, 0.45f);
                SpiritVoice.Play(_b.Species, VoiceIntent.Sleep, 0.45f);

                // c. Unravel into two colored threads.
                yield return Unravel();
                if (_skip || _a == null || _b == null) { Finish(); yield break; }

                // d. The loom weaves them in the air.
                yield return Weave();
                if (_skip) { Finish(); yield break; }

                // e. The cloth is whole; the weave commits.
                PlayWeaveChord();
                Puffs.Burst(ClothCenter(), _threadA, 10, 1.6f, 0.7f, 0.12f, OrderPuff);
                Puffs.Burst(ClothCenter(), _threadB, 10, 1.6f, 0.7f, 0.12f, OrderPuff);
                FloatingText.Show(ClothTop + Vector3.up * 0.4f, "The cloth is whole.", RiteText);
                yield return WaitBeat(WholeHold);
                if (_skip) { Finish(); yield break; }

                CommitWeave();
                if (_child == null) { Finish(); yield break; } // the loom stayed silent

                // f. The cryptid steps out of the finished cloth.
                yield return StepOut();
                if (_skip) { Finish(); yield break; }

                // g. The cloth folds down into a banner for the pouch.
                yield return FoldCloth();
                if (_skip) { Finish(); yield break; }

                // h. Light returns; Finish hands the newcomer to the naming ceremony.
                yield return FadeOverlay(DarkAlpha, 0f, DarkFadeOut);
                Finish();
            }
            finally
            {
                // Coroutine stopped or faulted mid-rite: never leave the world locked.
                if (!_finished) Finish();
            }
        }

        // ---- per-frame upkeep ----------------------------------------------------------------------

        private void Update()
        {
            if (_finished) return;

            // Re-assert the lock every frame (mirrors NamingCeremony): another UI
            // closing mid-rite (console, menus) must not hand gameplay input back.
            UIInputLock.ModalOpen = true;
            if (GameInput.Instance != null) GameInput.Instance.SetGameplayBlocked(true);
        }

        /// <summary>Skip-aware scaled wait.</summary>
        private IEnumerator WaitBeat(float seconds)
        {
            for (float t = 0f; t < seconds && !_skip; t += Time.deltaTime)
                yield return null;
        }

        private Vector3 ClothCenter() => ClothTop + Vector3.down * (ClothUnitHeight * ClothScale * 0.5f);

        // ---- beat b: darkness -----------------------------------------------------------------

        private void BuildDarkness()
        {
            var go = new GameObject("Darkness");
            go.transform.SetParent(transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            _overlay.sprite = SquareSprite;
            _overlay.sortingOrder = OrderOverlay;
            _overlay.color = new Color(NightColor.r, NightColor.g, NightColor.b, 0f);
            go.transform.localScale = new Vector3(400f, 400f, 1f);

            // The loom stays visible: a violet pool under it, its body hoisted above the dark.
            _loomPool = MakePool(Center + Vector3.up * 0.8f, 4.6f, LoomPool, null);
            if (_loom != null && _loom.BodyRenderer != null)
                Lift(_loom.BodyRenderer, OrderLoom);

            Lift(_a.Renderer, OrderActor);
            Lift(_b.Renderer, OrderActor);
            MakePool(Vector3.zero, 2.2f, Tinted(SpiritPool, _colA), _a.transform);
            MakePool(Vector3.zero, 2.2f, Tinted(SpiritPool, _colB), _b.transform);

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
        }

        private static Color Tinted(Color pool, Color tint)
        {
            var c = Color.Lerp(pool, tint, 0.45f);
            c.a = pool.a;
            return c;
        }

        private void Lift(SpriteRenderer sr, int order)
        {
            if (sr == null) return;
            _lifted.Add((sr, sr.sortingOrder));
            sr.sortingOrder = order;
        }

        private SpriteRenderer MakePool(Vector3 pos, float scale, Color color, Transform follow)
        {
            var go = new GameObject("LightPool");
            if (follow != null)
            {
                go.transform.SetParent(follow, false);
                // Spirits run a 1.6x root scale - keep pools world-sized.
                scale /= Mathf.Max(0.01f, follow.lossyScale.x);
            }
            else
            {
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
            }
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GlowSprite;
            sr.sortingOrder = OrderPool;
            sr.color = color;
            go.transform.localScale = new Vector3(scale, scale * 0.8f, 1f);
            _pools.Add(go);
            return sr;
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

        // ---- beat c: unravel ---------------------------------------------------------------------

        private IEnumerator Unravel()
        {
            _threadLineA = new Thread(transform, 22, OrderThread);
            _threadLineB = new Thread(transform, 22, OrderThread);
            _moteA = MakeMote(_threadA);
            _moteB = MakeMote(_threadB);

            float puffClock = 0f;
            for (float t = 0f; t < UnravelSeconds && !_skip; t += Time.deltaTime)
            {
                if (_a == null || _b == null) yield break;
                float k = Mathf.Clamp01(t / UnravelSeconds);
                float e = Mathf.SmoothStep(0f, 1f, k);

                UnravelOne(_a, PointA, _threadLineA, _moteA, _threadA, 0f, k, e);
                UnravelOne(_b, PointB, _threadLineB, _moteB, _threadB, 1.7f, k, e);

                puffClock += Time.deltaTime;
                if (puffClock > 0.45f)
                {
                    puffClock = 0f;
                    Puffs.Burst(_a.transform.position + Vector3.up * 0.4f, _threadA, 3, 0.8f, 0.5f, 0.07f, OrderPuff);
                    Puffs.Burst(_b.transform.position + Vector3.up * 0.4f, _threadB, 3, 0.8f, 0.5f, 0.07f, OrderPuff);
                }
                yield return null;
            }

            // Bodies fully gone; the threads remain as glowing motes.
            if (_a != null) { SetAlpha(_a, 0f); }
            if (_b != null) { SetAlpha(_b, 0f); }
        }

        private void UnravelOne(SpiritAgent agent, Vector3 point, Thread line, SpriteRenderer mote,
            Color thread, float phase, float k, float e)
        {
            // The body thins, stretches and fades (set after the agent's own
            // Update - coroutines resume later in the frame, the TheLoom pattern).
            SetAlpha(agent, Mathf.Pow(1f - k, 1.5f));
            if (agent.Renderer != null)
                agent.Renderer.transform.localScale = new Vector3(1f - 0.7f * e, 1f + 0.45f * e, 1f);
            agent.transform.position = point + Vector3.up * (0.3f * e);

            // The thread is pulled from the body up toward the cloth rod.
            Vector3 anchor = point + Vector3.up * (0.5f + 0.2f * e);
            Vector3 hub = ClothTop;
            Vector3 tip = Bezier(anchor, anchor + Vector3.up * 1.4f + (hub - anchor) * 0.25f, hub, e);
            line.Draw(anchor, tip, -0.15f, 0.14f, Time.time * 3f + phase, thread, 0.07f, 1f, 1f);

            mote.transform.position = anchor;
            mote.transform.localScale = Vector3.one * (0.3f + 0.2f * Mathf.Sin(Time.time * 6f + phase));
            mote.color = new Color(thread.r, thread.g, thread.b, 0.35f + 0.55f * e);
        }

        // ---- beat d: weave --------------------------------------------------------------------------

        private IEnumerator Weave()
        {
            BuildCloth();
            _shuttle = MakeMote(Color.white);

            float halfW = 0.5f * ClothScale; // cloth is 1.0 wide at scale 1
            for (float t = 0f; t < WeaveSeconds && !_skip; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / WeaveSeconds);
                float e = Mathf.SmoothStep(0f, 1f, k);

                // The cloth unrolls from the rod downward.
                if (_cloth != null)
                    _cloth.localScale = new Vector3(ClothScale, ClothScale * Mathf.Max(0.001f, e), 1f);
                float edgeY = ClothTop.y - ClothUnitHeight * ClothScale * e;

                // The shuttle zigzags across the leading edge, flashing between the colors.
                float swing = Mathf.Sin(t * 8f);
                Vector3 shuttle = new Vector3(Center.x + swing * halfW * 0.85f, edgeY, 0f);
                _shuttle.transform.position = shuttle;
                _shuttle.transform.localScale = Vector3.one * 0.34f;
                _shuttle.color = Color.Lerp(_threadA, _threadB, 0.5f + 0.5f * swing);

                // Both threads feed the shuttle from where the spirits were.
                float fade = Mathf.Clamp01((1f - k) / 0.15f); // threads vanish as the cloth finishes
                Vector3 anchorA = PointA + Vector3.up * 0.9f;
                Vector3 anchorB = PointB + Vector3.up * 0.9f;
                _threadLineA.Draw(anchorA, shuttle, 0.9f * (1f - 0.5f * e), 0.1f, t * 4f, _threadA, 0.07f, fade, 1f);
                _threadLineB.Draw(anchorB, shuttle, 0.9f * (1f - 0.5f * e), 0.1f, t * 4f + 1.7f, _threadB, 0.07f, fade, 1f);

                float moteAlpha = Mathf.Lerp(0.9f, 0.15f, e);
                _moteA.transform.position = anchorA;
                _moteB.transform.position = anchorB;
                _moteA.color = new Color(_threadA.r, _threadA.g, _threadA.b, moteAlpha);
                _moteB.color = new Color(_threadB.r, _threadB.g, _threadB.b, moteAlpha);
                _moteA.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.15f, e);
                _moteB.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.15f, e);

                // The loom's pool brightens as the cloth grows.
                if (_loomPool != null)
                {
                    var c = LoomPool;
                    c.a = LoomPool.a * (1f + 0.8f * e);
                    _loomPool.color = c;
                }
                yield return null;
            }

            // Cloth fully unrolled; threads and shuttle are spent.
            if (_cloth != null) _cloth.localScale = new Vector3(ClothScale, ClothScale, 1f);
            if (_threadLineA != null) _threadLineA.Hide();
            if (_threadLineB != null) _threadLineB.Hide();
            if (_shuttle != null) _shuttle.color = new Color(1f, 1f, 1f, 0f);
            if (_moteA != null) _moteA.color = new Color(1f, 1f, 1f, 0f);
            if (_moteB != null) _moteB.color = new Color(1f, 1f, 1f, 0f);
        }

        private void BuildCloth()
        {
            var go = new GameObject("Cloth");
            go.transform.SetParent(transform, false);
            go.transform.position = ClothTop;
            go.transform.localScale = new Vector3(ClothScale, 0.001f, 1f);
            _cloth = go.transform;
            _clothRenderer = go.AddComponent<SpriteRenderer>();
            _clothRenderer.sprite = TapestrySprites.Make(_colA, _colB, _clothSeed, true);
            _clothRenderer.sortingOrder = OrderCloth;
        }

        // ---- commit -------------------------------------------------------------------------------------

        /// <summary>
        /// THE one permanent-mutation block (save policy above): the essence
        /// toll, inheritance, the cryptid, the archive record + banner, the
        /// parents consumed. Idempotent. A failed commit (essence gone,
        /// parent lost) frees the parents and leaves everything as it was.
        /// </summary>
        private void CommitWeave()
        {
            if (_committed) return;
            _committed = true;

            var mgr = SpiritManager.Instance;
            if (mgr == null || _a == null || _b == null)
            {
                ReleaseParents();
                return;
            }

            Vector3 spawn = Center + Vector3.up * ChildStartOffset;
            _child = mgr.Weave(_a, _b, spawn, out _record);
            if (_child == null)
            {
                FloatingText.Show(Center + Vector3.up * 1.1f, "(the loom is silent)", UIStyle.Grey);
                ReleaseParents();
                return;
            }

            // The parents are gone (consumed + despawned).
            _a = null;
            _b = null;

            // The newcomer waits invisible inside the cloth, locked until its naming.
            _child.SetFollowing(false);
            _child.EnterCeremony(spawn);
            SetAlpha(_child, 0f);
            _child.transform.localScale = Vector3.one * 1.6f;
            Lift(_child.Renderer, OrderChildBehind);
            MakePool(Vector3.zero, 2.3f, Tinted(SpiritPool, _colA), _child.transform);
        }

        private void ReleaseParents()
        {
            if (_a != null) _a.ExitCeremony();
            if (_b != null) _b.ExitCeremony();
        }

        // ---- beat f: step out ---------------------------------------------------------------------------

        private IEnumerator StepOut()
        {
            if (_child == null) yield break;
            SpiritVoice.Play(_child.Species, VoiceIntent.Greet, 0.8f);

            float startY = Center.y + ChildStartOffset;
            float endY = Center.y + ChildEndOffset;
            float clothBottom = ClothTop.y - ClothUnitHeight * ClothScale;
            bool swapped = false;

            for (float t = 0f; t < StepOutSeconds && !_skip; t += Time.deltaTime)
            {
                if (_child == null) yield break;
                float k = Mathf.Clamp01(t / StepOutSeconds);
                float e = Mathf.SmoothStep(0f, 1f, k);

                float y = Mathf.Lerp(startY, endY, e);
                _child.transform.position = new Vector3(Center.x, y, 0f);
                _child.transform.localScale = Vector3.one * Mathf.Lerp(1.85f, 1.6f, e);
                SetAlpha(_child, Mathf.Clamp01(k / 0.4f));

                // Walks out through the cloth, in front of it once it clears the hem.
                if (!swapped && y < clothBottom - 0.2f && _child.Renderer != null)
                {
                    swapped = true;
                    _child.Renderer.sortingOrder = OrderActor;
                    Puffs.Burst(_child.transform.position + Vector3.up * 0.7f, _threadA, 5, 1.0f, 0.5f, 0.09f, OrderPuff);
                    Puffs.Burst(_child.transform.position + Vector3.up * 0.7f, _threadB, 5, 1.0f, 0.5f, 0.09f, OrderPuff);
                }
                yield return null;
            }

            if (_child != null)
            {
                _child.transform.position = ChildFinalPos;
                _child.transform.localScale = Vector3.one * 1.6f;
                _child.EnterCeremony(ChildFinalPos);
                if (_child.Renderer != null) _child.Renderer.sortingOrder = OrderActor;
                SpiritVoice.Play(_child.Species, VoiceIntent.Happy, 0.8f);
            }
        }

        // ---- beat g: the cloth folds into a banner -----------------------------------------------------

        private IEnumerator FoldCloth()
        {
            if (_cloth == null) yield break;

            var player = GameObject.FindWithTag("Player");
            Vector3 target = player != null
                ? player.transform.position + Vector3.up * 0.7f : Center + Vector3.down * 1.4f;
            Vector3 from = _cloth.position;

            for (float t = 0f; t < FoldSeconds && !_skip; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / FoldSeconds);
                float e = Mathf.SmoothStep(0f, 1f, k);
                if (_cloth == null) yield break;
                _cloth.position = Vector3.Lerp(from, target, e);
                float s = Mathf.Lerp(ClothScale, 0.35f, e);
                _cloth.localScale = new Vector3(s, s, 1f);
                if (_clothRenderer != null)
                    _clothRenderer.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
                yield return null;
            }
            if (_cloth != null) Puffs.Burst(target, Color.Lerp(_threadA, _threadB, 0.5f), 6, 1.0f, 0.5f, 0.09f, OrderPuff);
        }

        // ---- cleanup --------------------------------------------------------------------------------------------

        /// <summary>
        /// End state (normal or skipped): commit (if not yet), restore world +
        /// input, release the locks, hand the newcomer to the naming ceremony.
        /// Safe to reach from any beat.
        /// </summary>
        private void Finish()
        {
            if (_finished) return;
            _finished = true;

            if (_pauseHooked && GameInput.Instance != null)
                GameInput.Instance.PausePressed -= OnPausePressed;
            _pauseHooked = false;

            // A skip before the commit still weaves (skip = jump to the end state).
            if (_a != null && _b != null && !_committed) CommitWeave();

            // Restore sorting, then remove the visuals.
            foreach (var (sr, order) in _lifted)
                if (sr != null) sr.sortingOrder = order;
            _lifted.Clear();
            foreach (var pool in _pools)
                if (pool != null) Destroy(pool);
            _pools.Clear();

            // A parent still standing (rite aborted before the commit) is simply released.
            ReleaseParents();
            if (_a != null) SetAlpha(_a, 1f);
            if (_b != null) SetAlpha(_b, 1f);

            // A skipped step-out still puts the newcomer clear of the loom.
            if (_child != null)
            {
                _child.transform.position = ChildFinalPos;
                _child.transform.localScale = Vector3.one * 1.6f;
                _child.EnterCeremony(ChildFinalPos);
            }

            if (_loom != null) _loom.EndRite();

            // Modal restore, pause-respecting (HomePickerUI pattern). Running goes
            // false first so the owner check below does not count this ceremony.
            Running = false;
            UIInputLock.ModalOpen = false;
            if (GameInput.Instance != null)
            {
                bool paused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                if (!paused && !UIInputLock.NonModalOwnerHolds) GameInput.Instance.SetGameplayBlocked(false);
            }

            HandoffToNaming();
            NamingCeremony.TryStartPending(); // a naming queued behind the rite goes now

            Destroy(gameObject); // overlay, cloth, threads and motes all ride this root
        }

        /// <summary>
        /// Name echo: the naming ceremony opens on the newcomer with a blend of
        /// the parents' names pre-filled (fully editable). It runs the
        /// "light descends" variant with a weave-specific title.
        /// </summary>
        private void HandoffToNaming()
        {
            if (_child == null) return;

            string blend = WeaveNames.Blend(_nameA, _nameB);
            NamingCeremony.Begin(_child, blend, "A legend is woven!");

            if (_record != null && WeaveArchive.Instance != null)
                WeaveArchive.Instance.WatchNaming(_record, _child);

            if (_record != null)
                FloatingText.Show(_child.transform.position + Vector3.up * 1.9f,
                    "Banner woven. Hang it from the Build menu.", RiteText);
        }

        /// <summary>
        /// Esc = skip. GameManager's TogglePause subscribed first, so the game
        /// just paused - undo that and jump to the end state. Console typing
        /// never skips.
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
            // Hard teardown (scene unload mid-rite): never leave the world
            // locked. Nothing is committed here - see the save policy.
            if (_finished) return;
            _finished = true;

            if (_pauseHooked && GameInput.Instance != null)
                GameInput.Instance.PausePressed -= OnPausePressed;
            foreach (var (sr, order) in _lifted)
                if (sr != null) sr.sortingOrder = order;
            foreach (var pool in _pools)
                if (pool != null) Destroy(pool);
            ReleaseParents();
            if (_child != null) _child.ExitCeremony();
            if (_loom != null) _loom.EndRite();
            Running = false;
            UIInputLock.ModalOpen = false;
            if (GameInput.Instance != null
                && (GameManager.Instance == null || !GameManager.Instance.IsPaused)
                && !UIInputLock.NonModalOwnerHolds)
                GameInput.Instance.SetGameplayBlocked(false);
        }

        // ---- helpers ----------------------------------------------------------------------------------------------

        private static void SetAlpha(SpiritAgent agent, float alpha)
        {
            if (agent == null || agent.Renderer == null) return;
            var c = agent.Renderer.color;
            agent.Renderer.color = new Color(c.r, c.g, c.b, alpha);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        private SpriteRenderer MakeMote(Color color)
        {
            var go = new GameObject("Mote");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GlowSprite;
            sr.sortingOrder = OrderMote;
            sr.color = color;
            go.transform.localScale = Vector3.one * 0.3f;
            return sr;
        }

        /// <summary>
        /// A glowing thread drawn as a chain of thin rotated quads along a
        /// sagging, wobbling curve (no LineRenderer: no shader/material
        /// dependency).
        /// </summary>
        private sealed class Thread
        {
            private readonly SpriteRenderer[] _segs;

            public Thread(Transform parent, int segments, int order)
            {
                _segs = new SpriteRenderer[segments];
                for (int i = 0; i < segments; i++)
                {
                    var go = new GameObject("Thread");
                    go.transform.SetParent(parent, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = SquareSprite;
                    sr.sortingOrder = order;
                    _segs[i] = sr;
                }
            }

            /// <param name="sag">Droop (positive = hangs below the chord).</param>
            /// <param name="wobble">Sideways ripple amplitude.</param>
            /// <param name="grow">0..1 portion of the curve that exists.</param>
            public void Draw(Vector3 from, Vector3 to, float sag, float wobble, float phase,
                Color color, float width, float alpha, float grow)
            {
                int n = _segs.Length;
                Vector3 prev = Point(from, to, 0f, sag, wobble, phase);
                for (int i = 0; i < n; i++)
                {
                    float u1 = (i + 1) / (float)n * Mathf.Clamp01(grow);
                    Vector3 next = Point(from, to, u1, sag, wobble, phase);
                    Vector3 d = next - prev;
                    float len = d.magnitude;

                    var seg = _segs[i];
                    if (seg == null) { prev = next; continue; }
                    var tr = seg.transform;
                    tr.position = (prev + next) * 0.5f;
                    tr.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    tr.localScale = new Vector3(len + 0.03f, width, 1f);
                    seg.color = new Color(color.r, color.g, color.b, alpha);
                    prev = next;
                }
            }

            public void Hide()
            {
                for (int i = 0; i < _segs.Length; i++)
                    if (_segs[i] != null) _segs[i].color = new Color(1f, 1f, 1f, 0f);
            }

            private static Vector3 Point(Vector3 from, Vector3 to, float u, float sag, float wobble, float phase)
            {
                Vector3 p = Vector3.Lerp(from, to, u);
                float env = Mathf.Sin(Mathf.PI * u);
                p.y -= sag * env;
                Vector3 chord = to - from;
                Vector3 perp = new Vector3(-chord.y, chord.x, 0f).normalized;
                p += perp * (wobble * env * Mathf.Sin(u * 9f + phase));
                p.z = 0f;
                return p;
            }
        }

        // ---- generated sprites + audio -------------------------------------------------------------------------------

        private static Sprite _square, _glow;

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

        private static Sprite GlowSprite
        {
            get
            {
                if (_glow == null) _glow = AscensionPad.MakeRadialGlowSprite(64, 32f);
                return _glow;
            }
        }

        private static AudioClip _chord;

        /// <summary>
        /// One airy open chord (A3 / C#4 / E4 / B4, an add9), Bleeps house
        /// style: synthesized once, soft (sum peak ~0.26), slow swell and long
        /// tail. Routed through the AudioGuard SFX gate and master volume.
        /// </summary>
        private static void PlayWeaveChord()
        {
            if (Bleeps.Muted || !Application.isPlaying) return;
            float vol = Bleeps.SfxVolume;
            if (vol <= 0f) return;
            if (!AudioGuard.TryPlay(AudioBus.Sfx, "weave:chord", 0.8f, 1f)) return;

            if (_chord == null)
            {
                const int rate = 44100;
                const float seconds = 2.4f;
                float[] freqs = { 220f, 277.18f, 329.63f, 493.88f };
                var d = new float[Mathf.CeilToInt(seconds * rate)];
                for (int n = 0; n < freqs.Length; n++)
                {
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)rate;
                        float attack = Mathf.Clamp01(t / 0.25f);
                        float release = Mathf.Exp(-6f * t / 2.0f);
                        d[i] += 0.065f * Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * attack * release;
                    }
                }
                _chord = AudioClip.Create("WeaveChord", d.Length, 1, rate, false);
                _chord.SetData(d, 0);
            }

            var go = new GameObject("WeaveChord");
            go.hideFlags = HideFlags.HideInHierarchy;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.PlayOneShot(_chord, Mathf.Clamp01(0.8f * vol));
            Destroy(go, 3f);
        }
    }
}
