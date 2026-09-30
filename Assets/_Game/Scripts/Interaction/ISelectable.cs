using System.Collections.Generic;

namespace AnimalFarm.Interaction
{
    /// <summary>One entry in a selection context menu.</summary>
    public struct SelectAction
    {
        public string label;
        public System.Action action;
        /// <summary>False keeps the menu open (e.g. a two-step "Destroy?" confirm).</summary>
        public bool closeOnRun;

        public SelectAction(string label, System.Action action, bool closeOnRun = true)
        {
            this.label = label;
            this.action = action;
            this.closeOnRun = closeOnRun;
        }
    }

    /// <summary>
    /// Anything the player can CLICK to select (distinct from IInteractable's
    /// walk-up-and-press-E). Selection opens a context menu of actions.
    /// Implementors need a Collider2D for the click raycast.
    /// </summary>
    public interface ISelectable
    {
        /// <summary>Menu title, e.g. "Bansheep Home" or the spirit's name.</summary>
        string SelectableTitle { get; }

        /// <summary>Append this object's context actions.</summary>
        void GetSelectActions(List<SelectAction> into);
    }
}
