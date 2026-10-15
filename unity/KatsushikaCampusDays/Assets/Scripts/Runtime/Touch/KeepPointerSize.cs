using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 指で遊んでいる間も、キーボード・マウスのときと同じ大きさで描く (#187)。
    /// <see cref="TouchCanvasScale"/> が Canvas ごと 1.5 倍にするので、その分だけ縮めて打ち消す。
    /// 右上のミニマップと時計に使う。1.5 倍にすると横持ちのスマホで画面の高さの半分を占め、
    /// 右下の「走る」「ジャンプ」のボタンに時計が重なる。縦長の画面でも同じ倍率で描く
    /// （幅 800 の Canvas で 300 のまま描くと、左上のクエストの枠に届く）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class KeepPointerSize : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _position;

        /// <summary>入力の種類に合う倍率。指のときは <see cref="TouchCanvasScale"/> の拡大の逆数。</summary>
        public static float Factor(bool touch)
        {
            return touch ? TouchCanvasScale.TouchReference.y / TouchCanvasScale.PointerReference.y : 1f;
        }

        /// <summary>入力の種類と画面の向きに合う倍率。縦長の画面では指のときと同じ。</summary>
        public static float Factor(bool touch, bool portrait)
        {
            return Factor(touch || portrait);
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
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

        /// <summary>大きさと一緒に、角からの距離も縮める。でないと縮めた分だけ画面の内側へずれる。</summary>
        private void Sync()
        {
            float factor = Factor(InputMode.IsTouch, ScreenShape.IsPortrait);
            if (!Mathf.Approximately(_rect.localScale.x, factor))
            {
                _rect.localScale = new Vector3(factor, factor, 1f);
                _rect.anchoredPosition = _position * factor;
            }
        }
    }
}
