using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Lazily builds the single runtime uGUI canvas ("UI_Canvas") that every
    /// HUD element parents itself under. All UI is constructed in code — no
    /// prefabs, no TextMeshPro (GDD conventions).
    /// </summary>
    public static class UIRoot
    {
        private static RectTransform _root;

        /// <summary>Returns the shared canvas root, creating it on first use.</summary>
        public static RectTransform GetRoot()
        {
            if (_root != null) return _root;

            var go = new GameObject("UI_Canvas");

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            _root = go.GetComponent<RectTransform>();
            return _root;
        }

        /// <summary>
        /// Creates a legacy uGUI Text under <paramref name="parent"/> using the
        /// builtin LegacyRuntime font. Caller positions the RectTransform.
        /// </summary>
        public static Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Buttons need an EventSystem; make one if the scene has none.</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            // Project uses the new Input System (see GameInput), so use its UI module.
            es.AddComponent<InputSystemUIInputModule>();
        }
    }
}
