using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 左上の枠を、Canvas の幅の <see cref="Share"/> より右へはみ出させない (#187)。はみ出すなら角に寄せたまま縮める。
    /// 指で遊ぶときは UI が 1.5 倍になり、4:3 のタブレットではクエストの枠が画面の中央まで届いて、
    /// 上の真ん中のポーズのボタンに重なる。横長の画面では縮まない。
    /// 縦長の画面では <see cref="PortraitShare"/> まで使う。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class KeepLeftOfCenter : MonoBehaviour
    {
        /// <summary>左端から数えて使ってよい幅の割合。真ん中のボタン（半径は幅の 5% ほど）の手前で止まる。</summary>
        public const float Share = 0.44f;

        /// <summary>
        /// 縦長の画面で使ってよい幅の割合。縦長の画面では真ん中のボタンを枠の下へ移し、右上はミニマップだけになるので、
        /// ミニマップ（Canvas の幅 800 のうち右の 226）の手前まで使う。
        /// </summary>
        public const float PortraitShare = 0.7f;

        private RectTransform _rect;
        private RectTransform _canvasRect;
        private Vector2 _position;

        /// <summary>左端から extent まで届く枠を、幅 canvasWidth の share に収める倍率。収まるなら 1。</summary>
        public static float ScaleToShare(float extent, float canvasWidth, float share)
        {
            if (extent <= 0f || canvasWidth <= 0f)
            {
                return 1f;
            }

            return Mathf.Min(1f, canvasWidth * share / extent);
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _position = _rect.anchoredPosition;
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

        /// <summary>角からの距離も同じ倍率で縮める。でないと縮めた分だけ枠が内側へずれる。</summary>
        private void Fit()
        {
            if (_canvasRect == null)
            {
                return;
            }

            float extent = Mathf.Abs(_position.x) + _rect.rect.width;
            float scale = ScaleToShare(extent, _canvasRect.rect.width, ScreenShape.IsPortrait ? PortraitShare : Share);
            if (!Mathf.Approximately(_rect.localScale.x, scale))
            {
                _rect.localScale = new Vector3(scale, scale, 1f);
                _rect.anchoredPosition = _position * scale;
            }
        }
    }
}
