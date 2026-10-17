using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 時計の時間帯と、ミニマップの「N」・建物と階の名前の文字の大きさ (#187)。
    /// 指か縦長の画面では <see cref="KeepPointerSize"/> が時計とミニマップを 0.667 倍にするので、
    /// 24 や 22 のままだと縦 360 幅で 6.6〜7.8 px まで小さくなる。枠は縮めたまま、文字だけを向きで大きくする。
    /// 縦は 34（360 幅で約 10.2 px）、横の指は 28（740x360 で約 10.0 px、844x390 で約 11.2 px）。
    /// キーボード・マウスの横長は縮めないので、組み立てたときの大きさのまま。
    /// </summary>
    public static class HudTextScale
    {
        public const float PortraitSize = 34f;
        public const float TouchLandscapeSize = 28f;

        /// <summary>この組み合わせで <see cref="KeepPointerSize"/> が縮めるか。</summary>
        public static bool Shrunk(bool touch, bool portrait)
        {
            return touch || portrait;
        }

        /// <summary>組み立てたときの大きさ baseSize の文字を、今の向きで出す大きさ。</summary>
        public static float Size(float baseSize, bool touch, bool portrait)
        {
            if (!Shrunk(touch, portrait))
            {
                return baseSize;
            }

            return Mathf.Max(baseSize, portrait ? PortraitSize : TouchLandscapeSize);
        }

        /// <summary>向きと入力の組み合わせを 1 つの値にする。変わったときだけ当て直すため。</summary>
        public static int Key(bool touch, bool portrait)
        {
            return (touch ? 1 : 0) | (portrait ? 2 : 0);
        }

        /// <summary>
        /// 1 行の文字を、枠の高さいっぱいに置く。TextMeshPro は枠からはみ出しても描くが、
        /// 上下の余白を 0 にして、行の高さが枠に入る大きさだけを選ぶ。
        /// </summary>
        public static void FitRow(TMP_Text label, float size, float rowHeight)
        {
            if (label == null)
            {
                return;
            }

            label.fontSize = size;
            RectTransform rect = label.rectTransform;
            rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
            RectTransform parent = rect.parent as RectTransform;
            if (parent != null)
            {
                parent.sizeDelta = new Vector2(parent.sizeDelta.x, rowHeight);
            }
        }
    }
}
