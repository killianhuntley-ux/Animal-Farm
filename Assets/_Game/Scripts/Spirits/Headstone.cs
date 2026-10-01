using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.Requirements;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// A permanent marker for an ascended spirit (slice 04). Built only by
    /// HeadstoneRegistry (which adds the sprite and collider). Interacting
    /// cycles through the epitaph lines. Registers a "headstone_&lt;speciesId&gt;"
    /// world object for the requirement engine — and never unregisters:
    /// legacy is permanent.
    /// </summary>
    public class Headstone : MonoBehaviour, IInteractable, ISelectable
    {
        private static readonly Color StoneGrey = new Color(0.78f, 0.80f, 0.82f, 1f);

        public string SpiritName;
        public string SpeciesId;
        public string SpeciesDisplay;
        public int TimesFed;
        public int AscendedDay;
        public float DaysAmongUs;

        private int _epitaphIndex;
        private Vector3 _baseScale;

        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        public void Init(string spiritName, string speciesId, string speciesDisplay,
            int timesFed, int ascendedDay, float daysAmongUs)
        {
            SpiritName = spiritName;
            SpeciesId = speciesId;
            SpeciesDisplay = speciesDisplay;
            TimesFed = timesFed;
            AscendedDay = ascendedDay;
            DaysAmongUs = daysAmongUs;
            name = "Headstone_" + spiritName;

            // OnEnable ran before Init could set SpeciesId; register here too.
            WorldObjectRegistry.Register("headstone_" + SpeciesId);
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText => "Remember " + SpiritName;

        public bool CanInteract(GameObject actor) => true;

        public void Interact(GameObject actor)
        {
            ShowNextEpitaph();
        }

        /// <summary>Shows the next epitaph line (shared by Interact and Read).</summary>
        private void ShowNextEpitaph()
        {
            string line;
            switch (_epitaphIndex % 4)
            {
                case 0: line = SpiritName + " the " + SpeciesDisplay; break;
                case 1: line = "Fed " + TimesFed + " times."; break;
                case 2: line = "Days among us: " + DaysAmongUs.ToString("0.#"); break;
                default: line = "Ascended Day " + AscendedDay + "."; break;
            }
            _epitaphIndex++;

            FloatingText.Show(transform.position + Vector3.up * 1.0f, line, StoneGrey);
        }

        // ---- ISelectable --------------------------------------------------------

        public string SelectableTitle => "Remember " + SpiritName;

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            // Keeps the menu open so repeated clicks cycle the epitaph lines.
            into.Add(new SelectAction("Read", ShowNextEpitaph, false));
            // Muscle 04: the garden is the player's to arrange — any stone can
            // be carried to a new resting place, free, any time.
            into.Add(new SelectAction("Move stone", BeginMove));
        }

        /// <summary>
        /// Free ghost placement for this stone (crossing handoff + later
        /// rearranging). Right-click cancels and the stone stays put, so a
        /// stone is never in limbo — it always exists somewhere saved.
        /// </summary>
        public void BeginMove()
        {
            var controller = AnimalFarm.Player.SelectionController.Instance;
            var sr = GetComponent<SpriteRenderer>();
            if (controller == null || sr == null || sr.sprite == null) return;

            var stone = this;
            controller.BeginPlaceBuilding(sr.sprite, transform.localScale.x,
                AnimalFarm.Player.SelectionController.IsPlaceableCell,
                (cell, world) =>
                {
                    if (stone == null) return;
                    stone.transform.position = world;
                    AnimalFarm.Core.Bleeps.Play(AnimalFarm.Core.BleepKind.Build, 0.6f);
                });
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * 1.06f : _baseScale;
        }

        // ---- world presence ---------------------------------------------------

        private void OnEnable()
        {
            // Never unregistered — legacy is permanent.
            if (!string.IsNullOrEmpty(SpeciesId))
                WorldObjectRegistry.Register("headstone_" + SpeciesId);
        }
    }
}
