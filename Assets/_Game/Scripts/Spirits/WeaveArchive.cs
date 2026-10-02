using System;
using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Core.Saving;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The weaving record book (muscle 06): every weave ever performed (the
    /// woven-away parents, thread colors, the cryptid), the tapestry banners
    /// each one left behind (carried in the pouch as "tapestry_N" items, or
    /// hung in the world), and the rumors the player has heard (see
    /// WeaveArchive.Rumors.cs). The journal reads from here. Runtime
    /// GetOrCreate singleton (AscensionPadManager pattern): SpiritManager.Awake
    /// calls Ensure() so the SaveSystem's scene scan finds the "weaving" key
    /// before its Start load. The ceremony itself is never saved - a weave
    /// commits in one block (SpiritManager.Weave) and lands here.
    /// </summary>
    public partial class WeaveArchive : MonoBehaviour, ISaveable
    {
        public const string ItemPrefix = "tapestry_";

        public static WeaveArchive Instance { get; private set; }

        /// <summary>One weave: who went into the loom, what came out, the banner state.</summary>
        [Serializable]
        public class WeaveRecord
        {
            public int id;
            public int day;
            public string parentAName, parentASpeciesId, parentASpeciesName;
            public string parentBName, parentBSpeciesId, parentBSpeciesName;
            public string childName, childSpeciesId, childSpeciesName;
            public Color colorA, colorB;
            public bool hung;     // true = banner hangs in the world, false = carried (pouch)
            public float x, y;    // hung position
        }

        [Serializable]
        public class RumorRecord
        {
            public string resultId;   // the cryptid species the rumor points at
            public string teller;     // "The vendor", "The light", ...
            public string text;
        }

        [Serializable]
        private class ArchiveState
        {
            public List<WeaveRecord> weaves = new List<WeaveRecord>();
            public List<RumorRecord> rumors = new List<RumorRecord>();
            public int lastRumorDay = -1;
            public int nextId = 1;
        }

        private readonly List<WeaveRecord> _weaves = new List<WeaveRecord>();
        private readonly List<RumorRecord> _rumors = new List<RumorRecord>();
        private readonly Dictionary<int, TapestryBanner> _banners = new Dictionary<int, TapestryBanner>();
        private int _nextId = 1;
        private int _lastRumorDay = -1;

        public IReadOnlyList<WeaveRecord> Weaves => _weaves;
        public IReadOnlyList<RumorRecord> Rumors => _rumors;

        /// <summary>The id the NEXT weave will get (the rite pre-weaves a cloth with the banner's pattern).</summary>
        public int NextId => _nextId;

        /// <summary>Runtime GetOrCreate: builds the archive on first demand.</summary>
        public static void Ensure()
        {
            if (Instance != null || !Application.isPlaying) return;
            var go = new GameObject("WeaveArchive");
            go.AddComponent<WeaveArchive>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- weave records ----------------------------------------------------

        /// <summary>
        /// Logs a weave and adds its banner to the pouch. Called by
        /// SpiritManager.Weave inside the single commit block.
        /// </summary>
        public WeaveRecord AddWeave(string nameA, SpiritSpeciesDefinition speciesA,
            string nameB, SpiritSpeciesDefinition speciesB,
            string childName, SpiritSpeciesDefinition childSpecies, Color colorA, Color colorB)
        {
            var r = new WeaveRecord
            {
                id = _nextId++,
                day = GameClock.Instance != null ? GameClock.Instance.Day : 0,
                parentAName = nameA, parentASpeciesId = speciesA != null ? speciesA.id : "",
                parentASpeciesName = SpeciesLabel(speciesA),
                parentBName = nameB, parentBSpeciesId = speciesB != null ? speciesB.id : "",
                parentBSpeciesName = SpeciesLabel(speciesB),
                childName = childName,
                childSpeciesId = childSpecies != null ? childSpecies.id : "",
                childSpeciesName = SpeciesLabel(childSpecies),
                colorA = colorA, colorB = colorB,
                hung = false
            };
            _weaves.Add(r);
            if (Inventory.Instance != null) Inventory.Instance.Add(ItemId(r.id), 1);
            return r;
        }

        private static string SpeciesLabel(SpiritSpeciesDefinition s) =>
            s != null && !string.IsNullOrEmpty(s.displayName) ? s.displayName : (s != null ? s.id : "?");

        public WeaveRecord Find(int id)
        {
            for (int i = 0; i < _weaves.Count; i++)
                if (_weaves[i].id == id) return _weaves[i];
            return null;
        }

        /// <summary>All weaves that produced this cryptid species, oldest first.</summary>
        public List<WeaveRecord> WeavesFor(string childSpeciesId)
        {
            var list = new List<WeaveRecord>();
            if (string.IsNullOrEmpty(childSpeciesId)) return list;
            for (int i = 0; i < _weaves.Count; i++)
                if (_weaves[i].childSpeciesId == childSpeciesId) list.Add(_weaves[i]);
            return list;
        }

        /// <summary>The weave record that best matches a living cryptid: name match first, else the latest of its species.</summary>
        public WeaveRecord RecordFor(SpiritAgent agent)
        {
            if (agent == null || agent.Species == null) return null;
            WeaveRecord latest = null;
            for (int i = _weaves.Count - 1; i >= 0; i--)
            {
                var r = _weaves[i];
                if (r.childSpeciesId != agent.Species.id) continue;
                if (latest == null) latest = r;
                if (!string.IsNullOrEmpty(agent.GivenName)
                    && string.Equals(r.childName, agent.GivenName, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return latest;
        }

        /// <summary>Waits out the naming ceremony, then records the name the player chose for the cryptid.</summary>
        public void WatchNaming(WeaveRecord record, SpiritAgent child)
        {
            if (record != null && child != null) StartCoroutine(WatchNamingRoutine(record, child));
        }

        private IEnumerator WatchNamingRoutine(WeaveRecord record, SpiritAgent child)
        {
            float giveUp = Time.unscaledTime + 600f;
            yield return null; // let the ceremony claim Running first
            while (child != null && NamingCeremony.Running && Time.unscaledTime < giveUp)
                yield return null;
            if (child != null && !string.IsNullOrEmpty(child.GivenName))
                record.childName = child.GivenName;
        }

        // ---- banners (items + world) -------------------------------------------

        public static string ItemId(int weaveId) => ItemPrefix + weaveId;

        public static bool IsBannerItem(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith(ItemPrefix, StringComparison.Ordinal);

        public static bool TryParseItem(string id, out int weaveId)
        {
            weaveId = 0;
            return IsBannerItem(id)
                && int.TryParse(id.Substring(ItemPrefix.Length), out weaveId);
        }

        /// <summary>Pouch label: "Tapestry: Pip & Wisp".</summary>
        public static string BannerLabel(string id)
        {
            if (Instance != null && TryParseItem(id, out int n))
            {
                var r = Instance.Find(n);
                if (r != null) return "Tapestry: " + r.parentAName + " & " + r.parentBName;
            }
            return "Tapestry";
        }

        /// <summary>Weaves whose banner is in the pouch right now (not hung).</summary>
        public List<WeaveRecord> CarriedBanners()
        {
            var list = new List<WeaveRecord>();
            var inv = Inventory.Instance;
            if (inv == null) return list;
            for (int i = 0; i < _weaves.Count; i++)
                if (!_weaves[i].hung && inv.Count(ItemId(_weaves[i].id)) > 0) list.Add(_weaves[i]);
            return list;
        }

        /// <summary>Hang rule: usable land, nothing already built there, one banner per cell.</summary>
        public static bool CanHangAt(Vector2Int cell, TapestryBanner except)
        {
            var grid = TerrainGrid.Instance;
            if (grid == null || !grid.IsUsable(cell)) return false;
            if (!AnimalFarm.Player.SelectionController.IsPlaceableCell(cell)) return false;
            if (TrainingBuilding.AnyAtCell(cell)) return false;
            return !TapestryBanner.AnyAtCell(cell, except);
        }

        /// <summary>
        /// Placement ghost for a carried banner (Build menu row / post-weave hint).
        /// The banner leaves the pouch only when it is actually hung; right-click keeps it carried.
        /// </summary>
        public void BeginHang(WeaveRecord r)
        {
            if (r == null || r.hung) return;
            var sc = AnimalFarm.Player.SelectionController.Instance;
            var sprite = TapestrySprites.ForRecord(r);
            if (sc == null || sprite == null) return;

            sc.BeginPlaceBuilding(sprite, 1f, cell => CanHangAt(cell, null), (cell, world) =>
            {
                if (HangBanner(r, world))
                {
                    Bleeps.Play(BleepKind.Build, 0.7f);
                    FloatingText.Show(world + Vector3.up * 1.2f,
                        r.parentAName + " & " + r.parentBName + " hang here.", UIStyle.Cream);
                }
            });
        }

        /// <summary>Takes the banner out of the pouch and hangs it. False if it is not carried.</summary>
        public bool HangBanner(WeaveRecord r, Vector3 world)
        {
            if (r == null || r.hung) return false;
            if (Inventory.Instance == null || !Inventory.Instance.Consume(ItemId(r.id), 1)) return false;

            r.hung = true;
            r.x = world.x;
            r.y = world.y;
            SpawnBanner(r);
            return true;
        }

        /// <summary>Picks a hung banner back up (it returns to the pouch).</summary>
        public void TakeDownBanner(TapestryBanner banner)
        {
            if (banner == null) return;
            var r = Find(banner.WeaveId);
            _banners.Remove(banner.WeaveId);
            Destroy(banner.gameObject);
            if (r == null) return;

            r.hung = false;
            if (Inventory.Instance != null) Inventory.Instance.Add(ItemId(r.id), 1);
            FloatingText.Show(banner.transform.position + Vector3.up * 0.9f,
                "(banner folded away)", UIStyle.Grey);
        }

        private void SpawnBanner(WeaveRecord r)
        {
            if (_banners.TryGetValue(r.id, out var old) && old != null) Destroy(old.gameObject);
            var b = TapestryBanner.Create(r, new Vector3(r.x, r.y, 0f));
            if (b != null) _banners[r.id] = b;
        }

        // ---- ISaveable ----------------------------------------------------------

        public string SaveKey => "weaving";

        public string Capture()
        {
            var state = new ArchiveState { lastRumorDay = _lastRumorDay, nextId = _nextId };
            for (int i = 0; i < _weaves.Count; i++)
            {
                var r = _weaves[i];
                if (r.hung && _banners.TryGetValue(r.id, out var b) && b != null)
                {
                    r.x = b.transform.position.x; // moved banners keep their new spot
                    r.y = b.transform.position.y;
                }
                state.weaves.Add(r);
            }
            state.rumors.AddRange(_rumors);
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            foreach (var pair in _banners)
                if (pair.Value != null) Destroy(pair.Value.gameObject);
            _banners.Clear();
            TapestrySprites.ClearCache(); // ids may now map to different records: no stale art
            _weaves.Clear();
            _rumors.Clear();
            _nextId = 1;
            _lastRumorDay = -1;
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<ArchiveState>(json);
            if (state == null) return;

            _lastRumorDay = state.lastRumorDay;
            _nextId = Mathf.Max(1, state.nextId);
            if (state.rumors != null) _rumors.AddRange(state.rumors);
            if (state.weaves == null) return;

            for (int i = 0; i < state.weaves.Count; i++)
            {
                var r = state.weaves[i];
                if (r == null) continue;
                _weaves.Add(r);
                _nextId = Mathf.Max(_nextId, r.id + 1);
                if (r.hung) SpawnBanner(r);
            }
        }
    }
}
