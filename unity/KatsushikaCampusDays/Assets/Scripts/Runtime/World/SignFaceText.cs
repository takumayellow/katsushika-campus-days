using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 入口の立て看板の板の表と裏に貼った建物の名前。
    /// カメラを向かず、大きさも変えない。板と一緒に置いたまま読む。
    /// 文字は辞書の ui.building.&lt;id&gt; から引き、言語を切り替えるたびに引き直す（#181）。
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class SignFaceText : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private string _fallback;

        private TextMeshPro _text;

        /// <summary>辞書の鍵（ui.building.&lt;id&gt;）。</summary>
        public string Key => _key;

        /// <summary>辞書の鍵（ui.building.&lt;id&gt;）と、鍵が無いときに出す名前。</summary>
        public void Bind(string key, string fallback)
        {
            _key = key;
            _fallback = fallback;
            Refresh();
        }

        /// <summary>今の言語での表示名。鍵が空なら fallback をそのまま出す。</summary>
        public static string Resolve(string key, string fallback)
        {
            return string.IsNullOrEmpty(key) ? fallback : L.Get(key, fallback);
        }

        private void OnEnable()
        {
            L.LocaleChanged += OnLocaleChanged;
            OnLocaleChanged();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            if (!string.IsNullOrEmpty(_key))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (_text == null)
            {
                _text = GetComponent<TextMeshPro>();
            }

            _text.text = Resolve(_key, _fallback);
        }
    }
}
