using System.Collections;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Repo-man in the field (slice 07): spawned at the town gate by
    /// <see cref="RepoManManager"/>, walks slowly and visibly toward the target
    /// runaway, and either lectures (first-ever warning visit) or takes it into
    /// holding. Soothing the target mid-walk turns him around; a bribe of 2x
    /// the target's favored food does too. No physics -- pure transform walk,
    /// with a small fast officious bob.
    /// </summary>
    public class RepoManAgent : MonoBehaviour, IInteractable
    {
        private const float WalkSpeed = 1.15f;         // u/s, deliberately slow
        private const float ArriveDistance = 0.8f;
        private const float ReturnArriveDistance = 0.1f;
        private const float BobAmplitude = 0.035f;
        private const float BobFrequency = 2.6f;       // Hz: brisk, bureaucratic
        private const float LectureLineGap = 1.4f;     // seconds between lines
        private const float FocusScale = 1.06f;

        private static readonly Color PaperGrey = new Color(0.75f, 0.75f, 0.78f);
        private static readonly Color OfficialInk = new Color(0.92f, 0.92f, 0.85f);
        private static readonly Color BribeGold = new Color(1f, 0.9f, 0.4f);

        private enum Phase { Approaching, Lecturing, Returning }

        private RepoManManager _mgr;
        private SpiritAgent _target;
        private bool _warningVisit;

        private Phase _phase = Phase.Approaching;
        private Vector3 _spawnPoint;
        private bool _tookSomeone;
        private bool _resolved;
        private SpriteRenderer _body;
        private Vector3 _baseScale = Vector3.one;
        private bool _focused;

        /// <summary>Configures a freshly built Repo-man (the manager builds the visuals).</summary>
        public void Init(RepoManManager mgr, SpiritAgent target, bool warningVisit)
        {
            _mgr = mgr;
            _target = target;
            _warningVisit = warningVisit;
            _spawnPoint = transform.position;
            _baseScale = transform.localScale;
            _body = GetComponentInChildren<SpriteRenderer>();
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

            switch (_phase)
            {
                case Phase.Approaching: TickApproach(); break;
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
            if (_target == null || _target.State != SpiritState.Runaway)
            {
                Say("Hmph. Paperwork wasted.", PaperGrey);
                TurnAround(false);
                return;
            }

            // Runaways sit at the border, but re-aim each frame anyway.
            transform.position = Vector3.MoveTowards(
                transform.position, _target.transform.position, WalkSpeed * Time.deltaTime);

            if ((transform.position - _target.transform.position).sqrMagnitude
                > ArriveDistance * ArriveDistance)
                return;

            // Arrived.
            if (_warningVisit)
            {
                _phase = Phase.Lecturing;
                StartCoroutine(LectureThenLeave());
            }
            else
            {
                Say("Per section 12: repossession.", OfficialInk);
                if (_mgr != null) _mgr.TakeIntoHolding(_target);
                _target = null;
                TurnAround(true);
            }
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
        }

        private void TickReturn()
        {
            transform.position = Vector3.MoveTowards(
                transform.position, _spawnPoint, WalkSpeed * Time.deltaTime);

            if ((transform.position - _spawnPoint).sqrMagnitude
                <= ReturnArriveDistance * ReturnArriveDistance)
            {
                Resolve();
                Destroy(gameObject);
            }
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (_mgr != null) _mgr.OnRepoResolved(_tookSomeone);
        }

        private void OnDestroy()
        {
            // Safety net: if something destroys us mid-errand, still release the
            // manager's dispatch lock (and the banner) exactly once.
            Resolve();
        }

        private void Say(string text, Color color)
        {
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 1.1f, text, color);
        }

        private string TargetFavoredFood =>
            _target != null && _target.Species != null ? _target.Species.favoredFoodId : "";

        // ---- IInteractable (the bribe) --------------------------------------------

        public string PromptText =>
            _target != null ? $"Bribe ({RepoManManager.BribeFoodCount}x {TargetFavoredFood})" : "";

        /// <summary>Bribable only while walking toward an actual take (warnings cost nothing).</summary>
        public bool CanInteract(GameObject actor) =>
            !_resolved && _phase == Phase.Approaching && !_warningVisit && _target != null;

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor)) return;

            string food = TargetFavoredFood;
            if (Inventory.Instance != null
                && Inventory.Instance.Consume(food, RepoManManager.BribeFoodCount))
            {
                Say("A modest consideration. Never happened.", BribeGold);
                Debug.Log("[Repo] The Repo-man was bribed and remembers nothing.");
                TurnAround(false);
            }
            else
            {
                Say($"(needs {RepoManManager.BribeFoodCount}x {food})", PaperGrey);
            }
        }

        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }
    }
}
