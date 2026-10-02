using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Interaction
{
    /// <summary>
    /// "E always opens the menu" (muscle 01 item 11). InteractionSensor hands the
    /// focused target here on every Interact press: an ISelectable gets its
    /// context menu (SelectionMenuUI, anchored at the target); a target with
    /// exactly one real action runs it directly (ASSUMPTION: no pointless
    /// one-item menu); an IInteractable that is not selectable keeps its
    /// direct Interact. The same rules drive the bottom prompt text.
    /// </summary>
    public static class InteractMenu
    {
        private static readonly List<SelectAction> Scratch = new List<SelectAction>();

        /// <summary>
        /// Convention shared by every ISelectable: a label wrapped in parentheses
        /// ("(too far to tend)", "(paid up for today)") is an information row,
        /// not a choice. Keyboard navigation skips it and it never counts as an action.
        /// </summary>
        public static bool IsInfoRow(string label) =>
            !string.IsNullOrEmpty(label) && label[0] == '(' && label[label.Length - 1] == ')';

        /// <summary>The ISelectable that goes with a focused target (itself, or a sibling component).</summary>
        public static ISelectable SelectableOf(IInteractable target)
        {
            if (target == null) return null;
            if (target is Object unityObj && unityObj == null) return null; // destroyed
            if (target is ISelectable direct) return direct;
            var comp = target as Component;
            return comp != null ? comp.GetComponent<ISelectable>() : null;
        }

        /// <summary>
        /// Handles an Interact press on the focused target. True when the press
        /// was consumed (menu opened, or the single action ran); false means the
        /// caller should do the plain direct Interact.
        /// </summary>
        public static bool TryHandlePress(IInteractable target)
        {
            if (!MenuApplies()) return false;

            var selectable = SelectableOf(target);
            if (selectable == null) return false;

            var menu = SelectionMenuUI.Instance;
            if (menu != null && menu.IsOpen) return true; // already up: the press is spent

            // Another modal / ceremony owns input: Interact is normally disabled
            // then, but never act on a stray press.
            if (UIInputLock.BlockDirectKeys || UIInputLock.CeremonyActive) return true;

            var actions = new List<SelectAction>();
            selectable.GetSelectActions(actions);
            if (actions.Count == 0) return false;

            int actionable = CountActionable(actions, out int first);
            if (actionable == 1 && !NeedsMenu(target, actions[first]))
            {
                // ASSUMPTION: exactly one real choice -> just do it.
                var act = actions[first];
                if (act.action != null) act.action();
                return true;
            }

            var ui = SelectionMenuUI.GetOrCreate();
            if (ui == null) return false;
            ui.Open(selectable, target as Component);
            return true;
        }

        /// <summary>Bottom-of-screen text for a focused target: "Options", the single
        /// action's label, or the target's own PromptText when E acts directly.</summary>
        public static string PromptFor(IInteractable target)
        {
            if (target == null) return "";
            var selectable = MenuApplies() ? SelectableOf(target) : null;
            if (selectable == null) return target.PromptText;

            Scratch.Clear();
            selectable.GetSelectActions(Scratch);
            if (Scratch.Count == 0) return target.PromptText;

            int actionable = CountActionable(Scratch, out int first);
            string text = actionable == 1 && !NeedsMenu(target, Scratch[first]) ? Scratch[first].label : "Options";
            Scratch.Clear();
            return text;
        }

        /// <summary>
        /// A selectable-only target (focused through SelectableFocus: Home, Watchlight,
        /// TrainingBuilding) whose lone action is a two-step row ("Destroy" then
        /// "Really destroy?") always gets the menu, so a double-tap of E can never
        /// confirm a destroy. Interactables like weeds keep their direct repeat-press.
        /// </summary>
        private static bool NeedsMenu(IInteractable target, SelectAction only) =>
            target is SelectableFocus && !only.closeOnRun;

        /// <summary>Counts non-info rows; <paramref name="first"/> is the first one (or -1).</summary>
        public static int CountActionable(List<SelectAction> actions, out int first)
        {
            first = -1;
            int n = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                if (IsInfoRow(actions[i].label)) continue;
                if (first < 0) first = i;
                n++;
            }
            return n;
        }

        // Mirrors SelectionController's click rule: no context menus mid-competition
        // (E stays the direct Interact there, exactly as before).
        private static bool MenuApplies()
        {
            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            return comp == null || !comp.EventRunning;
        }
    }
}
