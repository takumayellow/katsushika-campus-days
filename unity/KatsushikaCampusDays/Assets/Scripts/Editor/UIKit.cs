using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KCD.Editor
{
    /// <summary>Canvas・矩形・文字といった、画面表示の下ごしらえ。</summary>
    public static partial class UIFactory
    {
        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);
        private static readonly Color Ink = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Panel = new Color(0.06f, 0.08f, 0.12f, 0.72f);
        private static readonly Color Accent = new Color(0.31f, 0.82f, 0.85f, 1f);

        private static TMP_FontAsset _font;

        /// <summary>Canvas を 1 枚作る。UI はすべてこの下にぶら下げる。</summary>
        public static Canvas CreateCanvas(Transform root, string name, int sortOrder)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(root, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>キーボード入力を受けるために EventSystem を 1 つ置く。</summary>
        public static void EnsureEventSystem(Transform root)
        {
            var go = new GameObject("EventSystem");
            go.transform.SetParent(root, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>矩形を 1 つ。アンカーと大きさだけ指定する軽い作り。</summary>
        public static RectTransform Rect(
            Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>親いっぱいに広がる矩形。</summary>
        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>半透明の下敷き。文字を読みやすくする。</summary>
        public static Image Backdrop(RectTransform parent, Color color)
        {
            RectTransform rect = Stretch(parent, "Backdrop");
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>文字。日本語フォントは FontLibrary が用意したものを使う。</summary>
        public static TMP_Text Label(
            RectTransform parent, string name, string text, float size, TextAlignmentOptions alignment)
        {
            RectTransform rect = Stretch(parent, name);
            rect.offsetMin = new Vector2(18f, 12f);
            rect.offsetMax = new Vector2(-18f, -12f);

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = Ink;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;

            TMP_FontAsset font = Font();
            if (font != null)
            {
                label.font = font;
            }

            return label;
        }

        private static TMP_FontAsset Font()
        {
            if (_font == null)
            {
                _font = FontLibrary.Ensure();
            }

            return _font;
        }

    }
}
