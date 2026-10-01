using System.Collections;
using System.Collections.Generic;
using AnimalFarm.Interaction;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Spirits
{
    /// <summary>
    /// The Loom (slice 06): two max-Spirit residents are woven together into a
    /// cryptid. Interacting (or clicking) opens the WeaveUI to pick the pair;
    /// the ceremony pulls both spirits to the loom, swirls them together, and
    /// hands them to SpiritManager.Weave, which consumes them. Visuals are
    /// built in code from bootstrapper-assigned sprites.
    /// </summary>
    public class TheLoom : MonoBehaviour, IInteractable, ISelectable
    {
        private const float ApproachOffset = 1.2f;
        private const float ArriveRadius = 0.3f;
        private const float ArriveTimeout = 6f;
        private const float SwirlSeconds = 1.5f;
        private const float SwirlStartRadius = 1.2f;
        private const float SwirlEndRadius = 0.2f;
        private const float SwirlMinAlpha = 0.3f;
        private const float SwirlTurnsPerSecond = 1.2f;
        private const float ResultDropOffset = 1.2f;
        private const float PopSeconds = 0.4f;
        private const float PopStartScale = 2.2f; // 1.4x the agents' 1.6 base scale
        private const float PopEndScale = 1.6f;   // SpiritAgent.Init base scale
        private const float ShimmerBaseAlpha = 0.88f;
        private const float ShimmerAmplitude = 0.08f;
        private const float ShimmerFrequency = 0.6f; // Hz

        [Header("Visuals (assigned by bootstrapper)")]
        [SerializeField] private Sprite loomSprite;
        [SerializeField] private Material spriteMaterial;

        private SpriteRenderer _renderer;
        private bool _weaving;
        private bool _focused;

        /// <summary>
        /// Runtime factory for the buildable Loom. AddComponent runs Awake
        /// immediately but Start only on the next update, so InitVisuals gets
        /// the serialized fields in place before Start builds the body sprite.
        /// </summary>
        public static TheLoom Create(Vector3 pos, Sprite sprite, Material mat)
        {
            var go = new GameObject("TheLoom");
            go.transform.position = pos;
            var loom = go.AddComponent<TheLoom>();
            loom.InitVisuals(sprite, mat);
            return loom;
        }

        /// <summary>Code-path stand-in for the bootstrapper's private-field
        /// assignment; also refreshes the renderer if Start already ran.</summary>
        public void InitVisuals(Sprite sprite, Material mat)
        {
            loomSprite = sprite;
            spriteMaterial = mat;
            if (_renderer != null)
            {
                _renderer.sprite = sprite;
                if (mat != null) _renderer.sharedMaterial = mat;
            }
        }

        private void Start()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            body.transform.localScale = Vector3.one * 1.6f;
            _renderer = body.AddComponent<SpriteRenderer>();
            _renderer.sprite = loomSprite;
            _renderer.sortingOrder = -2;
            if (spriteMaterial != null) _renderer.sharedMaterial = spriteMaterial;

            // Trigger volume for the InteractionSensor / selection raycast.
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.6f, 1.8f);

            WorldLabel.Attach(gameObject, "The Loom", -1.1f);
        }

        private void Update()
        {
            // Gentle idle shimmer on the sprite alpha; solid while weaving.
            if (_renderer == null) return;
            float wave = Mathf.Sin(Time.time * ShimmerFrequency * 2f * Mathf.PI);
            float alpha = ShimmerBaseAlpha + wave * ShimmerAmplitude;
            if (_focused) alpha += 0.06f;
            if (_weaving) alpha = 1f;
            var c = _renderer.color;
            _renderer.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
        }

        private static bool CompetitionRunning =>
            AnimalFarm.Competitions.CompetitionManager.Instance != null
            && AnimalFarm.Competitions.CompetitionManager.Instance.EventRunning;

        private void OpenWeaveUI()
        {
            if (_weaving || CompetitionRunning) return;
            var ui = WeaveUI.Instance;
            if (ui == null) ui = new GameObject("WeaveUI").AddComponent<WeaveUI>();
            ui.Open();
        }

        // ---- IInteractable ----------------------------------------------------

        public string PromptText => "Weave spirits";

        public bool CanInteract(GameObject actor) => !_weaving && !CompetitionRunning;

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor)) return;
            OpenWeaveUI();
        }

        public void SetFocused(bool focused) => _focused = focused;

        // ---- ISelectable ------------------------------------------------------

        public string SelectableTitle => "The Loom";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            into.Add(new SelectAction("Weave", OpenWeaveUI));
        }

        // ---- ceremony -----------------------------------------------------------

        /// <summary>Runs the weaving ceremony. The WeaveUI validated the pair.</summary>
        public void RunWeave(SpiritAgent a, SpiritAgent b)
        {
            if (_weaving || a == null || b == null || a == b) return;
            StartCoroutine(WeaveRoutine(a, b));
        }

        private IEnumerator WeaveRoutine(SpiritAgent a, SpiritAgent b)
        {
            _weaving = true;

            Vector3 center = transform.position;
            Vector3 pointA = center + Vector3.left * ApproachOffset;
            Vector3 pointB = center + Vector3.right * ApproachOffset;

            a.SetFollowing(false);
            b.SetFollowing(false);
            a.EnterCeremony(pointA);
            b.EnterCeremony(pointB);

            // Wait for both to drift to their side of the loom (or give up).
            float deadline = Time.time + ArriveTimeout;
            while (Time.time < deadline && a != null && b != null
                   && ((a.transform.position - pointA).sqrMagnitude > ArriveRadius * ArriveRadius
                       || (b.transform.position - pointB).sqrMagnitude > ArriveRadius * ArriveRadius))
                yield return null;

            if (a == null || b == null)
            {
                // One of them despawned mid-approach; let the other go.
                if (a != null) a.ExitCeremony();
                if (b != null) b.ExitCeremony();
                _weaving = false;
                yield break;
            }

            // Swirl: both orbit the loom on opposite sides, spiraling inward and
            // fading. Positions are set manually; the ceremony drift now aims at
            // the loom center, so it only pulls the same way.
            a.EnterCeremony(center);
            b.EnterCeremony(center);
            float startAngle = Mathf.Atan2(
                a.transform.position.y - center.y, a.transform.position.x - center.x);
            for (float t = 0f; t < SwirlSeconds; t += Time.deltaTime)
            {
                if (a == null || b == null) break;
                float k = Mathf.Clamp01(t / SwirlSeconds);
                float radius = Mathf.Lerp(SwirlStartRadius, SwirlEndRadius, k);
                float angle = startAngle + t * SwirlTurnsPerSecond * 2f * Mathf.PI;

                a.transform.position = center
                    + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                b.transform.position = center
                    + new Vector3(Mathf.Cos(angle + Mathf.PI), Mathf.Sin(angle + Mathf.PI), 0f) * radius;

                float alpha = Mathf.Lerp(1f, SwirlMinAlpha, k);
                SetAgentAlpha(a, alpha);
                SetAgentAlpha(b, alpha);
                yield return null;
            }

            FloatingText.Show(center + Vector3.up * 1.5f, "The threads pull tight...", UIStyle.Grey);

            SpiritAgent woven = null;
            if (SpiritManager.Instance != null && a != null && b != null)
                woven = SpiritManager.Instance.Weave(a, b, center + Vector3.down * ResultDropOffset);

            if (woven == null)
            {
                // Shouldn't happen -- the UI validated the pair. Let them go;
                // their own TickVisuals restores color next frame.
                if (a != null) { a.ExitCeremony(); SetAgentAlpha(a, 1f); }
                if (b != null) { b.ExitCeremony(); SetAgentAlpha(b, 1f); }
                FloatingText.Show(center + Vector3.up * 1.1f, "(the loom is silent)", UIStyle.Grey);
            }
            else
            {
                // The parents were consumed by Weave; greet the newcomer with a
                // brief scale-pop down onto its 1.6 base scale.
                FloatingText.Show(woven.transform.position + Vector3.up * 1.2f,
                    woven.GivenName + "!", UIStyle.Gold);
                for (float t = 0f; t < PopSeconds; t += Time.deltaTime)
                {
                    if (woven == null) break;
                    float k = Mathf.Clamp01(t / PopSeconds);
                    woven.transform.localScale = Vector3.one * Mathf.Lerp(PopStartScale, PopEndScale, k);
                    yield return null;
                }
                if (woven != null) woven.transform.localScale = Vector3.one * PopEndScale;
            }

            _weaving = false;
        }

        private static void SetAgentAlpha(SpiritAgent agent, float alpha)
        {
            if (agent == null || agent.Renderer == null) return;
            var c = agent.Renderer.color;
            agent.Renderer.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
}
