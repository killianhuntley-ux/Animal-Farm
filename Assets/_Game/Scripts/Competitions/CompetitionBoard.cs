using System.Collections.Generic;
using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using UnityEngine;

namespace AnimalFarm.Competitions
{
    /// <summary>
    /// The competition notice board (slice 05). Interacting opens the entry
    /// modal; the board also listens for finished events and pays out fame,
    /// morale and prizes.
    /// </summary>
    public class CompetitionBoard : MonoBehaviour, IInteractable, ISelectable
    {
        private const float FocusScale = 1.06f;

        private static readonly Color PrizeGold = new Color(0.95f, 0.80f, 0.35f, 1f);
        private static readonly Color ResultCream = new Color(0.95f, 0.95f, 0.92f, 1f);

        [SerializeField] private Sprite boardSprite;
        [SerializeField] private Material spriteMaterial;

        private Vector3 _baseScale = Vector3.one;
        private bool _subscribed;

        private void Start()
        {
            _baseScale = transform.localScale;

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = boardSprite;
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.4f, 1.6f);

            WorldLabel.Attach(gameObject, "Competition Board", -1.0f);

            TrySubscribe();
        }

        private void Update()
        {
            // The manager may awake after us; keep retrying until we hook in.
            if (!_subscribed) TrySubscribe();
        }

        private void OnDestroy()
        {
            if (_subscribed && CompetitionManager.Instance != null)
                CompetitionManager.Instance.EventFinished -= HandleFinished;
        }

        private void TrySubscribe()
        {
            if (CompetitionManager.Instance == null) return;
            CompetitionManager.Instance.EventFinished += HandleFinished;
            _subscribed = true;
        }

        // ------------------------------------------------------------ Results

        private void HandleFinished(SpiritAgent spirit, CompetitionResult result)
        {
            if (spirit == null) return;

            spirit.RecordCompetition(result.Won);

            // Morale swing. SpiritAgent has no public morale setter besides the
            // debug one, so we lean on Debug_SetSpirit (it clamps internally too).
            if (result.Won)
                spirit.Debug_SetSpirit(Mathf.Min(100f, spirit.Spirit + 25f));
            else
                spirit.Debug_SetSpirit(Mathf.Max(0f, spirit.Spirit - 10f));

            if (result.Won) AwardPrize();

            string who = !string.IsNullOrEmpty(spirit.GivenName) ? spirit.GivenName
                : spirit.Species != null ? spirit.Species.displayName
                : "Spirit";
            string summary = result.Won
                ? who + " won the " + result.eventName + "!"
                : who + " placed " + result.placement + "/" + result.entrants;
            FloatingText.Show(spirit.transform.position + Vector3.up * 0.8f, summary,
                result.Won ? PrizeGold : ResultCream);
        }

        private void AwardPrize()
        {
            if (Inventory.Instance == null) return;

            // Prize purses (slice 09 economy): obols, scaled by difficulty.
            // Same purse for every event; the entry fee was already paid in
            // CompetitionEntryUI.
            int obols;
            switch (Mathf.Clamp(CompetitionEntryUI.LastDifficulty, 0, 2))
            {
                case 0: obols = 15; break;
                case 1: obols = 40; break;
                default: obols = 100; break;
            }

            Inventory.Instance.Add("coin", obols);
            FloatingText.Show(transform.position + Vector3.up * 0.8f,
                "Prize: " + obols + " obols", PrizeGold);
        }

        // ------------------------------------------------------- IInteractable

        public string PromptText => "Competitions";

        public bool CanInteract(GameObject actor) =>
            CompetitionManager.Instance != null && !CompetitionManager.Instance.EventRunning;

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor)) return;
            if (CompetitionEntryUI.Instance != null) CompetitionEntryUI.Instance.Open();
        }

        public void SetFocused(bool focused)
        {
            transform.localScale = focused ? _baseScale * FocusScale : _baseScale;
        }

        // ------------------------------------------------------- ISelectable

        public string SelectableTitle => "Competition Board";

        public void GetSelectActions(List<SelectAction> into)
        {
            if (into == null) return;
            // Same guard + open as the walk-up Interact path.
            into.Add(new SelectAction("Competitions", () => Interact(gameObject)));
        }
    }
}
