using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 指で遊んでいる間だけ、Canvas の基準の大きさを <see cref="TouchReference"/> にして UI を 1.5 倍に描く (#187)。
    /// 横持ちのスマホ（844×390 CSS px）では、<see cref="PointerReference"/> のままだと会話の 32 px の文字が
    /// CSS で約 13 px まで縮み、指で叩く行も細くなる。マウスやキーに戻したら元の大きさに戻す。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class TouchCanvasScale : MonoBehaviour
    {
        /// <summary>キーボード・マウスで遊ぶときの基準。UI はこの大きさで組んである。</summary>
        public static readonly Vector2 PointerReference = new Vector2(1920f, 1080f);

        /// <summary>指で遊ぶときの基準。<see cref="PointerReference"/> の 2/3 なので、同じ画面で 1.5 倍に描く。</summary>
        public static readonly Vector2 TouchReference = new Vector2(1280f, 720f);

        private CanvasScaler _scaler;

        /// <summary>入力の種類に合う基準の大きさ。</summary>
        public static Vector2 ReferenceFor(bool touch)
        {
            return touch ? TouchReference : PointerReference;
        }

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
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
            Vector2 reference = ReferenceFor(InputMode.IsTouch);
            if (_scaler != null && _scaler.referenceResolution != reference)
            {
                _scaler.referenceResolution = reference;
            }
        }
    }
}
