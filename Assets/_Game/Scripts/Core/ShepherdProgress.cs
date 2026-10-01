using System;
using System.Collections.Generic;
using AnimalFarm.Core.Saving;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.Core
{
    /// <summary>
    /// Shepherd XP and level (muscle 01, verdict 2: "doing the work"). Every
    /// verb drips XP -- till, water, dig, sow, plant, harvest, build, feed,
    /// soothe -- so no action is ever wasted. Level N needs 100*N XP to reach
    /// level N+1; levels later gate tool tiers and vendor stock.
    ///
    /// Runtime-spawned via GetOrCreate (CompetitionManager pattern -- no scene
    /// setup, no bootstrapper edits). ToolController creates it in Awake so it
    /// exists before SaveSystem's Start-time scan picks up the "progress" key.
    ///
    /// Call sites use either AddXP(verb) on the instance or the static
    /// ShepherdProgress.Grant(verb) convenience, which is safe from anywhere.
    /// </summary>
    public class ShepherdProgress : MonoBehaviour, ISaveable
    {
        public static ShepherdProgress Instance { get; private set; }

        /// <summary>Total XP ever earned (levels are derived from this).</summary>
        public int TotalXp { get; private set; }

        /// <summary>Current level, starting at 1.</summary>
        public int Level { get; private set; } = 1;

        /// <summary>XP earned toward the next level.</summary>
        public int XpIntoLevel { get; private set; }

        /// <summary>XP needed to go from the current level to the next (100 * level).</summary>
        public int XpForNextLevel => XpToClimb(Level);

        /// <summary>Fired whenever XP or level changes (HUD refresh hook).</summary>
        public event Action OnProgressChanged;

        private static readonly Color LevelGold = new Color(1f, 0.84f, 0.25f, 1f);

        // Standard drip per verb. Unknown verbs fall back to 1 so a new call
        // site can never silently grant nothing.
        private static readonly Dictionary<string, int> VerbXp =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "till", 2 },
                { "water", 1 },
                { "dig", 2 },
                { "sow", 1 },
                { "plant", 2 },
                { "harvest", 3 },
                { "build", 8 },
                { "feed", 3 },
                { "soothe", 3 },
                { "herd", 2 },
                { "trial", 15 },
            };

        /// <summary>
        /// Returns the live progress tracker, creating one on the fly -- the
        /// system needs no scene setup, so runtime creation is safe.
        /// </summary>
        public static ShepherdProgress GetOrCreate()
        {
            if (Instance == null)
            {
                var go = new GameObject("ShepherdProgress (runtime)");
                go.AddComponent<ShepherdProgress>();
                go.AddComponent<ShepherdProgressHUD>();
            }
            return Instance;
        }

        /// <summary>
        /// Static convenience for call sites that should not care whether the
        /// tracker exists yet. amount &lt;= 0 means "the standard amount for
        /// this verb".
        /// </summary>
        public static void Grant(string verb, int amount = 0)
        {
            var progress = GetOrCreate();
            if (progress != null) progress.AddXP(verb, amount);
        }

        /// <summary>Adds the standard XP for a verb.</summary>
        public void AddXP(string verb) => AddXP(verb, 0);

        /// <summary>Adds XP for a verb; amount &lt;= 0 uses the standard drip.</summary>
        public void AddXP(string verb, int amount)
        {
            if (amount <= 0)
            {
                if (string.IsNullOrEmpty(verb) || !VerbXp.TryGetValue(verb, out amount))
                    amount = 1;
            }

            int levelBefore = Level;
            TotalXp += amount;
            RecalcLevel();

            if (Level > levelBefore) CelebrateLevelUp();
            OnProgressChanged?.Invoke();
        }

        /// <summary>XP needed to climb from <paramref name="level"/> to the next.</summary>
        public static int XpToClimb(int level) => 100 * Mathf.Max(1, level);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Derives Level and XpIntoLevel from TotalXp (curve: 100*N per level).</summary>
        private void RecalcLevel()
        {
            int level = 1;
            int remaining = TotalXp;
            while (remaining >= XpToClimb(level))
            {
                remaining -= XpToClimb(level);
                level++;
            }
            Level = level;
            XpIntoLevel = remaining;
        }

        /// <summary>Floating text over the shepherd + a happy chord.</summary>
        private void CelebrateLevelUp()
        {
            var player = GameObject.FindWithTag("Player");
            Vector3 pos = player != null
                ? player.transform.position + Vector3.up * 1.1f
                : Vector3.zero;

            AnimalFarm.UI.FloatingText.Show(pos, "Level " + Level + "!", LevelGold);
            Bleeps.Play(BleepKind.Ascend, 0.7f);
        }

        // ---- ISaveable ------------------------------------------------------

        [Serializable]
        private struct ProgressState
        {
            public int totalXp;
        }

        public string SaveKey => "progress";

        public string Capture() =>
            JsonUtility.ToJson(new ProgressState { totalXp = TotalXp });

        public void Restore(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var state = JsonUtility.FromJson<ProgressState>(json);
            TotalXp = Mathf.Max(0, state.totalXp);
            RecalcLevel(); // no level-up fanfare on restore
            OnProgressChanged?.Invoke();
        }
    }

    /// <summary>
    /// Small "Lv N" + thin XP bar, top-right under the clock HUD. Built once in
    /// code (UIStyle/UIRoot) and refreshed in place on OnProgressChanged. Lives
    /// on the same runtime object as ShepherdProgress (added by GetOrCreate).
    /// </summary>
    public class ShepherdProgressHUD : MonoBehaviour
    {
        private const float BarWidth = 150f;
        private const float BarPadding = 3f; // matches UIStyle.MakeBar fill inset

        private ShepherdProgress _progress;
        private Text _levelText;
        private RectTransform _fillRect;
        private bool _subscribed;

        private void Start()
        {
            _progress = GetComponent<ShepherdProgress>();

            var root = AnimalFarm.UI.UIRoot.GetRoot();

            // Top-right stack: ClockHUD (-20, 80 tall), calendar date button
            // (-104, 34 tall), then this (-146).
            var holder = new GameObject("ShepherdProgressHUD").AddComponent<RectTransform>();
            holder.SetParent(root, false);
            holder.anchorMin = new Vector2(1f, 1f);
            holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(1f, 1f);
            holder.anchoredPosition = new Vector2(-24f, -146f);
            holder.sizeDelta = new Vector2(260f, 44f);

            _levelText = AnimalFarm.UI.UIRoot.MakeText(
                holder, "LevelText", 22, TextAnchor.UpperRight, AnimalFarm.UI.UIStyle.Cream);
            var textRt = _levelText.rectTransform;
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(1f, 1f);
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta = new Vector2(0f, 26f);

            var shadow = _levelText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            // Thin XP bar under the text, right-aligned.
            var fillImg = AnimalFarm.UI.UIStyle.MakeBar(
                holder, "XpBar", out _fillRect, AnimalFarm.UI.UIStyle.Gold);
            var barRt = (RectTransform)_fillRect.parent;
            barRt.anchorMin = new Vector2(1f, 1f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -28f);
            barRt.sizeDelta = new Vector2(BarWidth, 10f);

            // HUD chrome must never swallow clicks.
            fillImg.raycastTarget = false;
            var barBgImg = barRt.GetComponent<Image>();
            if (barBgImg != null) barBgImg.raycastTarget = false;

            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (_subscribed || _progress == null) return;
            _progress.OnProgressChanged += Refresh;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && _progress != null)
                _progress.OnProgressChanged -= Refresh;
        }

        private void Refresh()
        {
            if (_progress == null || _levelText == null) return;

            _levelText.text = "Lv " + _progress.Level;

            float frac = _progress.XpForNextLevel > 0
                ? Mathf.Clamp01(_progress.XpIntoLevel / (float)_progress.XpForNextLevel)
                : 0f;
            float inner = BarWidth - BarPadding * 2f;
            _fillRect.sizeDelta = new Vector2(inner * frac, _fillRect.sizeDelta.y);
        }
    }
}
