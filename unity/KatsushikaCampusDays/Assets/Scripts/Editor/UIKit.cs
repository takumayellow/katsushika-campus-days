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
        private static readonly Color Ink = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Panel = new Color(0.06f, 0.08f, 0.12f, 0.72f);
        private static readonly Color Accent = new Color(0.31f, 0.82f, 0.85f, 1f);

        private static TMP_FontAsset _font;

        /// <summary>
        /// Canvas を 1 枚作る。UI はすべてこの下にぶら下げる。followTouch のときは、指で遊ぶ間だけ
        /// 1.5 倍に描く（<see cref="TouchCanvasScale"/>）。
        /// </summary>
        public static Canvas CreateCanvas(Transform root, string name, int sortOrder, bool followTouch = true)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(root, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = TouchCanvasScale.PointerReference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            TouchCanvasScale scale = go.AddComponent<TouchCanvasScale>();
            if (!followTouch)
            {
                scale.BindFixed(TouchCanvasScale.TouchPortraitWidth);
            }

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// 枠が Canvas に収まらないときは収まるまで縮める（<see cref="FitInCanvas"/>）。
        /// 指で遊ぶ間は 1.5 倍に描くので、大きな枠は横持ちのスマホやタブレットではみ出す。
        /// </summary>
        public static void KeepInCanvas(RectTransform rect)
        {
            rect.gameObject.AddComponent<FitInCanvas>();
        }

        /// <summary>
        /// 文字だけの枠は、Canvas の幅が足りなければ縮めずに幅を狭めて折り返す（<see cref="FitInCanvas.BindReflow"/>）。
        /// portraitHeight が正なら、縦長の画面ではその高さにして増えた行を入れる。
        /// portraitOnly なら、横長の画面では幅を狭めずに枠ごと縮める。
        /// </summary>
        public static void ReflowInCanvas(RectTransform rect, float portraitHeight = 0f, bool portraitOnly = false)
        {
            rect.gameObject.AddComponent<FitInCanvas>().BindReflow(portraitHeight, portraitOnly);
        }

        /// <summary>
        /// 1 行で見せたい見出しや案内。幅を狭めたときに折り返して枠からはみ出さないよう、minSize まで文字を小さくする。
        /// </summary>
        public static void ReflowLabel(RectTransform rect, TMP_Text label, float minSize)
        {
            ReflowInCanvas(rect);
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = minSize;
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

        /// <summary>parent の直下にある name の枠。UIFactory が付けた名前で探すので、見つからなければ組み立てを止める。</summary>
        public static RectTransform Child(RectTransform parent, string name)
        {
            if (!(parent.Find(name) is RectTransform child))
            {
                throw new System.InvalidOperationException(parent.name + " の直下に " + name + " が無い");
            }

            return child;
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
