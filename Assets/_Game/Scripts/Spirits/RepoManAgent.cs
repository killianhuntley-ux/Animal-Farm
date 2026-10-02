using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Repo-man in the field (slice 07 + muscle 07): spawned at the far
    /// end of the town road by <see cref="RepoManManager"/>, he WALKS in -
    /// never teleports - along the road, through the gates, whistling off-key
    /// while spirits nearby glance at him nervously. You see trouble coming.
    /// At the runaway he either lectures (first-ever warning visit, nothing
    /// taken) or confronts: the shepherd gets a choice (RepoBribeUI) to slip
    /// him obols to look the other way once (escalating price, saved by the
    /// manager). No choice in time and he takes the spirit into holding.
    /// Soothing the target at any point turns him around too. No physics -
    /// pure transform walk, with a small fast officious bob.
    /// </summary>
    public class RepoManAgent : MonoBehaviour, IInteractable
    {
        private const float WalkSpeed = 1.6f;          // u/s: slow enough to see him coming
        private const float ArriveDistance = 0.8f;
        private const float WaypointReach = 0.2f;
        private const float ReturnArriveDistance = 0.1f;
        private const float BobAmplitude = 0.035f;
        private const float BobFrequency = 2.6f;       // Hz: brisk, bureaucratic
        private const float LectureLineGap = 1.4f;     // seconds between lines
        private const float FocusScale = 1.06f;

        private const float ConfrontRadius = 7f;       // player this close on arrival = auto-offer the bribe
        private const float ConfrontGraceSeconds = 25f; // he consults his clipboard this long, then takes

        private const float NervousRadius = 6f;        // spirits within this watch him nervously
        private const float NervousPulseSeconds = 1.1f;
        private const float NervousCooldownSeconds = 7f;

        private const float WhistleVolumeMax = 0.55f;
        private const float WhistleHearRadius = 22f;   // fades to silence with distance
        private const float WhistleGapSeconds = 2.2f;

        private static readonly Color PaperGrey = new Color(0.75f, 0.75f, 0.78f);
        private static readonly Color OfficialInk = new Color(0.92f, 0.92f, 0.85f);
        private static readonly Color BribeGold = new Color(1f, 0.9f, 0.4f);

        private static readonly string[] NervousLines = { "(gulp)", "...", "(eyes down)" };

        private enum Phase { Approaching, Confronting, Lecturing, Returning }

        private RepoManManager _mgr;
        private SpiritAgent _target;
        private bool _warningVisit;

        private Phase _phase = Phase.Approaching;
        private Vector3 _spawnPoint;
        private Vector3[] _route = new Vector3[0];     // road waypoints, spawn -> target area
        private int _routeIndex;                       // next waypoint on the way in
        private int _returnIndex = -1;                 // next waypoint on the way out (-1 = spawn)
        private bool _tookSomeone;
        private bool _resolved;
        private SpriteRenderer _body;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;

        // confrontation
        private bool _talking;                         // the bribe modal is open: he waits
        private bool _autoPrompted;
        private float _confrontTimer;
        private string _targetName = "this spirit";

        // whistle + nervous glances
        private AudioSource _whistle;
        private float _nextWhistleAt;
        private float _nextNervousAt;
        private Transform _playerTransform;
        private readonly Dictionary<SpiritAgent, float> _nervousReadyAt =
            new Dictionary<SpiritAgent, float>();

        /// <summary>Configures a freshly built Repo-man (the manager builds the visuals).</summary>
        public void Init(RepoManManager mgr, SpiritAgent target, bool warningVisit, Vector3[] route)
        {
            _mgr = mgr;
            _target = target;
            _warningVisit = warningVisit;
            _route = route ?? new Vector3[0];
            _routeIndex = 0;
            _spawnPoint = transform.position;
            _baseScale = transform.localScale;
            _body = GetComponentInChildren<SpriteRenderer>();

            if (target != null)
            {
                if (!string.IsNullOrEmpty(target.GivenName)) _targetName = target.GivenName;
                else if (target.Species != null && !string.IsNullOrEmpty(target.Species.displayName))
                    _targetName = target.Species.displayName;
            }

            _nextWhistleAt = Time.time + 0.5f;
        }

        /// <summary>Manager-side abort (e.g. a save was loaded): no resolution callback.</summary>
        public void CancelSilently()
        {
            _resolved = true;
            Destroy(gameObject);
        }

        // ---- per-frame -----------------------------------------------------------

        private void Update()
        {
            TickBob();
            TickWhistle();
            TickNervousSpirits();

            if (_talking) return; // the bribe modal is open: he waits, clipboard in hand

            switch (_phase)
            {
                case Phase.Approaching: TickApproach(); break;
                case Phase.Confronting: TickConfront(); break;
                case Phase.Lecturing: break; // coroutine owns this stretch
                case Phase.Returning: TickReturn(); break;
            }
        }

        private void TickBob()
        {
            if (_body == null) return;
            float y = Mathf.Sin(Time.time * BobFrequency * 2f * Mathf.PI) * BobAmplitude;
            _body.transform.localPosition = new Vector3(0f, y, 0f);
        }

        private void TickApproach()
        {
            // Someone soothed the runaway (or it vanished): errand cancelled.
            if (TargetGone())
            {
                Say("Hmph. Paperwork wasted.", PaperGrey);
                TurnAround(false);
                return;
            }

            // Along the road first: gate by gate, then straight at the runaway.
            if (_routeIndex < _route.Length)
            {
                Vector3 wp = _route[_routeIndex];
                transform.position = Vector3.MoveTowards(
                    transform.position, wp, WalkSpeed * Time.deltaTime);
                if ((transform.position - wp).sqrMagnitude <= WaypointReach * WaypointReach)
                    _routeIndex++;
                return;
            }

            // Runaways sit at the border, but re-aim each frame anyway.
            Vector3 aim = _target.transform.position;
            aim.z = transform.position.z;
            transform.position = Vector3.MoveTowards(
                transform.position, aim, WalkSpeed * Time.deltaTime);

            if ((transform.position - aim).sqrMagnitude > ArriveDistance * ArriveDistance)
                return;

            // Arrived.
            if (_warningVisit)
            {
                _phase = Phase.Lecturing;
                StartCoroutine(LectureThenLeave());
            }
            else
            {
                _phase = Phase.Confronting;
                _confrontTimer = 0f;
                _autoPrompted = false;
                Say("Ahem. Per section 12...", OfficialInk);
            }
        }

        /// <summary>He is standing over the runaway. The shepherd may step in and
        /// bribe him; otherwise, when the clipboard consultation runs out, he takes.</summary>
        private void TickConfront()
        {
            if (TargetGone())
            {
                Say("Hmph. Paperwork wasted.", PaperGrey);
                TurnAround(false);
                return;
            }

            // The shepherd is close enough to hear him: put the choice on screen.
            // (Wait out any other modal the shepherd has open - one at a time.)
            if (!_autoPrompted && PlayerDistance() <= ConfrontRadius && !UIInputLock.ModalOpen)
            {
                _autoPrompted = true;
                OpenBribe();
                return;
            }

            _confrontTimer += Time.deltaTime;
            if (_confrontTimer >= ConfrontGraceSeconds) TakeNow();
        }

        private bool TargetGone() =>
            _target == null || _target.State != SpiritState.Runaway;

        private void TakeNow()
        {
            if (_target == null) { TurnAround(false); return; }
            Say("Per section 12: repossession.", OfficialInk);
            if (_mgr != null) _mgr.TakeIntoHolding(_target);
            _target = null;
            TurnAround(true);
        }

        private IEnumerator LectureThenLeave()
        {
            Say("Notice of neglect: first and final warning.", OfficialInk);
            yield return new WaitForSeconds(LectureLineGap);
            Say("Next time, the spirit comes with me.", OfficialInk);
            yield return new WaitForSeconds(LectureLineGap);
            Say("I don't make the rules. I laminate them.", OfficialInk);
            yield return new WaitForSeconds(LectureLineGap);
            TurnAround(false);
        }

        private void TurnAround(bool tookSomeone)
        {
            _tookSomeone = tookSomeone;
            _phase = Phase.Returning;
            // Walk back the way he came: through every gate he already passed.
            _returnIndex = Mathf.Min(_routeIndex, _route.Length) - 1;
            // The errand is settled either way: the alarm is over.
            AnimalFarm.UI.AlarmBannerUI.Hide(RepoManManager.Instance); // the manager owns the repo alarm
        }

        private void TickReturn()
        {
            Vector3 dest = _returnIndex >= 0 ? _route[_returnIndex] : _spawnPoint;
            transform.position = Vector3.MoveTowards(
                transform.position, dest, WalkSpeed * Time.deltaTime);

            float reach = _returnIndex >= 0 ? WaypointReach : ReturnArriveDistance;
            if ((transform.position - dest).sqrMagnitude > reach * reach) return;

            if (_returnIndex >= 0)
            {
                _returnIndex--;
                return;
            }

            Resolve();
            Destroy(gameObject);
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (_mgr != null) _mgr.OnRepoResolved(_tookSomeone);
        }

        private void OnDestroy()
        {
            // Never leave the modal (and its input lock) open under a dead Repo-man.
            if (_talking && AnimalFarm.UI.RepoBribeUI.Instance != null)
                AnimalFarm.UI.RepoBribeUI.Instance.CloseIfOpen();

            // Safety net: if something destroys us mid-errand, still release the
            // manager's dispatch lock (and the banner) exactly once.
            Resolve();
        }

        private void Say(string text, Color color)
        {
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 1.1f, text, color);
        }

        // ---- the bribe ---------------------------------------------------------------

        private void OpenBribe()
        {
            if (_mgr == null || _target == null) return;

            var ui = AnimalFarm.UI.RepoBribeUI.GetOrCreate();
            if (ui == null) return;

            if (ui.Open(_targetName, _mgr.NextBribePrice, OnBribeChosen, OnBribeRefused))
                _talking = true;
        }

        /// <summary>Called by the modal; false keeps it open (couldn't pay).</summary>
        private bool OnBribeChosen()
        {
            if (_mgr == null || !_mgr.TryPayBribe()) return false;

            _talking = false;
            Say("A modest consideration. Never happened.", BribeGold);
            Debug.Log("[Repo] The Repo-man was bribed and remembers nothing.");
            TurnAround(false); // the spirit stays; the paperwork does not
            return true;
        }

        /// <summary>The shepherd let him proceed. At the runaway that means a take.</summary>
        private void OnBribeRefused()
        {
            _talking = false;
            if (_phase == Phase.Confronting) TakeNow();
        }

        // ---- whistle -----------------------------------------------------------------

        /// <summary>Off-key whistling while he walks (in and out); silent while he
        /// talks, lectures, or stands over his prey. Goes through AudioGuard so the
        /// mute/solo/panic switches and flood limits apply like every other sound.</summary>
        private void TickWhistle()
        {
            bool walking = _phase == Phase.Approaching || _phase == Phase.Returning;
            bool silenced = Bleeps.Muted || !AudioGuard.IsAudible(AudioBus.Sfx);
            if (!walking || _talking || silenced)
            {
                if (_whistle != null && _whistle.isPlaying) _whistle.Stop();
                return;
            }

            if (_whistle != null && _whistle.isPlaying)
            {
                _whistle.volume = WhistleVolume();
                return;
            }

            if (Time.time < _nextWhistleAt) return;
            if (Bleeps.Muted || Bleeps.SfxVolume <= 0f) return;

            float vol = WhistleVolume();
            if (vol <= 0.01f) return;

            var clip = RepoManWhistle.Clip;
            if (clip == null) return;

            if (!AudioGuard.TryPlay(AudioBus.Sfx, "repo_whistle", vol, 1f))
            {
                _nextWhistleAt = Time.time + 1f;
                return;
            }

            if (_whistle == null)
            {
                _whistle = gameObject.AddComponent<AudioSource>();
                _whistle.playOnAwake = false;
                _whistle.loop = false;
                _whistle.spatialBlend = 0f; // 2D: volume is faded by distance below
            }
            _whistle.clip = clip;
            _whistle.volume = vol;
            _whistle.Play();
            _nextWhistleAt = Time.time + clip.length + WhistleGapSeconds;
        }

        private float WhistleVolume()
        {
            float falloff = Mathf.Clamp01(1f - PlayerDistance() / WhistleHearRadius);
            return WhistleVolumeMax * Bleeps.SfxVolume * falloff;
        }

        // ---- nervous glances ---------------------------------------------------------------

        /// <summary>Spirits near him watch him nervously: one flinch + a small
        /// whisper per pulse, so the unease ripples through the group.</summary>
        private void TickNervousSpirits()
        {
            if (Time.time < _nextNervousAt) return;
            _nextNervousAt = Time.time + NervousPulseSeconds;

            var spirits = SpiritManager.Instance;
            if (spirits == null) return;

            var all = spirits.AllSpirits;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a == _target) continue;
                if (a.State != SpiritState.Resident && a.State != SpiritState.Visitor) continue;
                if (Vector2.Distance(a.transform.position, transform.position) > NervousRadius)
                    continue;
                if (_nervousReadyAt.TryGetValue(a, out float ready) && Time.time < ready) continue;

                _nervousReadyAt[a] = Time.time + NervousCooldownSeconds;
                a.Nervous();
                AnimalFarm.UI.FloatingText.Show(a.transform.position + Vector3.up * 0.8f,
                    NervousLines[Random.Range(0, NervousLines.Length)], PaperGrey);
                break; // one per pulse
            }
        }

        private float PlayerDistance()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player == null) return float.PositiveInfinity;
                _playerTransform = player.transform;
            }

            Vector3 to = _playerTransform.position - transform.position;
            to.z = 0f;
            return to.magnitude;
        }

        // ---- IInteractable (talk / bribe) --------------------------------------------------

        private bool CanBribe =>
            !_resolved && !_talking && !_warningVisit && _target != null
            && (_phase == Phase.Approaching || _phase == Phase.Confronting);

        public string PromptText =>
            CanBribe && _mgr != null ? $"Bribe the Repo-man ({_mgr.NextBribePrice} obols)" : "";

        /// <summary>Bribable while walking toward / standing over an actual take
        /// (warnings cost nothing, so there is nothing to buy off).</summary>
        public bool CanInteract(GameObject actor) => CanBribe;

        public void Interact(GameObject actor)
        {
            if (!CanBribe) return;
            _autoPrompted = true; // don't re-offer on top of a deliberate talk
            OpenBribe();
        }

        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }
    }
}
