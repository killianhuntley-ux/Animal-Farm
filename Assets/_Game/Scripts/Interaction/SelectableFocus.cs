using UnityEngine;

namespace AnimalFarm.Interaction
{
    /// <summary>
    /// Focus stand-in for an ISelectable that is not an IInteractable (Home,
    /// TrainingBuilding, Watchlight). InteractionSensor adds one lazily the first
    /// time such an object is in range, so E can focus it exactly like any other
    /// target (gamepad parity with the mouse click): the shared FocusOutline
    /// shows, the prompt reads "[E] Options" (or the single action's label), and
    /// InteractMenu.TryHandlePress opens the sibling ISelectable's menu. The
    /// owner's own click behaviour is untouched. Never added to an object that
    /// already has a real IInteractable.
    /// </summary>
    public class SelectableFocus : MonoBehaviour, IInteractable
    {
        /// <summary>Empty: InteractMenu.PromptFor supplies the text via the sibling ISelectable.</summary>
        public string PromptText => "";

        public bool CanInteract(GameObject actor) => GetComponent<ISelectable>() != null;

        /// <summary>Menu handling lives in InteractMenu.TryHandlePress; a press that
        /// reaches here (e.g. mid-competition, when menus are off) does nothing.</summary>
        public void Interact(GameObject actor) { }

        public void SetFocused(bool focused) { }

        /// <summary>The focus stand-in for an object that is selectable but not interactable, or null.</summary>
        public static SelectableFocus For(GameObject go)
        {
            if (go == null) return null;
            var existing = go.GetComponent<SelectableFocus>();
            if (existing != null) return existing;
            if (go.GetComponent<ISelectable>() == null) return null;
            return go.AddComponent<SelectableFocus>();
        }
    }
}
