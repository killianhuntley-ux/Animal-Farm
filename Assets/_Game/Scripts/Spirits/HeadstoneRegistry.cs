using System;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Owns creation and persistence of headstones (slice 04). New stones are
    /// laid out on an auto-grid in the grave plot: right-then-up from
    /// gravePlotOrigin, 6 per row. Restored stones keep their saved positions.
    /// </summary>
    public class HeadstoneRegistry : MonoBehaviour, ISaveable
    {
        public static HeadstoneRegistry Instance { get; private set; }

        private const float Spacing = 0.9f;
        private const int PerRow = 6;

        [SerializeField] private Sprite headstoneSprite;
        [SerializeField] private Material spriteMaterial;
        [Tooltip("World position where the auto-layout rows start.")]
        [SerializeField] private Vector2 gravePlotOrigin = new Vector2(4f, 8f);

        private readonly List<Headstone> _all = new List<Headstone>();
        public IReadOnlyList<Headstone> All => _all;

        /// <summary>The stone sprite (ceremony drop + placement ghost); may be
        /// null in unwired scenes — callers must degrade.</summary>
        public Sprite HeadstoneSprite => headstoneSprite;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Muscle 04: the garden grows itself around placed stones; created
            // here so no scene wiring is needed (derived from stones, no save state).
            if (GetComponent<MemorialGarden>() == null) gameObject.AddComponent<MemorialGarden>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Lays a headstone for a spirit about to ascend on the grave
        /// plot's auto-grid. Call BEFORE despawn.</summary>
        public Headstone CreateHeadstone(SpiritAgent spirit)
        {
            _all.RemoveAll(h => h == null);
            var stone = CreateHeadstoneAt(spirit, SlotPosition(_all.Count));
            if (stone != null) stone.MarkPlaced(); // auto-grid stones are already resting
            return stone;
        }

        /// <summary>
        /// Lays a headstone at an explicit position (the Styx crossing drops
        /// it at the pad; the player then places it where they like). Call
        /// BEFORE despawn — stats are read off the live agent.
        /// </summary>
        public Headstone CreateHeadstoneAt(SpiritAgent spirit, Vector3 pos)
        {
            if (spirit == null) return null;

            string spiritName = !string.IsNullOrEmpty(spirit.GivenName)
                ? spirit.GivenName
                : (spirit.Species != null ? spirit.Species.displayName : "Spirit");
            string speciesId = spirit.Species != null ? spirit.Species.id : "";
            string speciesDisplay = spirit.Species != null ? spirit.Species.displayName : "?";

            var clock = GameClock.Instance;
            float totalHours = clock != null ? clock.TotalHours : 0f;
            int ascendedDay = clock != null ? clock.Day : 0;
            float daysAmongUs = Mathf.Max(0f, (totalHours - spirit.ResidentSinceTotalHours) / 24f);

            _all.RemoveAll(h => h == null);
            var stone = Spawn(spiritName, speciesId, speciesDisplay,
                spirit.TimesFed, ascendedDay, daysAmongUs, pos);
            if (stone != null && clock != null) stone.CrossedHour = clock.Hours;
            return stone;
        }

        /// <summary>
        /// Console/QA only: lays a stone with no live spirit (species display
        /// name resolved from the known species). Placed stones start their
        /// garden clock now; unplaced ones wait like a fresh crossing drop.
        /// </summary>
        public Headstone Debug_CreateStone(string spiritName, string speciesId, Vector3 pos, bool placed)
        {
            var species = SpiritManager.Instance != null ? SpiritManager.Instance.FindSpecies(speciesId) : null;
            var clock = GameClock.Instance;
            _all.RemoveAll(h => h == null);
            var stone = Spawn(spiritName, speciesId,
                species != null ? species.displayName : "Spirit",
                UnityEngine.Random.Range(4, 20), clock != null ? clock.Day : 0, UnityEngine.Random.Range(3f, 15f), pos);
            if (stone == null) return null;
            if (clock != null) stone.CrossedHour = clock.Hours;
            stone.Witnesses = UnityEngine.Random.Range(0, 5);
            if (placed) stone.MarkPlaced();
            return stone;
        }

        /// <summary>Grid slots run right along a row, then up to the next row.</summary>
        private Vector3 SlotPosition(int index)
        {
            int col = index % PerRow;
            int row = index / PerRow;
            return new Vector3(
                gravePlotOrigin.x + col * Spacing,
                gravePlotOrigin.y + row * Spacing,
                0f);
        }

        private Headstone Spawn(string spiritName, string speciesId, string speciesDisplay,
            int timesFed, int ascendedDay, float daysAmongUs, Vector3 pos)
        {
            var go = new GameObject("Headstone");
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = headstoneSprite;
            sr.sortingOrder = 0;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.45f;

            var stone = go.AddComponent<Headstone>();
            stone.Init(spiritName, speciesId, speciesDisplay, timesFed, ascendedDay, daysAmongUs);
            AnimalFarm.UI.WorldLabel.Attach(go, spiritName, -0.55f);
            _all.Add(stone);
            return stone;
        }

        // ---- ISaveable -------------------------------------------------------

        [Serializable]
        private struct HeadstoneRecord
        {
            public string name;
            public string speciesId;
            public string speciesDisplay;
            public int timesFed;
            public int ascendedDay;
            public float daysAmongUs;
            public float x, y;
            // Memorial garden (version 2). Older saves lack these (v == 0) and
            // restore as placed, aged from their ascension day.
            public int v;
            public bool placed;
            public float placedHours;
            public float crossedHour;
            public int witnesses;
        }

        [Serializable]
        private class HeadstonesState { public List<HeadstoneRecord> stones = new List<HeadstoneRecord>(); }

        public string SaveKey => "headstones";

        public string Capture()
        {
            var state = new HeadstonesState();
            foreach (var stone in _all)
            {
                if (stone == null) continue;
                state.stones.Add(new HeadstoneRecord
                {
                    name = stone.SpiritName,
                    speciesId = stone.SpeciesId,
                    speciesDisplay = stone.SpeciesDisplay,
                    timesFed = stone.TimesFed,
                    ascendedDay = stone.AscendedDay,
                    daysAmongUs = stone.DaysAmongUs,
                    x = stone.transform.position.x,
                    y = stone.transform.position.y,
                    v = 2,
                    placed = stone.Placed,
                    placedHours = stone.PlacedHours,
                    crossedHour = stone.CrossedHour,
                    witnesses = stone.Witnesses
                });
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            for (int i = _all.Count - 1; i >= 0; i--)
                if (_all[i] != null) Destroy(_all[i].gameObject);
            _all.Clear();

            if (string.IsNullOrEmpty(json)) return;
            var state = JsonUtility.FromJson<HeadstonesState>(json);
            if (state?.stones == null) return;

            foreach (var record in state.stones)
            {
                var stone = Spawn(record.name, record.speciesId, record.speciesDisplay,
                    record.timesFed, record.ascendedDay, record.daysAmongUs,
                    new Vector3(record.x, record.y, 0f));
                if (stone == null) continue;

                if (record.v >= 2)
                {
                    stone.SetPlacement(record.placed, record.placedHours);
                    stone.CrossedHour = record.crossedHour;
                    stone.Witnesses = record.witnesses;
                }
                else
                {
                    stone.SetPlacement(true, Mathf.Max(0, record.ascendedDay - 1) * 24f);
                }
            }
        }
    }
}
