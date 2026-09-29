using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// One growing plant instance. Never placed by hand — always created via
    /// <see cref="PlantManager.PlantSeed"/>. Growth is derived from the planting
    /// timestamp against <see cref="GameClock.TotalHours"/>, so plants keep
    /// growing across save/load without per-frame bookkeeping.
    /// </summary>
    public class Plant : MonoBehaviour, AnimalFarm.Interaction.IInteractable
    {
        private PlantSpecies _species;
        private Vector2Int _cell;
        private float _plantedAtTotalHours;

        private SpriteRenderer _renderer;
        private int _appliedStage = -1;
        private Vector3 _baseScale = Vector3.one;

        public PlantSpecies Species => _species;
        public Vector2Int Cell => _cell;

        /// <summary>Game-hours timestamp of planting (for save).</summary>
        public float PlantedAtTotalHours => _plantedAtTotalHours;

        private int LastStageIndex =>
            _species != null && _species.stageSprites != null && _species.stageSprites.Length > 0
                ? _species.stageSprites.Length - 1
                : 0;

        /// <summary>Current growth stage, derived from elapsed game-hours.</summary>
        public int Stage
        {
            get
            {
                if (_species == null) return 0;
                float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : _plantedAtTotalHours;
                float perStage = Mathf.Max(0.01f, _species.hoursPerStage);
                int stage = Mathf.FloorToInt((now - _plantedAtTotalHours) / perStage);
                return Mathf.Clamp(stage, 0, LastStageIndex);
            }
        }

        public bool IsMature => _species != null && Stage >= LastStageIndex;

        /// <summary>Called by PlantManager right after AddComponent.</summary>
        public void Init(PlantSpecies s, Vector2Int cell, float plantedAtTotalHours)
        {
            _species = s;
            _cell = cell;
            _plantedAtTotalHours = plantedAtTotalHours;

            name = "Plant_" + (s != null ? s.id : "unknown");

            if (TerrainGrid.Instance != null)
                transform.position = TerrainGrid.Instance.CellCenterWorld(cell);

            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<SpriteRenderer>();

            _baseScale = transform.localScale;
            _appliedStage = -1;
            ApplyStage();
        }

        /// <summary>Back-dates the planting timestamp so the plant is at its last stage now.</summary>
        public void ForceMature()
        {
            if (_species == null) return;
            float now = GameClock.Instance != null ? GameClock.Instance.TotalHours : _plantedAtTotalHours;
            _plantedAtTotalHours = now - LastStageIndex * Mathf.Max(0.01f, _species.hoursPerStage);
            ApplyStage();
        }

        private void Update()
        {
            ApplyStage();
        }

        private void ApplyStage()
        {
            if (_species == null || _renderer == null) return;

            int stage = Stage;
            if (stage == _appliedStage) return;
            _appliedStage = stage;

            if (_species.stageSprites != null && _species.stageSprites.Length > 0)
                _renderer.sprite = _species.stageSprites[Mathf.Clamp(stage, 0, _species.stageSprites.Length - 1)];
            _renderer.color = _species.tint;
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText
        {
            get
            {
                string label = _species != null ? _species.displayName : "Plant";
                return IsMature ? "Harvest " + label : label + " (growing)";
            }
        }

        public bool CanInteract(GameObject actor) => IsMature;

        public void Interact(GameObject actor)
        {
            if (!IsMature || _species == null) return;

            if (Inventory.Instance != null)
                Inventory.Instance.Add(_species.produceId, _species.produceAmount);

            Debug.Log("[Plants] Harvested " + _species.displayName + " -> "
                      + _species.produceAmount + "x " + _species.produceId);

            if (PlantManager.Instance != null)
                PlantManager.Instance.RemovePlant(this);
            else
                Destroy(gameObject);
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * 1.08f : _baseScale;
        }
    }
}
