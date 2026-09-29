using UnityEngine;

namespace AnimalFarm.Interaction
{
    /// <summary>
    /// Anything the shepherd can walk up to and use. One generic system serves
    /// feed/soothe/talk/inspect later (GDD slice 01). Implementors must live on
    /// a GameObject with a Collider2D (isTrigger recommended).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short verb phrase for the prompt UI, e.g. "Inspect".</summary>
        string PromptText { get; }

        bool CanInteract(GameObject actor);

        void Interact(GameObject actor);

        /// <summary>Called when this becomes / stops being the focused target (for highlight).</summary>
        void SetFocused(bool focused);
    }
}
