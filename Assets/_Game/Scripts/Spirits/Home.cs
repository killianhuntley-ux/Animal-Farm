using System.Collections.Generic;
using AnimalFarm.Interaction;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// A placed spirit home (slice 04): species-specific, capacity one. Homeless
    /// residents claim the nearest free home of their species on a slow tick
    /// (SpiritAgent side). "Has a home" is one of the three fulfilment gates.
    /// Created only via HomeManager.PlaceHome. Click-selectable (slice: click
    /// selection): Move / occupant label / two-step Destroy.
    /// </summary>
    public class Home : MonoBehaviour, ISelectable
    {
        private static readonly List<Home> _all = new List<Home>();
        public static IReadOnlyList<Home> All => _all;

        public string SpeciesId { get; private set; }
        public string DisplayName { get; private set; }
        public Vector2Int Cell { get; private set; }
        public SpiritAgent Occupant { get; private set; }
        public bool IsFree => Occupant == null;

        private bool _confirmingDestroy;

        public void Init(string speciesId, Vector2Int cell, Vector3 worldPos)
        {
            Init(speciesId, cell, worldPos, null);
        }

        public void Init(string speciesId, Vector2Int cell, Vector3 worldPos, string displayName)
        {
            SpeciesId = speciesId;
            DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : speciesId;
            Cell = cell;
            transform.position = worldPos;
            name = "Home_" + speciesId;
        }

        /// <summary>
        /// Repositions a placed home (move mode). The occupant keeps its claim
        /// and wanders to the new spot naturally.
        /// </summary>
        public void MoveTo(Vector2Int cell, Vector3 worldPos)
        {
            Cell = cell;
            transform.position = worldPos;
        }

        public bool TryClaim(SpiritAgent agent)
        {
            if (agent == null || Occupant != null) return false;
            if (agent.Species == null || agent.Species.id != SpeciesId) return false;
            Occupant = agent;
            return true;
        }

        public void Release(SpiritAgent agent)
        {
            if (Occupant == agent) Occupant = null;
        }

        /// <summary>Nearest free home for a species, or null.</summary>
        public static Home FindFree(string speciesId, Vector3 nearPos)
        {
            Home best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _all.Count; i++)
            {
                var h = _all[i];
                if (h == null || !h.IsFree || h.SpeciesId != speciesId) continue;
                float sqr = (h.transform.position - nearPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = h; }
            }
            return best;
        }

        public static bool AnyAtCell(Vector2Int cell)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].Cell == cell) return true;
            return false;
        }

        // ---- ISelectable -------------------------------------------------------

        public string SelectableTitle =>
            (!string.IsNullOrEmpty(DisplayName) ? DisplayName
                : !string.IsNullOrEmpty(SpeciesId) ? SpeciesId : "Spirit") + " Home";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;

            into.Add(new SelectAction("Move", () =>
            {
                var selection = AnimalFarm.Player.SelectionController.Instance;
                if (selection != null) selection.BeginMove(this);
            }));

            // Occupant line: a label-only no-op action (keeps the menu open).
            string occupantLabel = "Vacant";
            if (Occupant != null)
            {
                string who = !string.IsNullOrEmpty(Occupant.GivenName) ? Occupant.GivenName
                    : Occupant.Species != null && !string.IsNullOrEmpty(Occupant.Species.displayName)
                        ? Occupant.Species.displayName
                        : "Spirit";
                occupantLabel = "Occupant: " + who;
            }
            into.Add(new SelectAction(occupantLabel, () => { }, false));

            // Two-step destroy: first click relabels, second click commits.
            if (!_confirmingDestroy)
            {
                into.Add(new SelectAction("Destroy", () => { _confirmingDestroy = true; }, false));
            }
            else
            {
                into.Add(new SelectAction("Really destroy?", () =>
                {
                    _confirmingDestroy = false;
                    Destroy(gameObject); // occupant is released via OnDisable
                }));
            }
        }

        private void OnEnable() => _all.Add(this);

        private void OnDisable()
        {
            _all.Remove(this);
            if (Occupant != null) Occupant.NotifyHomeLost(this);
        }
    }
}
