using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// シーンを読み込む間に出す黒い画面。「読み込み中」と進み具合のバーだけの簡単な作り (#15)。
    /// <see cref="SceneLoader"/> が DontDestroyOnLoad の GameManager の子に 1 枚だけ作り、出し入れする。
    ///
    /// 画像は使わない（Image の色だけ）。出している間の描画は黒地とバーで 1 回、文字で 1 回ほど。
    /// 文字は TMP の既定フォント（KCD_JP）で描く。「読み込み中」と「Loading」の字は Data の JSON から焼いた字に入っている。
    /// </summary>
    public sealed class LoadingOverlay
    {
        /// <summary>フェード（100）と裏エンド（101 / 102）より上。読み込み中はどの画面よりも前に出す。</summary>
        public const int SortingOrder = 1000;

        public const string JapaneseLabel = "読み込み中";
        public const string EnglishLabel = "Loading";

        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);
        private static readonly Color Ink = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color BarBack = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color BarFill = new Color(0.31f, 0.82f, 0.85f, 1f);

        private const float LabelSize = 44f;
        private static readonly Vector2 LabelBox = new Vector2(800f, 80f);
        private static readonly Vector2 LabelPosition = new Vector2(0f, 24f);
        private static readonly Vector2 BarBox = new Vector2(640f, 10f);
        private static readonly Vector2 BarPosition = new Vector2(0f, -36f);

        private readonly GameObject _root;
        private readonly RectTransform _fill;
        private readonly TMP_Text _label;

        private LoadingOverlay(GameObject root, RectTransform fill, TMP_Text label)
        {
            _root = root;
            _fill = fill;
            _label = label;
        }

        /// <summary>画面の根。持ち主（GameManager）ごと消えると null に見える。</summary>
        public GameObject Root => _root;

        /// <summary>いま出しているか。</summary>
        public bool IsVisible => _root != null && _root.activeSelf;

        /// <summary>バーの伸び（0-1）。</summary>
        public float Progress { get; private set; }

        /// <summary>出している文字。</summary>
        public string Label => _label != null ? _label.text : string.Empty;

        /// <summary>言語ごとの文字。辞書（JSON）には載せず、クレジットや設定と同じくコードで持つ。</summary>
        public static string LabelFor(bool english)
        {
            return english ? EnglishLabel : JapaneseLabel;
        }

        /// <summary>parent の下に隠した状態で作る。</summary>
        public static LoadingOverlay Create(Transform parent)
        {
            var root = new GameObject("LoadingOverlay", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 黒地がマウスのクリックを受けて、下の画面へ通さない。
            root.AddComponent<GraphicRaycaster>();
            Image black = NewImage(root.transform, "Black", Color.black);
            Stretch(black.rectTransform);
            black.raycastTarget = true;

            TMP_Text label = NewLabel(root.transform);

            Image back = NewImage(root.transform, "BarBack", BarBack);
            Center(back.rectTransform, BarPosition, BarBox);

            Image fillImage = NewImage(back.transform, "BarFill", BarFill);
            RectTransform fill = fillImage.rectTransform;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            // 部品を付け終えてから隠す（TMP の Awake を付けた時点で済ませておく）。
            root.SetActive(false);
            return new LoadingOverlay(root, fill, label);
        }

        /// <summary>今の言語の文字を入れて出す。</summary>
        public void Show(float progress)
        {
            if (_root == null)
            {
                return;
            }

            if (_label != null)
            {
                _label.text = LabelFor(L.IsEnglish);
            }

            SetProgress(progress);
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>バーを伸ばす。0-1 の外は端に丸める。</summary>
        public void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            if (_fill != null)
            {
                _fill.anchorMax = new Vector2(Progress, 1f);
            }
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text NewLabel(Transform parent)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Center((RectTransform)go.transform, LabelPosition, LabelBox);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = JapaneseLabel;
            label.fontSize = LabelSize;
            label.color = Ink;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
