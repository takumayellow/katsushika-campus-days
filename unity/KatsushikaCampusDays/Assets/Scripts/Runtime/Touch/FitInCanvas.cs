using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 枠が Canvas に収まらないときだけ、収まるまで縮める (#187)。指で遊ぶ間は UI を 1.5 倍に描くので
    /// （<see cref="TouchCanvasScale"/>）、1920×1080 基準で組んだ大きな枠は、横持ちのスマホでは高さが、
    /// 4:3 のタブレットでは幅が足りなくなる。縮めるだけで大きくはしないので、PC の 16:9 では倍率は 1 のまま。
    /// 文字だけの枠は、縮める代わりに幅を Canvas に合わせて狭め、文字を折り返す（<see cref="BindReflow"/>）。
    /// 縦持ちのスマホでは Canvas の幅が 800 しかなく、幅 1520 の会話の枠を縮めると文字が 8 px まで小さくなる。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class FitInCanvas : MonoBehaviour
    {
        /// <summary>Canvas の端から空ける幅（基準の単位）。</summary>
        public const float Margin = 16f;

        [SerializeField] private bool _reflow;
        [SerializeField] private float _portraitHeight;
        [SerializeField] private bool _portraitOnly;

        private RectTransform _rect;
        private RectTransform _canvasRect;
        private Vector2 _size;
        private bool _hasSize;

        /// <summary>
        /// 幅が足りないときは縮めずに幅を狭める。portraitHeight が正なら、縦長の画面ではその高さにする
        /// （折り返して増えた行を入れる）。portraitOnly なら、横長の画面では組んだ大きさのまま縮める
        /// （中の箱を左右に並べていて、幅を狭めると重なる枠）。SceneBuilder から差し込む。
        /// </summary>
        public void BindReflow(float portraitHeight, bool portraitOnly = false)
        {
            _reflow = true;
            _portraitHeight = portraitHeight;
            _portraitOnly = portraitOnly;
        }

        /// <summary>
        /// 元の大きさを差し替える。画面の向きで枠の置き方を変えるとき（<see cref="TitleLayout"/>、
        /// <see cref="PhotoHintLayout"/>）に使う。
        /// 差し替えないと、次のフレームで元の大きさに書き戻される。
        /// </summary>
        public void SetSize(Vector2 size)
        {
            _size = size;
            _hasSize = true;
        }

        /// <summary>
        /// 幅 width の枠を、canvasWidth に左右 margin を空けて収める幅。収まっていれば元の幅。
        /// Canvas の幅が決まっていない（0 以下）ときも元の幅を返す。
        /// </summary>
        public static float WidthToFit(float width, float canvasWidth, float margin)
        {
            float room = canvasWidth - margin * 2f;
            return room <= 0f ? width : Mathf.Min(width, room);
        }

        /// <summary>
        /// size の枠を canvas に、上下左右 margin を空けて収める倍率。収まっていれば 1。
        /// 枠か Canvas の大きさが決まっていない（0 以下）ときも 1 を返し、何もしない。
        /// </summary>
        public static float ScaleToFit(Vector2 size, Vector2 canvas, float margin)
        {
            Vector2 room = canvas - new Vector2(margin * 2f, margin * 2f);
            if (size.x <= 0f || size.y <= 0f || room.x <= 0f || room.y <= 0f)
            {
                return 1f;
            }

            return Mathf.Min(1f, room.x / size.x, room.y / size.y);
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            if (!_hasSize)
            {
                _size = _rect.sizeDelta;
            }
        }

        private void OnEnable()
        {
            Canvas canvas = GetComponentInParent<Canvas>(true);
            _canvasRect = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
            Fit();
        }

        private void LateUpdate()
        {
            Fit();
        }

        private void Fit()
        {
            if (_canvasRect == null)
            {
                return;
            }

            if (_reflow)
            {
                Reflow();
            }

            float scale = ScaleToFit(_rect.rect.size, _canvasRect.rect.size, Margin);
            if (!Mathf.Approximately(_rect.localScale.x, scale))
            {
                _rect.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>枠の幅を Canvas に合わせ、縦長の画面では高さを足す。アンカーは 1 点に置いた枠だけを扱う。</summary>
        private void Reflow()
        {
            bool portrait = ScreenShape.IsPortrait;
            Vector2 size = _size;
            if (portrait || !_portraitOnly)
            {
                float height = _portraitHeight > 0f && portrait ? _portraitHeight : _size.y;
                size = new Vector2(WidthToFit(_size.x, _canvasRect.rect.width, Margin), height);
            }

            if (_rect.sizeDelta != size)
            {
                _rect.sizeDelta = size;
            }
        }
    }
}
