using AnimalFarm.Core;
using AnimalFarm.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalFarm.Onboarding
{
    /// <summary>
    /// The guide-light's one sanctioned reappearance channel (Muscle 10
    /// verdict 1: "Navi but less intrusive"). Other systems call
    /// GuideMoments.Announce(line) for TUTORIAL-TYPE beats only - a vendor
    /// moving in, a new system unlocking - and the line shows in the guide's
    /// visual style (the same top-center gold hum the onboarding HUD used)
    /// for a few seconds, then fades. Write lines in the guide's voice:
    /// short, dark-funny, kind underneath.
    ///
    /// Scoping: while a real tutorial step is active (OnboardingManager
    /// exists and is not complete) Announce does NOTHING, so the two never
    /// overlap and the tutorial keeps the single-line rule. No idle quips:
    /// if your beat is not introducing something new, do not call this.
    /// </summary>
    public static class GuideMoments
    {
        private const float HoldSeconds = 5f;
        private const float FadeSeconds = 1f;
        private const int FontSize = 24; // matches the onboarding objective line

        private static Runner _runner;

        /// <summary>Shows one guide-voiced line for a tutorial-type beat.
        /// Silently ignored during the real tutorial (no overlapping) and
        /// for empty lines. A new announce replaces the current one.</summary>
        public static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            // The onboarding tutorial owns the guide channel until it is done.
            var onboarding = OnboardingManager.Instance;
            if (onboarding != null && !onboarding.IsComplete) return;

            if (_runner == null)
            {
                var go = new GameObject("GuideMoments");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }
            _runner.Show(line);
        }

        /// <summary>Owns the single hum line: builds it lazily, holds, fades.</summary>
        private class Runner : MonoBehaviour
        {
            private Text _line;
            private float _elapsed;
            private bool _live;

            public void Show(string line)
            {
                if (_line == null && !BuildLine()) return; // no UI root yet

                _line.text = "The light hums: " + line;
                SetAlpha(1f);
                _line.gameObject.SetActive(true);
                _elapsed = 0f;
                _live = true;

                Bleeps.Play(BleepKind.Click, 0.6f);
            }

            private bool BuildLine()
            {
                var root = UIRoot.GetRoot();
                if (root == null) return false;

                // Same style and berth as the onboarding objective line.
                _line = UIRoot.MakeText(
                    root, "GuideMoment", FontSize, TextAnchor.MiddleCenter, UIStyle.GoldDim);

                var rt = _line.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -18f);
                rt.sizeDelta = new Vector2(1100f, 36f);

                _line.gameObject.SetActive(false);
                return true;
            }

            private void Update()
            {
                if (!_live || _line == null) return;

                _elapsed += Time.deltaTime; // scaled: it waits out a pause
                if (_elapsed <= HoldSeconds) return;

                float fadeT = (_elapsed - HoldSeconds) / FadeSeconds;
                if (fadeT >= 1f)
                {
                    _live = false;
                    _line.gameObject.SetActive(false);
                    return;
                }
                SetAlpha(1f - fadeT);
            }

            private void SetAlpha(float a)
            {
                var c = UIStyle.GoldDim;
                c.a *= Mathf.Clamp01(a);
                _line.color = c;
            }
        }
    }
}
