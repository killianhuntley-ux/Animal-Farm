using AnimalFarm.Core;
using AnimalFarm.Interaction;
using UnityEngine;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Lives on the shepherd. Scans a radius every frame for the nearest usable
    /// IInteractable, drives focus highlights, and forwards Interact presses.
    /// FocusChanged fires with null when focus is lost entirely.
    /// </summary>
    public class InteractionSensor : MonoBehaviour
    {
        [SerializeField] private float radius = 1.4f;

        public static event System.Action<IInteractable> FocusChanged;

        public IInteractable Current { get; private set; }

        private void Start()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.InteractPressed += OnInteractPressed;
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
                GameInput.Instance.InteractPressed -= OnInteractPressed;
        }

        private void OnInteractPressed()
        {
            Current?.Interact(gameObject);
        }

        private void Update()
        {
            IInteractable nearest = null;
            float nearestSqr = float.MaxValue;

            Vector2 origin = transform.position;
            var hits = Physics2D.OverlapCircleAll(origin, radius);
            foreach (var hit in hits)
            {
                foreach (var interactable in hit.GetComponents<IInteractable>())
                {
                    if (!interactable.CanInteract(gameObject)) continue;

                    float sqr = ((Vector2)hit.transform.position - origin).sqrMagnitude;
                    if (sqr < nearestSqr)
                    {
                        nearestSqr = sqr;
                        nearest = interactable;
                    }
                }
            }

            if (ReferenceEquals(nearest, Current)) return;

            // The old focus may have been destroyed this frame; guard the callback.
            if (Current != null && (!(Current is Object oldObj) || oldObj != null))
                Current.SetFocused(false);

            Current = nearest;
            Current?.SetFocused(true);
            FocusChanged?.Invoke(Current);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
