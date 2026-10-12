using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 枠が Canvas に収まらないときだけ、収まるまで縮める (#187)。指で遊ぶ間は UI を 1.5 倍に描くので
    /// （<see cref="TouchCanvasScale"/>）、1920×1080 基準で組んだ大きな枠は、横持ちのスマホでは高さが、
    /// 4:3 のタブレットでは幅が足りなくなる。縮めるだけで大きくはしないので、PC の 16:9 では倍率は 1 のまま。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class FitInCanvas : MonoBehaviour
    {
        /// <summary>Canvas の端から空ける幅（基準の単位）。</summary>
        public const float Margin = 16f;

        private RectTransform _rect;
        private RectTransform _canvasRect;

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

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
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

            float scale = ScaleToFit(_rect.rect.size, _canvasRect.rect.size, Margin);
            if (!Mathf.Approximately(_rect.localScale.x, scale))
            {
                _rect.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
