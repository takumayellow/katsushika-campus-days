using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトル画面の右下に出す地図データの帰属表示 (#20)。
    /// タイトルと主人公選びのどちらの画面でも見えるように、TitleCanvas の直下に置く。言語を切り替えたら描き直す。
    /// </summary>
    public sealed class MapAttributionLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;

        /// <summary>いま表示している文面。</summary>
        public string Text => _label != null ? _label.text : string.Empty;

        /// <summary>TitleStage から差し込む。文面は TitleStage が既定文で入れ、実行時に OnEnable で今の言語に合わせる。</summary>
        public void Bind(TMP_Text label)
        {
            _label = label;
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

        /// <summary>今の言語の文面で描き直す。</summary>
        public void Refresh()
        {
            if (_label != null)
            {
                _label.text = MapAttribution.Line();
            }
        }
    }
}
