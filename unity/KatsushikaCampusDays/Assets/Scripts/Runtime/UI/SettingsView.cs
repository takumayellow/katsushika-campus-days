using System;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 設定画面。上下で行を選び、左右で値を変える。BGM / 効果音 / 環境音の音量と言語。
    /// ポーズメニューとタイトルの両方から開く。Esc か「閉じる」で戻る。
    /// </summary>
    public sealed class SettingsView : MonoBehaviour
    {
        private const int RowBgm = 0;
        private const int RowSe = 1;
        private const int RowAmbient = 2;
        private const int RowLanguage = 3;
        private const int RowClose = 4;
        private const int RowCount = 5;

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;

        private int _index;
        private Action _onClosed;

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body)
        {
            _root = root;
            _heading = heading;
            _body = body;
        }

        public void Open(Action onClosed)
        {
            if (_root == null)
            {
                return;
            }

            _onClosed = onClosed;
            _index = 0;
            _root.SetActive(true);
            Redraw();
            AudioManager.Instance?.PlayUi("ui_open");
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _root.SetActive(false);
            KCDInput.MarkModalClosed();
            AudioManager.Instance?.PlayUi("ui_close");

            Action callback = _onClosed;
            _onClosed = null;
            callback?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (KCDInput.MenuPressed || KCDInput.CancelPressed)
            {
                Close();
                return;
            }

            int vertical = KCDInput.MenuVertical;
            if (vertical != 0)
            {
                _index = (_index + vertical + RowCount) % RowCount;
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            int horizontal = KCDInput.MenuHorizontal;
            if (horizontal != 0 && Adjust(horizontal))
            {
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (KCDInput.SubmitPressed)
            {
                if (_index == RowClose)
                {
                    AudioManager.Instance?.PlayUi("ui_confirm");
                    Close();
                }
                else if (Adjust(1))
                {
                    Redraw();
                    AudioManager.Instance?.PlayUi("ui_move");
                }
            }
        }

        private bool Adjust(int direction)
        {
            AudioManager audio = AudioManager.Instance;
            switch (_index)
            {
                case RowBgm:
                    if (audio == null) { return false; }
                    audio.BgmVolume = Step(audio.BgmVolume, direction);
                    return true;
                case RowSe:
                    if (audio == null) { return false; }
                    audio.SeVolume = Step(audio.SeVolume, direction);
                    return true;
                case RowAmbient:
                    if (audio == null) { return false; }
                    audio.AmbientVolume = Step(audio.AmbientVolume, direction);
                    return true;
                case RowLanguage:
                    L.Toggle();
                    return true;
                default:
                    return false;
            }
        }

        private static float Step(float value, int direction)
        {
            return Mathf.Clamp01(Mathf.Round((value + direction * 0.1f) * 10f) / 10f);
        }

        private static string Bar(float value)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * 10f), 0, 10);
            return new string('■', filled) + new string('□', 10 - filled) + "  " + (filled * 10) + "%";
        }

        private void Redraw()
        {
            if (_heading != null)
            {
                _heading.text = L.Get("ui.settings.heading", "設定");
            }

            if (_body == null)
            {
                return;
            }

            AudioManager audio = AudioManager.Instance;
            string bgm = audio != null ? Bar(audio.BgmVolume) : "-";
            string se = audio != null ? Bar(audio.SeVolume) : "-";
            string ambient = audio != null ? Bar(audio.AmbientVolume) : "-";
            string language = "◀ " + L.Get("ui.settings.language." + L.Locale, L.Locale) + " ▶";

            var builder = new System.Text.StringBuilder(512);
            AppendRow(builder, RowBgm, L.Get("ui.settings.bgm_volume", "音楽の音量"), bgm);
            AppendRow(builder, RowSe, L.Get("ui.settings.se_volume", "効果音の音量"), se);
            AppendRow(builder, RowAmbient, L.Pick("環境音の音量", "Ambient Volume"), ambient);
            AppendRow(builder, RowLanguage, L.Get("ui.settings.language", "言語"), language);
            builder.Append('\n');
            AppendRow(builder, RowClose, L.Pick("閉じる", "Close"), string.Empty);
            builder.Append("\n<size=70%><alpha=#99>");
            builder.Append(L.Pick("↑↓ で選択　←→ で変更　Esc で戻る", "Up/Down: select   Left/Right: change   Esc: back"));
            builder.Append("<alpha=#FF></size>");
            _body.text = builder.ToString();
        }

        private void AppendRow(System.Text.StringBuilder builder, int row, string label, string value)
        {
            bool selected = row == _index;
            builder.Append(selected ? "<color=#FFD98A>▶ " : "   ");
            builder.Append(label);
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append("　　");
                builder.Append(value);
            }

            builder.Append(selected ? "</color>\n" : "\n");
        }
    }
}
