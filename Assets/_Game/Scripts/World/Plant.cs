using AnimalFarm.Core;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// One growing plant instance. Never placed by hand — always created via
    /// <see cref="PlantManager.PlantSeed"/>. Growth is ACCUMULATED game-hours
    /// (not derived from the planting timestamp): each frame adds the elapsed
    /// game-hours scaled by soil moisture — watered soil grows at full speed,
    /// dry soil at half. Watering is a boost, never a death sentence — owner
    /// law: work is never wasted. Progress persists via GrowthHours.
    /// </summary>
    public class Plant : MonoBehaviour, AnimalFarm.Interaction.IInteractable
    {
        private const float DrySpeed = 0.5f; // dry dirt still grows, just slower

        private PlantSpecies _species;
        private Vector2Int _cell;
        private float _plantedAtTotalHours;
        private float _growthHours;
        private float _lastClockHours = float.NaN; // NaN = no clock sample yet

        private SpriteRenderer _renderer;
        private int _appliedStage = -1;
        private Vector3 _baseScale = Vector3.one;

        public PlantSpecies Species => _species;
        public Vector2Int Cell => _cell;

        /// <summary>Game-hours timestamp of planting (for save/migration).</summary>
        public float PlantedAtTotalHours => _plantedAtTotalHours;

        /// <summary>Accumulated effective growth, in game-hours (for save).</summary>
        public float GrowthHours => _growthHours;

        private int LastStageIndex =>
            _species != null && _species.stageSprites != null && _species.stageSprites.Length > 0
                ? _species.stageSprites.Length - 1
                : 0;

        /// <summary>Current growth stage, derived from accumulated growth-hours.</summary>
        public int Stage
        {
            get
            {
                if (_species == null) return 0;
                float perStage = Mathf.Max(0.01f, _species.hoursPerStage);
                int stage = Mathf.FloorToInt(_growthHours / perStage);
                return Mathf.Clamp(stage, 0, LastStageIndex);
            }
        }

        public bool IsMature => _species != null && Stage >= LastStageIndex;

        /// <summary>Called by PlantManager right after AddComponent.
        /// growthHours seeds accumulated progress (0 for a fresh seed; the
        /// save's value — or the migrated elapsed time — on restore).</summary>
        public void Init(PlantSpecies s, Vector2Int cell, float plantedAtTotalHours, float growthHours = 0f)
        {
            _species = s;
            _cell = cell;
            _plantedAtTotalHours = plantedAtTotalHours;
            _growthHours = Mathf.Max(0f, growthHours);
            _lastClockHours = float.NaN;

            name = "Plant_" + (s != null ? s.id : "unknown");

            if (TerrainGrid.Instance != null)
                transform.position = TerrainGrid.Instance.CellCenterWorld(cell);

            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<SpriteRenderer>();

            _baseScale = transform.localScale;
            _appliedStage = -1;
            ApplyStage();
        }

        /// <summary>Jumps accumulated growth to the last stage (debug/cheat).</summary>
        public void ForceMature()
        {
            if (_species == null) return;
            _growthHours = LastStageIndex * Mathf.Max(0.01f, _species.hoursPerStage);
            ApplyStage();
        }

        private void Update()
        {
            AccumulateGrowth();
            ApplyStage();
        }

        /// <summary>
        /// Adds this frame's elapsed game-hours, scaled by soil moisture:
        /// watered dirt = full speed, dry = half. Watering is a boost, never a
        /// death sentence — owner law: work is never wasted.
        /// </summary>
        private void AccumulateGrowth()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            float now = clock.TotalHours;
            if (!float.IsNaN(_lastClockHours))
            {
                float deltaHours = Mathf.Max(0f, now - _lastClockHours);
                bool watered = TerrainGrid.Instance != null && TerrainGrid.Instance.IsWatered(_cell);
                _growthHours += deltaHours * (watered ? 1f : DrySpeed);
            }
            _lastClockHours = now;
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
            AnimalFarm.Core.ShepherdProgress.Grant("harvest");
            VendorArrivals.Note("cropsHarvested"); // hidden vendor move-in milestone

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
