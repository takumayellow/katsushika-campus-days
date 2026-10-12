using System;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 設定画面。上下で行を選び、左右で値を変える。BGM / 効果音 / 環境音の音量と言語と画質 (#70)。
    /// ポーズメニューとタイトルの両方から開く。Esc か「閉じる」で戻る。
    /// 指で遊んでいるときは、◀ ▶ を叩くとその行の値が変わり、「閉じる」を叩くと戻る (#187)。
    /// </summary>
    public sealed class SettingsView : MonoBehaviour
    {
        private const int RowBgm = 0;
        private const int RowSe = 1;
        private const int RowAmbient = 2;
        private const int RowLanguage = 3;
        private const int RowQuality = 4;
        private const int RowClose = 5;
        private const int RowCount = 6;

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TmpLinkTap _tap;

        private int _index;
        private Action _onClosed;

        /// <summary>◀ ▶ と行を叩けるリンク付きで書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _drawnForTouch;

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>選んでいる行。</summary>
        public int SelectedRow => _index;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body)
        {
            _root = root;
            _heading = heading;
            _body = body;
        }

        /// <summary>行と ◀ ▶ を叩く受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void OnEnable()
        {
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        private void OnDisable()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }
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

            if (InputMode.IsTouch != _drawnForTouch)
            {
                Redraw();
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

        /// <summary>
        /// 叩いたリンクを読む。"choice:N" は行そのもの（direction 0）、"dec:N" は ◀（-1）、"inc:N" は ▶（+1）。
        /// 形が違うか行の外なら false。
        /// </summary>
        public static bool TryParseTap(string linkId, out int row, out int direction)
        {
            direction = 0;
            if (TmpLinkTap.TryParse(linkId, "dec:", out row))
            {
                direction = -1;
            }
            else if (TmpLinkTap.TryParse(linkId, "inc:", out row))
            {
                direction = 1;
            }
            else if (!TmpLinkTap.TryParseChoice(linkId, out row))
            {
                return false;
            }

            return row < RowCount;
        }

        /// <summary>マウスのクリックは TmpLinkTap が捨て、ここでは指で遊んでいるときだけ受ける。</summary>
        private void OnTapped(string linkId)
        {
            if (InputMode.IsTouch && TryParseTap(linkId, out int row, out int direction))
            {
                ApplyTap(row, direction);
            }
        }

        /// <summary>
        /// 叩いた行を選ぶ。direction が 0 でなければ（◀ ▶）その行の値を変え、「閉じる」の行そのものなら閉じる。
        /// 開いていなければ何もしない。
        /// </summary>
        public void ApplyTap(int row, int direction)
        {
            if (!IsOpen || row < 0 || row >= RowCount)
            {
                return;
            }

            _index = row;
            if (direction == 0 && row == RowClose)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Close();
                return;
            }

            if (direction != 0)
            {
                Adjust(direction);
            }

            Redraw();
            AudioManager.Instance?.PlayUi("ui_move");
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
                case RowQuality:
                    QualityManager.SetTier(QualityTiers.Step(QualityManager.Current, direction));
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

            _drawnForTouch = InputMode.IsTouch;
            bool links = _drawnForTouch;
            AudioManager audio = AudioManager.Instance;
            string bgm = audio != null ? Bar(audio.BgmVolume) : "-";
            string se = audio != null ? Bar(audio.SeVolume) : "-";
            string ambient = audio != null ? Bar(audio.AmbientVolume) : "-";
            string language = L.Get("ui.settings.language." + L.Locale, L.Locale);
            string quality = QualityLabel(QualityManager.Current);

            // 音量の ◀ ▶ は指のときだけ出す。キーでは ←→ で変えるので、棒だけで足りる。
            bool volumeArrows = links && audio != null;
            var builder = new System.Text.StringBuilder(768);
            AppendRow(builder, RowBgm, L.Get("ui.settings.bgm_volume", "音楽の音量"), bgm, volumeArrows, links);
            AppendRow(builder, RowSe, L.Get("ui.settings.se_volume", "効果音の音量"), se, volumeArrows, links);
            AppendRow(builder, RowAmbient, L.Pick("環境音の音量", "Ambient Volume"), ambient, volumeArrows, links);
            AppendRow(builder, RowLanguage, L.Get("ui.settings.language", "言語"), language, true, links);
            AppendRow(builder, RowQuality, L.Get("ui.settings.quality", "画質"), quality, true, links);
            builder.Append('\n');
            AppendRow(builder, RowClose, L.Pick("閉じる", "Close"), string.Empty, false, links);
            builder.Append("\n<size=70%><alpha=#99>");
            builder.Append(links
                ? L.Pick("◀ ▶ を叩いて変更　「閉じる」で戻る", "Tap ◀ ▶ to change   Close: back")
                : L.Pick("↑↓ で選択　←→ で変更　Esc で戻る", "Up/Down: select   Left/Right: change   Esc: back"));
            builder.Append("<alpha=#FF></size>");
            _body.text = builder.ToString();
        }

        private static string QualityLabel(QualityTier tier)
        {
            string fallback;
            switch (tier)
            {
                case QualityTier.Low:
                    fallback = L.Pick("低", "Low");
                    break;
                case QualityTier.High:
                    fallback = L.Pick("高", "High");
                    break;
                default:
                    fallback = L.Pick("中", "Medium");
                    break;
            }

            return L.Get(QualityTiers.LabelKey(tier), fallback);
        }

        private void AppendRow(System.Text.StringBuilder builder, int row, string label, string value, bool arrows,
            bool links)
        {
            AppendRow(builder, row, row == _index, label, value, arrows, links);
        }

        /// <summary>
        /// 1 行を足す。選んでいる行にだけ ▶ と色を付ける。arrows なら値を ◀ ▶ で挟む。
        /// links なら名前を <c>&lt;link="choice:N"&gt;</c>、◀ を "dec:N"、▶ を "inc:N" で囲み、叩けるようにする。
        /// </summary>
        public static void AppendRow(System.Text.StringBuilder builder, int row, bool selected, string label,
            string value, bool arrows, bool links)
        {
            builder.Append(selected ? "<color=#FFD98A>" : string.Empty);
            AppendLinked(builder, links ? "choice:" : null, row, (selected ? "▶ " : "   ") + label);
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append("　　");
                if (arrows)
                {
                    AppendLinked(builder, links ? "dec:" : null, row, "◀");
                    builder.Append(' ');
                }

                builder.Append(value);
                if (arrows)
                {
                    builder.Append(' ');
                    AppendLinked(builder, links ? "inc:" : null, row, "▶");
                }
            }

            builder.Append(selected ? "</color>\n" : "\n");
        }

        /// <summary>prefix があれば text を <c>&lt;link="prefixN"&gt;</c> で囲んで足す。無ければそのまま足す。</summary>
        private static void AppendLinked(System.Text.StringBuilder builder, string prefix, int row, string text)
        {
            if (prefix == null)
            {
                builder.Append(text);
                return;
            }

            builder.Append("<link=\"").Append(prefix).Append(row).Append("\">").Append(text).Append("</link>");
        }
    }
}
