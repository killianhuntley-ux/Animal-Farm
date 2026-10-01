using System.Collections.Generic;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// Pair-interaction coordinator (muscle 03): when two idle spirits drift
    /// within arm's reach they either play (circle each other, happy chirps,
    /// a tiny Spirit bump) or squabble (quick back-off, grumpy blip, NO mood
    /// loss) based on species compatibility. Same species always play;
    /// cross-species like/dislike is a stable hash of the two ids, so the
    /// social map is consistent run to run (and weaving can read it later).
    /// Runtime GetOrCreate singleton - the first SpiritAgent.Init calls
    /// Ensure().
    /// </summary>
    public class SpiritSocialManager : MonoBehaviour
    {
        public static SpiritSocialManager Instance { get; private set; }

        private const float ScanInterval = 0.6f;
        private const float PairDistance = 1.5f;
        private const float PairCooldownSeconds = 25f; // real seconds, per pair

        private readonly Dictionary<long, float> _pairReadyAt = new Dictionary<long, float>();
        private readonly List<long> _expired = new List<long>();
        private float _scanTimer;

        /// <summary>Runtime GetOrCreate: builds the manager on first demand.</summary>
        public static void Ensure()
        {
            if (Instance != null || !Application.isPlaying) return;
            var go = new GameObject("SpiritSocialManager");
            go.AddComponent<SpiritSocialManager>();
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

        private void Update()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;

            var mgr = SpiritManager.Instance;
            if (mgr == null) return;
            var spirits = mgr.AllSpirits;

            for (int i = 0; i < spirits.Count; i++)
            {
                var a = spirits[i];
                if (a == null || !a.IsSocialIdle) continue;

                for (int j = i + 1; j < spirits.Count; j++)
                {
                    var b = spirits[j];
                    if (b == null || !b.IsSocialIdle) continue;

                    Vector3 d = a.transform.position - b.transform.position;
                    d.z = 0f;
                    if (d.sqrMagnitude > PairDistance * PairDistance) continue;

                    long key = PairKey(a, b);
                    if (_pairReadyAt.TryGetValue(key, out float readyAt)
                        && Time.unscaledTime < readyAt)
                        continue;
                    _pairReadyAt[key] =
                        Time.unscaledTime + PairCooldownSeconds + Random.Range(0f, 10f);

                    Vector3 mid = (a.transform.position + b.transform.position) * 0.5f;
                    if (LikesEachOther(a.Species, b.Species))
                    {
                        // Same spin + opposite starting angles = they chase
                        // each other around the midpoint.
                        a.BeginPairPlay(mid, clockwise: false);
                        b.BeginPairPlay(mid, clockwise: false);
                    }
                    else
                    {
                        a.BeginPairSquabble(b.transform.position);
                        b.BeginPairSquabble(a.transform.position);
                    }
                    break; // a is busy now; move to the next i
                }
            }

            if (_pairReadyAt.Count > 128) PruneCooldowns();
        }

        /// <summary>Order-independent key for one spirit pair.</summary>
        private static long PairKey(SpiritAgent a, SpiritAgent b)
        {
            int ia = a.GetInstanceID(), ib = b.GetInstanceID();
            int lo = Mathf.Min(ia, ib), hi = Mathf.Max(ia, ib);
            return ((long)hi << 32) ^ (uint)lo;
        }

        /// <summary>
        /// Species compatibility: kin always play; otherwise a deterministic
        /// hash of the ordered id pair decides like (play) vs dislike
        /// (squabble). Stable across runs and symmetric by construction.
        /// </summary>
        private static bool LikesEachOther(SpiritSpeciesDefinition a, SpiritSpeciesDefinition b)
        {
            if (a == null || b == null || string.IsNullOrEmpty(a.id) || string.IsNullOrEmpty(b.id))
                return true; // benefit of the doubt
            if (a.id == b.id) return true;

            string lo = string.CompareOrdinal(a.id, b.id) <= 0 ? a.id : b.id;
            string hi = lo == a.id ? b.id : a.id;
            string s = lo + "|" + hi;
            int h = 17;
            for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
            return (h & 1) == 0;
        }

        private void PruneCooldowns()
        {
            _expired.Clear();
            foreach (var pair in _pairReadyAt)
                if (Time.unscaledTime >= pair.Value) _expired.Add(pair.Key);
            for (int i = 0; i < _expired.Count; i++) _pairReadyAt.Remove(_expired[i]);
        }
    }
}
