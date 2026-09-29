using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// Ticks every gate of every chain at 4 Hz (scaled time — pausing via
    /// Time.timeScale = 0 freezes evaluation), caches the results and fires
    /// <see cref="OnGateChanged"/> only on transitions. A chain gate whose
    /// RequirementSet is null is always CLOSED.
    /// </summary>
    public class RequirementEvaluator : MonoBehaviour
    {
        public static RequirementEvaluator Instance { get; private set; }

        private const float TickInterval = 0.25f; // 4 Hz

        [SerializeField] private GateChain[] chains;

        private static readonly Gate[] AllGates =
            { Gate.Appear, Gate.Visit, Gate.Resident, Gate.Fulfil };

        private static readonly GateChain[] Empty = new GateChain[0];

        private readonly Dictionary<(string chainId, Gate gate), bool> _cache =
            new Dictionary<(string, Gate), bool>();

        private float _timer;

        public IReadOnlyList<GateChain> Chains => chains ?? Empty;

        /// <summary>(chainId, gate, isOpen) — fired only when a gate's state flips.</summary>
        public event Action<string, Gate, bool> OnGateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            EvaluateAll(); // establish initial state (initial opens fire as transitions from closed)
        }

        private void Update()
        {
            _timer += Time.deltaTime; // scaled: pause freezes evaluation
            if (_timer < TickInterval) return;
            _timer -= TickInterval;
            EvaluateAll();
        }

        /// <summary>Last cached state; unknown chain/gate = closed.</summary>
        public bool IsGateOpen(string chainId, Gate gate) =>
            !string.IsNullOrEmpty(chainId)
            && _cache.TryGetValue((chainId, gate), out bool open)
            && open;

        private void EvaluateAll()
        {
            if (chains == null) return;

            for (int i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain == null || string.IsNullOrEmpty(chain.chainId)) continue;

                for (int g = 0; g < AllGates.Length; g++)
                {
                    Gate gate = AllGates[g];
                    var set = chain.GetSet(gate);
                    bool open = set != null && set.Evaluate(); // null set = closed

                    var key = (chain.chainId, gate);
                    _cache.TryGetValue(key, out bool previous); // unseen = closed
                    _cache[key] = open;
                    if (open == previous) continue;

                    Debug.Log($"[Gates] {chain.chainId}.{gate} -> {(open ? "OPEN" : "CLOSED")}");
                    OnGateChanged?.Invoke(chain.chainId, gate, open);
                }
            }
        }
    }
}
