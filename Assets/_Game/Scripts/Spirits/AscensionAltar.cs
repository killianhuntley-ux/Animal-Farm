using System.Collections;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The ascension altar (slice 04). Lead a fulfilled resident here (following)
    /// and interact to begin the ceremony: the spirit steps onto the platform, a
    /// column of light rises, a farewell line floats up, a headstone is laid in
    /// the grave plot, and the spirit ascends. Unfulfilled attempts fail softly
    /// with a hint about what is still missing. Visuals are built in code from
    /// bootstrapper-assigned sprites.
    /// </summary>
    public class AscensionAltar : MonoBehaviour, IInteractable
    {
        private const float NearRadius = 2.6f;
        private const float ArriveRadius = 0.2f;
        private const float ArriveTimeout = 6f;
        private const float ColumnFadeIn = 1.2f;
        private const float FarewellHold = 1.2f;
        private const float ColumnFadeOut = 0.8f;

        private static readonly Color IdleGlow = new Color(0.75f, 0.85f, 1f, 0.3f);
        private static readonly Color GoldGlow = new Color(1f, 0.84f, 0.45f, 0.9f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.45f, 1f);
        private static readonly Color PaleGreyBlue = new Color(0.70f, 0.75f, 0.85f, 1f);

        private static readonly string[] FarewellLines =
        {
            "Off you go, then.",
            "The next field is greener.",
            "It waves. Sort of.",
            "Do not look back. It is, briefly."
        };

        [Header("Visuals (assigned by bootstrapper)")]
        [SerializeField] private Sprite platformSprite;
        [SerializeField] private Sprite glowSprite;
        [SerializeField] private Sprite columnSprite;
        [SerializeField] private Material spriteMaterial;

        private SpriteRenderer _platform;
        private SpriteRenderer _glow;
        private SpriteRenderer _column;

        private SpiritAgent _nearSpirit;
        private bool _ceremonyRunning;
        private float _flickerUntil;
        private bool _focused;

        private void Start()
        {
            _platform = MakeChild("Platform", platformSprite, -5);
            _glow = MakeChild("Glow", glowSprite, -4);

            _column = MakeChild("Column", columnSprite, 40);
            _column.transform.localScale = new Vector3(1.4f, 5f, 1f);
            _column.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            _column.gameObject.SetActive(false);

            AnimalFarm.UI.WorldLabel.Attach(gameObject, "Ascension Altar", -1.0f);

            // Trigger volume for the InteractionSensor.
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(2.2f, 1.6f);
        }

        private SpriteRenderer MakeChild(string childName, Sprite sprite, int order)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        private void Update()
        {
            RefreshNearSpirit();
            UpdateGlow();
        }

        /// <summary>Nearest FOLLOWING spirit within range, or null.</summary>
        private void RefreshNearSpirit()
        {
            _nearSpirit = null;

            var manager = SpiritManager.Instance;
            if (manager == null) return;

            var spirits = manager.AllSpirits;
            if (spirits == null) return;

            float bestSqr = NearRadius * NearRadius;
            for (int i = 0; i < spirits.Count; i++)
            {
                var agent = spirits[i];
                if (agent == null || !agent.IsFollowing) continue;

                float sqr = (agent.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; _nearSpirit = agent; }
            }
        }

        /// <summary>
        /// Idle: soft slow pulse. Fulfilled spirit near (or ceremony running):
        /// strong gold pulse. A fail beat flickers the glow briefly.
        /// </summary>
        private void UpdateGlow()
        {
            if (_glow == null) return;

            float alpha;
            Color color;
            bool charged = _ceremonyRunning || (_nearSpirit != null && _nearSpirit.IsFulfilled);
            if (charged)
            {
                float wave = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.5f, 0.9f, wave);
                color = GoldGlow;
            }
            else
            {
                float wave = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.15f, 0.3f, wave);
                color = IdleGlow;
            }

            if (Time.time < _flickerUntil)
                alpha *= 0.2f + 0.7f * Mathf.PingPong(Time.time * 14f, 1f);

            if (_focused) alpha = Mathf.Min(1f, alpha + 0.05f);

            _glow.color = new Color(color.r, color.g, color.b, alpha);
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText
        {
            get
            {
                if (_nearSpirit == null) return "The altar waits";
                return _nearSpirit.IsFulfilled ? "Begin Ascension" : "Attempt Ascension";
            }
        }

        public bool CanInteract(GameObject actor) =>
            _nearSpirit != null && !_ceremonyRunning;

        public void Interact(GameObject actor)
        {
            if (_ceremonyRunning || _nearSpirit == null) return;

            if (_nearSpirit.IsFulfilled)
                StartCoroutine(CeremonyRoutine(_nearSpirit));
            else
                FailBeat(_nearSpirit);
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
        }

        // ---- ceremony ---------------------------------------------------------

        private IEnumerator CeremonyRoutine(SpiritAgent spirit)
        {
            _ceremonyRunning = true;

            Vector3 center = PlatformCenter();
            spirit.SetFollowing(false);
            spirit.EnterCeremony(center);

            // Wait for the spirit to step onto the platform (or give up and go).
            float deadline = Time.time + ArriveTimeout;
            while (spirit != null && Time.time < deadline &&
                   (spirit.transform.position - center).sqrMagnitude > ArriveRadius * ArriveRadius)
                yield return null;

            if (spirit == null)
            {
                _ceremonyRunning = false;
                yield break;
            }

            // Column of light rises while the glow ramps gold (Update handles it).
            if (_column != null)
            {
                _column.gameObject.SetActive(true);
                SetColumnAlpha(0f);
                for (float t = 0f; t < ColumnFadeIn; t += Time.deltaTime)
                {
                    SetColumnAlpha(Mathf.Clamp01(t / ColumnFadeIn));
                    yield return null;
                }
                SetColumnAlpha(1f);
            }

            string farewell = FarewellLines[Random.Range(0, FarewellLines.Length)];
            FloatingText.Show(center + Vector3.up * 1.5f, farewell, Gold);

            yield return new WaitForSeconds(FarewellHold);

            if (spirit != null)
            {
                // Lay the headstone BEFORE the spirit despawns — its stats are
                // read off the live agent.
                if (HeadstoneRegistry.Instance != null)
                    HeadstoneRegistry.Instance.CreateHeadstone(spirit);
                if (SpiritManager.Instance != null)
                    SpiritManager.Instance.Ascend(spirit);
            }

            if (_column != null)
            {
                for (float t = 0f; t < ColumnFadeOut; t += Time.deltaTime)
                {
                    SetColumnAlpha(1f - Mathf.Clamp01(t / ColumnFadeOut));
                    yield return null;
                }
                _column.gameObject.SetActive(false);
            }

            _ceremonyRunning = false;
        }

        /// <summary>Soft failure: name what's missing, flicker; no state change.</summary>
        private void FailBeat(SpiritAgent spirit)
        {
            string spiritName = !string.IsNullOrEmpty(spirit.GivenName)
                ? spirit.GivenName
                : (spirit.Species != null ? spirit.Species.displayName : "The spirit");

            string hint;
            if (!spirit.HasHome)
                hint = "(" + spiritName + " needs a home)";
            else if (spirit.Spirit < 100f)
                hint = "(" + spiritName + "'s spirit is not yet full)";
            else if (!spirit.TaskDone)
                hint = spirit.Species != null && !string.IsNullOrEmpty(spirit.Species.taskHint)
                    ? spirit.Species.taskHint
                    : "(an unfinished wish lingers)";
            else
                hint = "(the altar is silent)";

            FloatingText.Show(PlatformCenter() + Vector3.up * 1.5f, hint, PaleGreyBlue);
            _flickerUntil = Time.time + 0.6f;
        }

        private Vector3 PlatformCenter() =>
            _platform != null ? _platform.transform.position : transform.position;

        private void SetColumnAlpha(float alpha)
        {
            if (_column == null) return;
            var c = _column.color;
            _column.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
}
