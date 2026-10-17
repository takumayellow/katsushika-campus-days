using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトル画面の右下に出す地図データの帰属表示 (#20)。
    /// タイトルと主人公選びのどちらの画面でも見えるように、TitleCanvas の直下に置く。言語を切り替えたら描き直す。
    /// 設定とクレジットの枠が開いて文字にかかる間は、文字を隠す (#187)。枠は半透明なので、横持ちのスマホで枠が
    /// Canvas の高さいっぱいに広がると、帰属表示が枠の本文に透けて重なる。クレジットの枠は本文の 1 行目に同じ帰属表示を出す。
    /// </summary>
    public sealed class MapAttributionLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private RectTransform[] _avoid = new RectTransform[0];

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>いま表示している文面。</summary>
        public string Text => _label != null ? _label.text : string.Empty;

        /// <summary>文字を出しているか。開いた枠が文字にかかる間は false。</summary>
        public bool IsShown => _label != null && _label.enabled;

        /// <summary>TitleStage から差し込む。文面は TitleStage が既定文で入れ、実行時に OnEnable で今の言語に合わせる。</summary>
        public void Bind(TMP_Text label)
        {
            _label = label;
        }

        /// <summary>開いて文字にかかる間は文字を隠す枠。TitleStage から差し込む。</summary>
        public void Avoid(params RectTransform[] panels)
        {
            _avoid = panels;
        }

        private void OnEnable()
        {
            L.LocaleChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= Refresh;
        }

        private void LateUpdate()
        {
            Sync();
        }

        /// <summary>今の言語の文面で描き直す。</summary>
        public void Refresh()
        {
            if (_label != null)
            {
                _label.text = MapAttribution.Line();
            }
        }

        /// <summary>開いている枠が文字にかかっていれば文字を隠し、かからなくなったら戻す。</summary>
        public void Sync()
        {
            if (_label == null)
            {
                return;
            }

            bool shown = !Covered(_label.rectTransform, _avoid);
            if (_label.enabled != shown)
            {
                _label.enabled = shown;
            }
        }

        /// <summary>
        /// panels のうち表示されているものが、label の四角にかかるか。四角は画面に置いた後の位置と大きさで比べるので、
        /// 枠の大きさや縮める倍率（<see cref="FitInCanvas"/>）が画面ごとに変わっても、そのときの枠で決まる。
        /// </summary>
        public static bool Covered(RectTransform label, IEnumerable<RectTransform> panels)
        {
            if (label == null || panels == null)
            {
                return false;
            }

            Rect area = WorldRect(label);
            foreach (RectTransform panel in panels)
            {
                if (panel != null && panel.gameObject.activeInHierarchy && WorldRect(panel).Overlaps(area))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>rect の四隅を囲む四角（ワールド座標の x と y）。</summary>
        private static Rect WorldRect(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            Vector2 min = Corners[0];
            Vector2 max = Corners[0];
            for (int i = 1; i < Corners.Length; i++)
            {
                min = Vector2.Min(min, Corners[i]);
                max = Vector2.Max(max, Corners[i]);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
