using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalFarm.Requirements
{
    /// <summary>
    /// F1-toggled IMGUI panel (right side) showing every gate chain, its four
    /// gates and each condition's live Describe(). Conditions are only
    /// evaluated while the panel is open, so the overlay is free when hidden.
    /// </summary>
    public class RequirementDebugOverlay : MonoBehaviour
    {
        private const float PanelWidth = 360f;

        private const string ColorOpen = "#7CE87C";
        private const string ColorClosed = "#9A9A9A";
        private const string ColorFail = "#FF7A6B";

        private bool _visible;
        private Vector2 _scroll;
        private GUIStyle _label;
        private GUIStyle _header;

        private void Update()
        {
            if (Keyboard.current?.f1Key.wasPressedThisFrame == true)
                _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 12 };
                _header = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontStyle = FontStyle.Bold,
                    fontSize = 13
                };
            }

            var panel = new Rect(Screen.width - PanelWidth, 0f, PanelWidth, Screen.height);

            // Dark translucent background.
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prev;

            GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 8f, panel.width - 16f, panel.height - 16f));
            GUILayout.Label("<color=white>Requirement Gates (F1)</color>", _header);

            var evaluator = RequirementEvaluator.Instance;
            if (evaluator == null)
            {
                GUILayout.Label($"<color={ColorFail}>No RequirementEvaluator in scene.</color>", _label);
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                var chains = evaluator.Chains;
                if (chains.Count == 0)
                    GUILayout.Label($"<color={ColorClosed}>No gate chains assigned.</color>", _label);

                for (int i = 0; i < chains.Count; i++)
                {
                    var chain = chains[i];
                    if (chain == null) continue;

                    GUILayout.Space(6f);
                    GUILayout.Label($"<color=white>{chain.chainId}</color>", _header);
                    DrawGate(evaluator, chain, Gate.Appear);
                    DrawGate(evaluator, chain, Gate.Visit);
                    DrawGate(evaluator, chain, Gate.Resident);
                    DrawGate(evaluator, chain, Gate.Fulfil);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void DrawGate(RequirementEvaluator evaluator, GateChain chain, Gate gate)
        {
            bool open = evaluator.IsGateOpen(chain.chainId, gate);
            string gateColor = open ? ColorOpen : ColorClosed;
            GUILayout.Label($"  <color={gateColor}>{gate}: {(open ? "OPEN" : "closed")}</color>", _label);

            var set = chain.GetSet(gate);
            if (set == null)
            {
                GUILayout.Label($"    <color={ColorClosed}>(no requirement set — always closed)</color>", _label);
                return;
            }

            var conditions = set.Conditions;
            if (conditions == null || conditions.Length == 0)
            {
                GUILayout.Label($"    <color={ColorClosed}>(no conditions — always met)</color>", _label);
                return;
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];
                if (condition == null) continue;

                bool met = condition.Evaluate();
                string color = met ? ColorOpen : ColorFail;
                GUILayout.Label($"    <color={color}>{condition.Describe()}</color>", _label);
            }
        }
    }
}
