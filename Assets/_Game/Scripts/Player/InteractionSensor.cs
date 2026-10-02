using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Lives on the shepherd. Scans a radius every frame for the nearest usable
    /// IInteractable (weighted toward where the shepherd is facing), drives
    /// focus highlights (scale bump via SetFocused + the shared FocusOutline),
    /// and forwards Interact presses: an ISelectable target opens its context
    /// menu (InteractMenu / SelectionMenuUI), anything else interacts directly.
    /// FocusChanged fires with null when focus is lost entirely.
    /// </summary>
    public class InteractionSensor : MonoBehaviour
    {
        [SerializeField] private float radius = 1.4f;

        public static event System.Action<IInteractable> FocusChanged;

        public IInteractable Current { get; private set; }

        private ShepherdController _ctrl;

        private void Awake()
        {
            _ctrl = GetComponent<ShepherdController>();
        }

        private void Start()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.InteractPressed += OnInteractPressed;
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.InteractPressed -= OnInteractPressed;
            FocusOutline.Set(Current, false);
        }

        private void OnInteractPressed()
        {
            if (PoutyMount.IsRiding)
            {
                // dismount to interact (muscle 08); other Interact subscribers must ignore this press
                PoutyMount.DismountFrame = Time.frameCount;
                PoutyMount.Instance?.Dismount();
                return;
            }

            var target = Current;
            if (target == null || (target is Object targetObj && targetObj == null)) return;

            // The press that just confirmed a menu row is spent (closing the menu
            // re-enables Interact; the Interact event can land a frame later).
            int sinceClose = Time.frameCount - SelectionMenuUI.ClosedFrame; // negative = stale from an old session
            if (sinceClose >= 0 && sinceClose <= 1) return;

            // E always opens the menu on selectable targets (one real action runs
            // directly); plain interactables keep the direct Interact.
            if (InteractMenu.TryHandlePress(target)) return;
            target.Interact(gameObject);
        }

        private void Update()
        {
            IInteractable nearest = null;
            float nearestSqr = float.MaxValue;

            Vector2 origin = transform.position;
            var hits = Physics2D.OverlapCircleAll(origin, radius);
            foreach (var hit in hits)
            {
                // Selectable-only objects (Home, TrainingBuilding, Watchlight) are
                // focusable too, through a SelectableFocus stand-in, so E opens
                // their menu without a mouse.
                IInteractable[] candidates = hit.GetComponents<IInteractable>();
                if (candidates.Length == 0)
                {
                    var stand = SelectableFocus.For(hit.gameObject);
                    if (stand != null) candidates = new IInteractable[] { stand };
                }

                foreach (var interactable in candidates)
                {
                    if (!interactable.CanInteract(gameObject)) continue;

                    Vector2 to = (Vector2)hit.transform.position - origin;
                    float score = to.sqrMagnitude;

                    // Facing-weighted focus (muscle 01): what you are looking at
                    // beats what merely stands closest -- targets ahead read as
                    // nearer, targets behind as farther. The current focus gets
                    // a little stickiness so it does not flicker at the seam.
                    if (_ctrl != null && score > 0.0001f)
                    {
                        float dot = Vector2.Dot(_ctrl.FacingDir, to.normalized);
                        score *= Mathf.Lerp(1.8f, 0.5f, (dot + 1f) * 0.5f);
                        if (ReferenceEquals(interactable, Current)) score *= 0.8f;
                    }

                    if (score < nearestSqr)
                    {
                        nearestSqr = score;
                        nearest = interactable;
                    }
                }
            }

            if (ReferenceEquals(nearest, Current)) return;

            // The old focus may have been destroyed this frame; guard the callback.
            if (Current != null && (!(Current is Object oldObj) || oldObj != null))
            {
                Current.SetFocused(false);
                FocusOutline.Set(Current, false);
            }

            Current = nearest;
            Current?.SetFocused(true);
            FocusOutline.Set(Current, true);
            FocusChanged?.Invoke(Current);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
