using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 縦長の画面では、枠を別のアンカーと位置に置く。指の操作の上端のボタン（ポーズとクエストログ）に使う。
    /// 横長の画面では上端の真ん中に置くが、縦長の画面では左上のクエストの枠と右上のミニマップの間に入らないので、
    /// クエストの枠の下へ移す。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PortraitPosition : MonoBehaviour
    {
        [SerializeField] private Vector2 _portraitAnchor;
        [SerializeField] private Vector2 _portraitPosition;

        private RectTransform _rect;
        private Vector2 _anchorMin;
        private Vector2 _anchorMax;
        private Vector2 _position;

        /// <summary>縦長の画面でのアンカー（1 点）と anchoredPosition。SceneBuilder から差し込む。</summary>
        public void Bind(Vector2 portraitAnchor, Vector2 portraitPosition)
        {
            _portraitAnchor = portraitAnchor;
            _portraitPosition = portraitPosition;
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _anchorMin = _rect.anchorMin;
            _anchorMax = _rect.anchorMax;
            _position = _rect.anchoredPosition;
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
            bool portrait = ScreenShape.IsPortrait;
            Vector2 anchorMin = portrait ? _portraitAnchor : _anchorMin;
            Vector2 anchorMax = portrait ? _portraitAnchor : _anchorMax;
            Vector2 position = portrait ? _portraitPosition : _position;
            if (_rect.anchorMin != anchorMin || _rect.anchorMax != anchorMax)
            {
                _rect.anchorMin = anchorMin;
                _rect.anchorMax = anchorMax;
            }

            if (_rect.anchoredPosition != position)
            {
                _rect.anchoredPosition = position;
            }
        }
    }
}
