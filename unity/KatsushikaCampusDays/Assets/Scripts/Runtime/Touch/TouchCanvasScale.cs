using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// Canvas の基準の大きさを、入力の種類と画面の向きに合わせる (#187)。
    /// 横長の画面では、指で遊んでいる間だけ基準を <see cref="TouchReference"/> にして UI を 1.5 倍に描く。
    /// 横持ちのスマホ（844×390 CSS px）では、<see cref="PointerReference"/> のままだと会話の 32 px の文字が
    /// CSS で約 13 px まで縮み、指で叩く行も細くなる。マウスやキーに戻したら元の大きさに戻す。
    /// 縦長の画面では幅に合わせ、幅を <see cref="PortraitWidth"/>（指の操作の Canvas は <see cref="TouchPortraitWidth"/>）
    /// として描く。縦持ちのスマホ（390×844）でも、文字とボタンが横持ちのときと近い大きさになる。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class TouchCanvasScale : MonoBehaviour
    {
        /// <summary>キーボード・マウスで遊ぶときの基準。UI はこの大きさで組んである。</summary>
        public static readonly Vector2 PointerReference = new Vector2(1920f, 1080f);

        /// <summary>指で遊ぶときの基準。<see cref="PointerReference"/> の 2/3 なので、同じ画面で 1.5 倍に描く。</summary>
        public static readonly Vector2 TouchReference = new Vector2(1280f, 720f);

        /// <summary>縦長の画面での HUD とメニューの Canvas の幅。390 px の幅で、会話の 32 の文字が約 16 px になる。</summary>
        public const float PortraitWidth = 800f;

        /// <summary>縦長の画面での指の操作の Canvas の幅。横持ちのスマホ（高さ 390 を 720 で描く）とボタンが同じ大きさになる。</summary>
        public const float TouchPortraitWidth = 720f;

        /// <summary>縦長の画面で幅に合わせる（match = 0）。</summary>
        private const float PortraitMatch = 0f;

        [SerializeField] private bool _followTouch = true;
        [SerializeField] private float _portraitWidth = PortraitWidth;

        private CanvasScaler _scaler;
        private Vector2 _landscapeReference;
        private float _landscapeMatch;

        /// <summary>入力の種類に合う、横長の画面での基準の大きさ。</summary>
        public static Vector2 ReferenceFor(bool touch)
        {
            return touch ? TouchReference : PointerReference;
        }

        /// <summary>縦長の画面での基準の大きさ。幅に合わせるので高さは使わないが、縦長の比にしておく。</summary>
        public static Vector2 PortraitReference(float width)
        {
            return new Vector2(width, width * 2f);
        }

        /// <summary>
        /// 指の操作の Canvas 用。横長の画面では作ったときの基準のまま（入力の種類で変えない）にし、
        /// 縦長の画面だけ幅に合わせる。SceneBuilder から差し込む。
        /// </summary>
        public void BindFixed(float portraitWidth)
        {
            _followTouch = false;
            _portraitWidth = portraitWidth;
        }

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _landscapeReference = _scaler.referenceResolution;
            _landscapeMatch = _scaler.matchWidthOrHeight;
        }

        private void OnEnable()
        {
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        private void Sync()
        {
            if (_scaler == null)
            {
                return;
            }

            bool portrait = ScreenShape.IsPortrait;
            Vector2 reference;
            float match;
            if (portrait)
            {
                reference = PortraitReference(_portraitWidth);
                match = PortraitMatch;
            }
            else
            {
                reference = _followTouch ? ReferenceFor(InputMode.IsTouch) : _landscapeReference;
                match = _landscapeMatch;
            }

            if (_scaler.referenceResolution != reference)
            {
                _scaler.referenceResolution = reference;
            }

            if (!Mathf.Approximately(_scaler.matchWidthOrHeight, match))
            {
                _scaler.matchWidthOrHeight = match;
            }
        }
    }
}
