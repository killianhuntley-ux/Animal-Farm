using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Simple id → count inventory (slice 02). No slots, no stacking limits;
    /// produce and seeds are just string ids.
    /// </summary>
    public class Inventory : MonoBehaviour, ISaveable
    {
        public static Inventory Instance { get; private set; }

        /// <summary>(id, new count) after any change.</summary>
        public event Action<string, int> OnChanged;

        private readonly Dictionary<string, int> _items = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> All => _items;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public int Count(string id) =>
            !string.IsNullOrEmpty(id) && _items.TryGetValue(id, out int n) ? n : 0;

        public void Add(string id, int n)
        {
            if (string.IsNullOrEmpty(id) || n <= 0) return;

            int newCount = Count(id) + n;
            _items[id] = newCount;
            OnChanged?.Invoke(id, newCount);
        }

        /// <summary>False (and no change) if there isn't enough.</summary>
        public bool Consume(string id, int n)
        {
            if (string.IsNullOrEmpty(id) || n <= 0) return false;

            int have = Count(id);
            if (have < n) return false;

            int newCount = have - n;
            if (newCount == 0) _items.Remove(id);
            else _items[id] = newCount;

            OnChanged?.Invoke(id, newCount);
            return true;
        }

        // ---- ISaveable (parallel arrays; JsonUtility can't do dictionaries) ----

        [Serializable]
        private struct InventoryState
        {
            public string[] ids;
            public int[] counts;
        }

        public string SaveKey => "inventory";

        public string Capture()
        {
            var ids = new string[_items.Count];
            var counts = new int[_items.Count];
            int i = 0;
            foreach (var pair in _items)
            {
                ids[i] = pair.Key;
                counts[i] = pair.Value;
                i++;
            }
            return JsonUtility.ToJson(new InventoryState { ids = ids, counts = counts });
        }

        public void Restore(string json)
        {
            _items.Clear();

            if (!string.IsNullOrEmpty(json))
            {
                var state = JsonUtility.FromJson<InventoryState>(json);
                if (state.ids != null && state.counts != null)
                {
                    int n = Mathf.Min(state.ids.Length, state.counts.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (!string.IsNullOrEmpty(state.ids[i]) && state.counts[i] > 0)
                            _items[state.ids[i]] = state.counts[i];
                    }
                }
            }

            foreach (var pair in _items)
                OnChanged?.Invoke(pair.Key, pair.Value);
        }
    }
}
