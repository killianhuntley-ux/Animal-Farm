using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Onboarding;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// THE NAMING CEREMONY "Light descends" (muscle 03, verdict 5).
    /// When a visitor becomes a resident (SpiritManager.NamingRequested,
    /// bridged by NamePromptUI) or a weave produces a new spirit:
    ///   a. time softens (Time.timeScale eases to ~0.3), gameplay blocks
    ///      (modal law) and the spirit holds still;
    ///   b. the guide-light drifts down from above to hover over the spirit,
    ///      a warm glow blooms beneath it, and one gentle chord rings;
    ///   c. the spirit bows as the name field appears (NamePromptUI.Prompt,
    ///      pre-filled with the suggestion - still editable);
    ///   d. confirming echoes the name as three floating texts, the world
    ///      eases back to full speed, the light drifts off, and the journal
    ///      page flips open on the new resident.
    /// ~6 s of choreography around however long the player types. Esc skips
    /// the flourish: the name field appears immediately, and after naming the
    /// echo + journal flip are dropped (the name always lands). The Pause
    /// press is intercepted exactly like the Styx crossing (GameManager's
    /// toggle is undone here); typing in the field never skips.
    ///
    /// Requests that arrive mid-ceremony queue and play one after another.
    /// No state is saved by the ceremony itself: the name is committed by
    /// SpiritAgent.SetGivenName at confirm (an unnamed spirit saved before
    /// that simply loads unnamed, as before).
    /// </summary>
    public class NamingCeremony : MonoBehaviour
    {
        public static bool Running { get; private set; }

        // ---- timings (REAL seconds; the ceremony runs on unscaled time) ----------
        private const float SoftScale = 0.3f;        // world speed while softened
        private const float SoftRampSeconds = 0.7f;  // full 0..1 change of speed
        private const float DescendSeconds = 2.0f;
        private const float ArrivalPulseSeconds = 0.35f;
        private const float BowSeconds = 1.2f;
        private const float FieldDelay = 0.45f;      // into the bow, then the field appears
        private const float EchoGap = 0.42f;
        private const float AfterEchoHold = 0.5f;
        private const float HoverHeight = 1.25f;     // light above the spirit's root
        private const float StartHeight = 4.6f;      // where the light begins its descent
        private const float RestLightScale = 1.25f;
        private const float GlowMaxAlpha = 0.32f;

        private static readonly Color GlowWarm = new Color(1f, 0.92f, 0.65f, 1f);

        // ---- request queue ----------------------------------------------------------

        private struct Request
        {
            public SpiritAgent agent;
            public string suggested;
            public string title;
        }

        private static readonly Queue<Request> Pending = new Queue<Request>();

        /// <summary>
        /// Entry point. Runs the ceremony for <paramref name="agent"/> (queued
        /// if one is already playing). <paramref name="suggestedName"/> pre-fills
        /// the name field - null/blank picks a random pool name - and the player
        /// can always edit it (weaving passes a blend of the parents' names).
        /// <paramref name="title"/> overrides the field's heading (null = the
        /// default "A spirit wishes to stay!"). The name is applied with
        /// SpiritAgent.SetGivenName when the player confirms.
        /// </summary>
        public static void Begin(SpiritAgent agent, string suggestedName = null, string title = null)
        {
            if (agent == null) return;

            // Never two ceremonies at once: wait behind a running naming, Styx
            // crossing or weave rite (those call TryStartPending when they end),
            // and behind any open modal / text field or a competition event (the
            // queue poller starts it once the way is clear).
            if (MustDefer())
            {
                Pending.Enqueue(new Request { agent = agent, suggested = suggestedName, title = title });
                EnsurePoller();
                return;
            }
            Launch(agent, suggestedName, title);
        }

        /// <summary>
        /// Starts the next queued naming if nothing is playing. Called by the
        /// Styx and weave ceremonies when they finish.
        /// </summary>
        public static void TryStartPending()
        {
            if (MustDefer())
            {
                if (HasPending()) EnsurePoller();
                return;
            }
            StartNext();
        }

        /// <summary>Something else owns the screen: another ceremony, a modal / text field, or a competition event.</summary>
        private static bool MustDefer() =>
            Running || StyxCrossingCeremony.Running || WeaveRiteCeremony.Running
            || UIInputLock.BlockDirectKeys || UIInputLock.EventActive;

        /// <summary>Polls the queue (static state has no Update of its own) until it drains.</summary>
        private sealed class QueuePoller : MonoBehaviour
        {
            private void Update()
            {
                if (!HasPending())
                {
                    _poller = null;
                    Destroy(gameObject);
                    return;
                }
                TryStartPending();
            }
        }

        private static QueuePoller _poller;

        private static void EnsurePoller()
        {
            if (_poller != null) return;
            var go = new GameObject("NamingCeremonyQueue");
            go.hideFlags = HideFlags.HideInHierarchy;
            _poller = go.AddComponent<QueuePoller>();
        }

        private static void Launch(SpiritAgent agent, string suggested, string title)
        {
            var go = new GameObject("NamingCeremony");
            var c = go.AddComponent<NamingCeremony>();
            c._agent = agent;
            c._suggested = suggested;
            c._title = title;
            Running = true; // claim now, before the coroutine's first frame
            c.StartCoroutine(c.Run());
        }

        private static bool HasPending()
        {
            while (Pending.Count > 0 && Pending.Peek().agent == null) Pending.Dequeue();
            return Pending.Count > 0;
        }

        private static void StartNext()
        {
            if (!HasPending()) return;
            var r = Pending.Dequeue();
            Launch(r.agent, r.suggested, r.title);
        }

        // ---- instance state -----------------------------------------------------------

        private SpiritAgent _agent;
        private string _suggested;
        private string _title;

        private bool _skip;
        private bool _finished;
        private bool _pauseHooked;

        private float _softScale = 1f;
        private float _softTarget = 1f;

        private GuideLight _guide;
        private bool _guideWasVisible;
        private GameObject _ownLight;      // fallback when no GuideLight exists
        private Vector3 _lightPos;
        private float _lightScale = 1f;

        private GameObject _glow;
        private SpriteRenderer _glowRenderer;
        private float _glowAlpha;

        private static bool IsPaused => GameManager.Instance != null && GameManager.Instance.IsPaused;

        /// <summary>Real-time step that stalls while the game is paused.</summary>
        private static float StepDt() => IsPaused ? 0f : Time.unscaledDeltaTime;

        private Vector3 HoverSpot() =>
            (_agent != null ? _agent.transform.position : transform.position) + Vector3.up * HoverHeight;

        // ---- the ceremony ----------------------------------------------------------------

        private IEnumerator Run()
        {
            try
            {
                var agent = _agent;

                // A quiet bed under the chord.
                if (AmbientMusic.Instance != null) AmbientMusic.Instance.DuckFor(10f);

                // Modal law: block gameplay for the whole ceremony.
                UIInputLock.ModalOpen = true;
                if (GameInput.Instance != null)
                {
                    GameInput.Instance.SetGameplayBlocked(true);
                    GameInput.Instance.PausePressed += OnPausePressed;
                    _pauseHooked = true;
                }

                // a. Time softens; the spirit holds still where it stands.
                agent.SetFollowing(false);
                agent.EnterCeremony(agent.transform.position);
                _softTarget = SoftScale;

                Vector3 hover = HoverSpot();
                Vector3 start = hover + new Vector3(0.9f, StartHeight, 0f);
                BuildLight(start);
                BuildGlow();

                // b. The guide-light drifts down over the spirit; a chord rings on arrival.
                bool chordPlayed = false;
                for (float t = 0f; t < DescendSeconds && !_skip && agent != null; t += StepDt())
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / DescendSeconds);
                    hover = HoverSpot();
                    Vector3 p = Vector3.Lerp(start, hover, k);
                    p.x += Mathf.Sin(k * Mathf.PI * 2f) * 0.35f * (1f - k); // a lazy sideways drift
                    _lightPos = p;
                    _glowAlpha = k;

                    if (!chordPlayed && k > 0.8f)
                    {
                        chordPlayed = true;
                        PlayChord();
                    }
                    yield return null;
                }

                if (agent == null) { Finish(false); yield break; }

                hover = HoverSpot();
                _lightPos = hover;
                _glowAlpha = 1f;

                if (!_skip)
                {
                    if (!chordPlayed) PlayChord();
                    Puffs.Burst(agent.transform.position + Vector3.up * 0.4f, GlowWarm, 8, 1.2f, 0.6f, 0.1f);

                    // The light swells as it settles, then rests a little larger.
                    for (float t = 0f; t < ArrivalPulseSeconds && !_skip; t += StepDt())
                    {
                        float k = t / ArrivalPulseSeconds;
                        _lightScale = Mathf.Lerp(1f, RestLightScale, k) + 0.4f * Mathf.Sin(k * Mathf.PI);
                        yield return null;
                    }
                }
                _lightScale = RestLightScale;

                // c. The spirit bows as the name field appears.
                if (!_skip)
                {
                    agent.PlayBow(BowSeconds);
                    yield return Beat(FieldDelay);
                }

                string chosen = null;
                bool done = false;
                var ui = NamePromptUI.Instance;
                if (ui != null)
                {
                    ui.Prompt(agent, _suggested, n => { chosen = n; done = true; }, _title, true);
                }
                else
                {
                    // No name UI in this scene: take the suggestion so the spirit is never nameless.
                    chosen = string.IsNullOrWhiteSpace(_suggested)
                        ? NamePromptUI.RandomSuggestion() : _suggested.Trim();
                    done = true;
                }

                while (!done && agent != null) yield return null;

                if (agent == null)
                {
                    if (ui != null && ui.IsOpen) ui.Cancel();
                    Finish(false);
                    yield break;
                }

                // d. The name lands: it echoes, the world eases back, the journal flips open.
                agent.SetGivenName(chosen);
                _softTarget = 1f;

                Vector3 head = agent.transform.position + Vector3.up * 0.9f;
                FloatingText.Show(head, chosen + "!", UIStyle.Gold);
                if (_skip) { Finish(false); yield break; }

                if (agent.Species != null) SpiritVoice.Play(agent.Species, VoiceIntent.Happy, 0.9f);
                Puffs.Burst(agent.transform.position + Vector3.up * 0.3f, UIStyle.Gold, 8, 1.4f, 0.5f, 0.1f);
                yield return Beat(EchoGap);

                if (!_skip && agent != null)
                    FloatingText.Show(head + new Vector3(0.28f, 0.42f, 0f), chosen + "...",
                        new Color(UIStyle.Cream.r, UIStyle.Cream.g, UIStyle.Cream.b, 0.7f));
                yield return Beat(EchoGap);

                if (!_skip && agent != null)
                    FloatingText.Show(head + new Vector3(-0.3f, 0.84f, 0f), chosen + "...",
                        new Color(UIStyle.Cream.r, UIStyle.Cream.g, UIStyle.Cream.b, 0.45f));
                yield return Beat(AfterEchoHold);

                Finish(!_skip);
            }
            finally
            {
                // Coroutine stopped or faulted mid-ceremony: never leave the world locked.
                if (!_finished) Finish(false);
            }
        }

        /// <summary>Skip-aware real-time wait (stalls while paused).</summary>
        private IEnumerator Beat(float seconds)
        {
            for (float t = 0f; t < seconds && !_skip; t += StepDt())
                yield return null;
        }

        // ---- per-frame upkeep ---------------------------------------------------------------

        private void Update()
        {
            if (_finished) return;

            // Re-assert the modal flag: another UI closing mid-ceremony must not clear it.
            UIInputLock.ModalOpen = true;

            // Time softening rides real time; unpausing resets the scale, so re-apply it.
            _softScale = Mathf.MoveTowards(_softScale, _softTarget, Time.unscaledDeltaTime / SoftRampSeconds);
            if (!IsPaused) Time.timeScale = _softScale;

            // Keep the world locked even if the console (or a menu) closed mid-ceremony.
            if (GameInput.Instance != null) GameInput.Instance.SetGameplayBlocked(true);

            // The light: exact position (bobbing gently), scaled, kept visible.
            Vector3 lp = _lightPos + Vector3.up * (Mathf.Sin(Time.unscaledTime * 2.4f) * 0.06f);
            if (_guide != null)
            {
                _guide.SetVisible(true);
                _guide.SetCeremonyPosition(lp);
                _guide.transform.localScale = Vector3.one * _lightScale;
            }
            else if (_ownLight != null)
            {
                _ownLight.transform.position = lp;
                _ownLight.transform.localScale = Vector3.one * _lightScale;
            }

            // The warm glow under the spirit follows it.
            if (_glow != null)
            {
                if (_agent != null)
                    _glow.transform.position = _agent.transform.position + new Vector3(0f, -0.1f, 0f);
                if (_glowRenderer != null)
                {
                    var c = GlowWarm;
                    c.a = GlowMaxAlpha * Mathf.Clamp01(_glowAlpha);
                    _glowRenderer.color = c;
                }
                float s = 0.5f + 1.1f * Mathf.Clamp01(_glowAlpha);
                _glow.transform.localScale = new Vector3(s, s * 0.8f, 1f);
            }
        }

        // ---- scene pieces -----------------------------------------------------------------------

        private void BuildLight(Vector3 startPos)
        {
            var onboarding = OnboardingManager.Instance;
            _guide = onboarding != null ? onboarding.Guide : null;
            _lightPos = startPos;

            if (_guide != null)
            {
                _guideWasVisible = _guide.WantsVisible;
                _guide.BeginCeremony(startPos);
                _guide.SetVisible(true);
                return;
            }

            // No guide in this scene: a stand-in orb of the same look.
            _ownLight = new GameObject("NamingLight");
            _ownLight.transform.position = startPos;
            MakeOrb(_ownLight.transform, "Core", 0.45f, 0.9f, 150);
            MakeOrb(_ownLight.transform, "Halo", 0.9f, 0.25f, 149);
        }

        private static void MakeOrb(Transform parent, string name, float scale, float alpha, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GlowSprite;
            sr.sortingOrder = order;
            sr.color = new Color(1f, 0.95f, 0.8f, alpha);
        }

        private void BuildGlow()
        {
            _glow = new GameObject("NamingGlow");
            _glowRenderer = _glow.AddComponent<SpriteRenderer>();
            _glowRenderer.sprite = GlowSprite;
            _glowRenderer.sortingOrder = 1; // a soft pool of light over the spirit's feet
            _glowRenderer.color = new Color(GlowWarm.r, GlowWarm.g, GlowWarm.b, 0f);
            _glowAlpha = 0f;
            if (_agent != null) _glow.transform.position = _agent.transform.position;
        }

        // ---- teardown ---------------------------------------------------------------------------

        /// <summary>
        /// End state (normal or skipped): restore world speed, input and the
        /// light, release the spirit, then flip the journal open on it.
        /// </summary>
        private void Finish(bool openJournal)
        {
            if (_finished) return;
            _finished = true;

            var agent = _agent;
            Running = false; // before Teardown: it must not count this ceremony as an owner
            Teardown();

            // Only open the journal when nothing else is waiting for its own ceremony.
            if (openJournal && agent != null && !HasPending() && JournalUI.Instance != null)
                JournalUI.Instance.OpenToResident(agent, true);

            TryStartPending();
            Destroy(gameObject);
        }

        private void Teardown()
        {
            if (_pauseHooked && GameInput.Instance != null)
                GameInput.Instance.PausePressed -= OnPausePressed;
            _pauseHooked = false;

            var ui = NamePromptUI.Instance;
            if (ui != null && ui.IsOpen) ui.Cancel();

            if (_agent != null) _agent.ExitCeremony();

            if (_guide != null)
            {
                _guide.EndCeremony();                 // drifts back to the shepherd
                // and fades away again if it was hidden (or the tutorial finished meanwhile)
                var onboarding = OnboardingManager.Instance;
                bool onboardingDone = onboarding != null && onboarding.IsComplete;
                _guide.SetVisible(_guideWasVisible && !onboardingDone);
            }
            if (_ownLight != null) Destroy(_ownLight);
            if (_glow != null) Destroy(_glow);

            // World speed + input back, pause-respecting.
            bool paused = IsPaused;
            Time.timeScale = paused ? 0f : 1f;
            // Leave input alone if another owner still holds it (console, journal,
            // another ceremony). Running is already false here, so this ceremony
            // does not count itself; the name prompt's own Hide may have released
            // the block a moment ago, so re-assert it when someone still owns it.
            bool journalOpen = JournalUI.Instance != null && JournalUI.Instance.IsOpen;
            if (!journalOpen) UIInputLock.ModalOpen = false;
            if (GameInput.Instance != null && !paused)
                GameInput.Instance.SetGameplayBlocked(UIInputLock.AnyOwnerHolds);
        }

        private void OnDestroy()
        {
            // Hard teardown (scene unload mid-ceremony): never leave time slowed
            // or the world locked. The queue is dropped with the scene.
            if (_finished) return;
            _finished = true;
            Running = false;
            Teardown();
            Pending.Clear();
        }

        /// <summary>
        /// Esc = skip the flourish. GameManager's TogglePause subscribed first,
        /// so the game just paused - undo that and skip instead. Typing in the
        /// name field never skips (and Esc there keeps its old pause meaning).
        /// </summary>
        private void OnPausePressed()
        {
            if (UIInputLock.TextInputActive) return;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused)
                GameManager.Instance.SetPaused(false);
            _skip = true;
        }

        // ---- generated bits ------------------------------------------------------------------------

        private static Sprite _glowSprite;

        private static Sprite GlowSprite
        {
            get
            {
                if (_glowSprite == null)
                {
                    _glowSprite = AscensionPad.MakeRadialGlowSprite(64, 32f);
                    _glowSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                return _glowSprite;
            }
        }

        private static AudioClip _chord;

        /// <summary>
        /// One gentle major chord (C4 / E4 / G4 / D5 - a bright add9), Bleeps
        /// house style: synthesized once, soft (sum peak ~0.28), slow swell and
        /// a long tail. Routed through the AudioGuard SFX gate and master volume.
        /// </summary>
        private static void PlayChord()
        {
            if (Bleeps.Muted || !Application.isPlaying) return;
            float vol = Bleeps.SfxVolume;
            if (vol <= 0f) return;
            if (!AudioGuard.TryPlay(AudioBus.Sfx, "naming:chord", 0.8f, 1f)) return;

            if (_chord == null)
            {
                const int rate = 44100;
                const float seconds = 2.6f;
                float[] freqs = { 261.63f, 329.63f, 392f, 587.33f };
                var d = new float[Mathf.CeilToInt(seconds * rate)];
                for (int n = 0; n < freqs.Length; n++)
                {
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)rate;
                        float attack = Mathf.Clamp01(t / 0.3f);
                        float release = Mathf.Exp(-6f * t / 2.2f);
                        d[i] += 0.07f * Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * attack * release;
                    }
                }
                _chord = AudioClip.Create("NamingChord", d.Length, 1, rate, false);
                _chord.SetData(d, 0);
            }

            var go = new GameObject("NamingChord");
            go.hideFlags = HideFlags.HideInHierarchy;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.PlayOneShot(_chord, Mathf.Clamp01(0.8f * vol));
            Destroy(go, 3.2f);
        }
    }
}
