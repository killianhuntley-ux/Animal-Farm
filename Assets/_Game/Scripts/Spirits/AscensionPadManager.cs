using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Owns the built Ascension Pads (muscle 04): persists pad positions, and
    /// remembers that a spirit has EVER reached fulfilment — the flag that
    /// unlocks the pad row in the build menu (nothing else tracked that, so
    /// it lives here). Runtime GetOrCreate singleton (SpiritSocialManager
    /// pattern); SpiritManager.Awake calls Ensure() so the SaveSystem's
    /// initial scene scan finds the "ascension" key before its Start load.
    /// Degrades gracefully: no SpiritManager = no manager = no pad row.
    /// </summary>
    public class AscensionPadManager : MonoBehaviour, ISaveable
    {
        public static AscensionPadManager Instance { get; private set; }

        private const float FulfilmentScanInterval = 2f; // scaled seconds

        private readonly List<AscensionPad> _pads = new List<AscensionPad>();
        private bool _seenFulfilment;
        private float _scanTimer;

        /// <summary>True once any spirit has EVER been fulfilled (or ascended).</summary>
        public bool FulfilmentSeen => _seenFulfilment;

        /// <summary>The pad is unique — the build row greys out when one exists.</summary>
        public bool HasPad
        {
            get
            {
                _pads.RemoveAll(p => p == null);
                return _pads.Count > 0;
            }
        }

        /// <summary>First live pad, or null (ceremony anchor lookups).</summary>
        public AscensionPad FirstPad
        {
            get
            {
                _pads.RemoveAll(p => p == null);
                return _pads.Count > 0 ? _pads[0] : null;
            }
        }

        /// <summary>Runtime GetOrCreate: builds the manager on first demand.</summary>
        public static void Ensure()
        {
            if (Instance != null || !Application.isPlaying) return;
            var go = new GameObject("AscensionPadManager");
            go.AddComponent<AscensionPadManager>();
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

        public void Register(AscensionPad pad)
        {
            if (pad != null && !_pads.Contains(pad)) _pads.Add(pad);
        }

        public void Unregister(AscensionPad pad)
        {
            _pads.Remove(pad);
        }

        // ---- fulfilment watch -------------------------------------------------

        private void Update()
        {
            if (_seenFulfilment) return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < FulfilmentScanInterval) return;
            _scanTimer = 0f;

            var mgr = SpiritManager.Instance;
            if (mgr == null) return;

            // A fulfilled spirit standing in the field right now...
            var spirits = mgr.AllSpirits;
            if (spirits != null)
            {
                for (int i = 0; i < spirits.Count; i++)
                {
                    var a = spirits[i];
                    if (a != null && a.IsFulfilled) { MarkFulfilmentSeen(); return; }
                }
            }

            // ...or a pre-pad save where someone already ascended at the old altar.
            var species = mgr.KnownSpecies;
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                if (s != null && mgr.AscendedCount(s.id) > 0) { MarkFulfilmentSeen(); return; }
            }
        }

        /// <summary>One-way flag flip with a one-time unlock whisper.</summary>
        public void MarkFulfilmentSeen()
        {
            if (_seenFulfilment) return;
            _seenFulfilment = true;

            var player = GameObject.FindWithTag("Player");
            AnimalFarm.UI.FloatingText.Show(
                player != null ? player.transform.position + Vector3.up * 1.1f : Vector3.zero,
                "(the river has taken notice. The hammer learns: Ascension Pad)",
                new Color(0.75f, 0.85f, 1f));
        }

        // ---- ISaveable -------------------------------------------------------

        [Serializable]
        private class AscensionState
        {
            public bool seenFulfilment;
            public float[] padX;
            public float[] padY;
        }

        public string SaveKey => "ascension";

        public string Capture()
        {
            _pads.RemoveAll(p => p == null);
            var state = new AscensionState
            {
                seenFulfilment = _seenFulfilment,
                padX = new float[_pads.Count],
                padY = new float[_pads.Count]
            };
            for (int i = 0; i < _pads.Count; i++)
            {
                state.padX[i] = _pads[i].transform.position.x;
                state.padY[i] = _pads[i].transform.position.y;
            }
            return JsonUtility.ToJson(state);
        }

        public void Restore(string json)
        {
            for (int i = _pads.Count - 1; i >= 0; i--)
                if (_pads[i] != null) Destroy(_pads[i].gameObject);
            _pads.Clear();

            if (string.IsNullOrEmpty(json)) return; // pre-pad save: stays locked
            var state = JsonUtility.FromJson<AscensionState>(json);
            if (state == null) return;

            _seenFulfilment = state.seenFulfilment;

            if (state.padX == null || state.padY == null) return;
            int n = Mathf.Min(state.padX.Length, state.padY.Length);
            for (int i = 0; i < n; i++)
                AscensionPad.Create(new Vector3(state.padX[i], state.padY[i], 0f));
        }
    }
}
