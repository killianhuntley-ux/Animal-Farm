using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Spirits;
using UnityEngine;

namespace AnimalFarm.World
{
    /// <summary>
    /// Essence economy source (slice 09): contentment IS production - GDD 3.5.
    /// Every 30 scaled seconds, each Resident with Spirit >= 70 has a 50%
    /// chance to shed one glowing essence mote at its position. The shepherd
    /// collects motes by walking over them.
    ///
    /// Pickups are NOT saved: an uncollected mote lost to a save/load cycle is
    /// an acceptable loss (they respawn from happy residents within a tick or
    /// two), and the world cap keeps litter bounded anyway.
    /// </summary>
    public class EssenceSpawner : MonoBehaviour
    {
        private const float TickSeconds = 30f;        // scaled, so it pauses with the game
        private const float SpiritThreshold = 70f;
        private const float ShedChance = 0.5f;
        private const int MaxLivePickups = 8;         // cap on uncollected motes in the world
        private const float ScatterRadius = 0.6f;     // small random offset off the spirit

        [Tooltip("Mote sprite. Falls back to a runtime white square if unassigned.")]
        [SerializeField] private Sprite moteSprite;
        [Tooltip("Optional shared sprite material (e.g. the soft URP ghost material).")]
        [SerializeField] private Material spriteMaterial;

        private readonly List<EssencePickup> _live = new List<EssencePickup>();
        private float _timer;

        // ---- 1x1 white fallback sprite (BoulderTrialEvent pattern) -----------

        private static Sprite _whiteSprite;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    // 4 pixels per unit -> the 4x4 texture is exactly 1x1 world units.
                    _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                return _whiteSprite;
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime; // scaled: no shedding while paused
            if (_timer < TickSeconds) return;
            _timer = 0f;

            Tick();
        }

        private void Tick()
        {
            // Collected (destroyed) pickups leave null holes behind.
            _live.RemoveAll(p => p == null);

            var manager = SpiritManager.Instance;
            if (manager == null) return;

            var spirits = manager.AllSpirits;
            if (spirits == null) return;

            for (int i = 0; i < spirits.Count; i++)
            {
                if (_live.Count >= MaxLivePickups) return;

                var agent = spirits[i];
                if (agent == null || agent.State != SpiritState.Resident) continue;
                if (agent.Spirit < SpiritThreshold) continue;
                if (Random.value >= ShedChance) continue;

                Vector3 pos = agent.transform.position
                    + (Vector3)(Random.insideUnitCircle * ScatterRadius);
                SpawnPickup(pos);
            }
        }

        private void SpawnPickup(Vector3 pos)
        {
            var go = new GameObject("EssencePickup");
            go.transform.position = new Vector3(pos.x, pos.y, 0f);

            var pickup = go.AddComponent<EssencePickup>();
            pickup.Init(moteSprite != null ? moteSprite : WhiteSprite, spriteMaterial);
            _live.Add(pickup);
        }
    }

    /// <summary>
    /// One glowing essence mote. Bobs and pulses gently; collected when the
    /// SHEPHERD (tag "Player") walks over its trigger. Persists until collected
    /// (the spawner's world cap handles litter). Not saved - see EssenceSpawner.
    /// </summary>
    public class EssencePickup : MonoBehaviour
    {
        private const float TriggerRadius = 0.45f;
        private const float BaseScale = 0.4f;
        private const float BobAmplitude = 0.07f;
        private const float BobFrequency = 0.8f;    // Hz
        private const float PulseAmplitude = 0.08f; // fraction of base scale
        private const float PulseFrequency = 1.3f;  // Hz

        private static readonly Color MoteGold = new Color(1f, 0.9f, 0.5f);

        private SpriteRenderer _renderer;
        private Vector3 _basePos;
        private float _phase;
        private bool _collected;

        /// <summary>Configures the mote; the spawner supplies sprite + material.</summary>
        public void Init(Sprite sprite, Material material)
        {
            _basePos = transform.position;
            _phase = Random.value * 10f; // desync the bobs of nearby motes

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = MoteGold;
            _renderer.sortingOrder = 3;
            if (material != null) _renderer.sharedMaterial = material;

            transform.localScale = Vector3.one * BaseScale;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = TriggerRadius;
        }

        private void Update()
        {
            // Gentle bob + pulse so the mote reads as alive even as a grey box.
            float t = Time.time + _phase;
            float y = Mathf.Sin(t * BobFrequency * 2f * Mathf.PI) * BobAmplitude;
            transform.position = _basePos + new Vector3(0f, y, 0f);

            float pulse = 1f + Mathf.Sin(t * PulseFrequency * 2f * Mathf.PI) * PulseAmplitude;
            transform.localScale = Vector3.one * (BaseScale * pulse);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_collected || other == null || !other.CompareTag("Player")) return;
            _collected = true;

            if (Inventory.Instance != null) Inventory.Instance.Add("essence", 1);
            AnimalFarm.UI.FloatingText.Show(
                transform.position + Vector3.up * 0.5f, "+1 essence", MoteGold);

            Destroy(gameObject);
        }
    }
}
