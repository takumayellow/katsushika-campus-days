using System;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 設定画面。上下で行を選び、左右で値を変える。BGM / 効果音 / 環境音の音量と言語と画質 (#70)、
    /// カメラの感度・上下の反転・フルスクリーン (#6)。
    /// ポーズメニューとタイトルの両方から開く。Esc か「閉じる」で戻る。
    /// 指で遊んでいるときは、◀ ▶ を叩くとその行の値が変わり、「閉じる」を叩くと戻る (#187)。
    /// 縦持ちのスマホでは名前と値を 2 行に分けて書く。幅 800 の Canvas では 1 行に入らず、
    /// 枠ごと縮めると文字が 11 px、行の間隔が 18 px ほどになって、隣の行を叩いてしまう。
    /// </summary>
    public sealed class SettingsView : MonoBehaviour
    {
        private const int RowBgm = 0;
        private const int RowSe = 1;
        private const int RowAmbient = 2;
        private const int RowLanguage = 3;
        private const int RowQuality = 4;
        private const int RowSensitivity = 5;
        private const int RowInvert = 6;
        private const int RowFullscreen = 7;
        private const int RowClose = 8;
        private const int RowCount = 9;

        /// <summary>本文を縮めるときの下限。</summary>
        private const float MinBodyFontSize = 22f;

        /// <summary>
        /// 名前と値を 2 行に分けて書くときの行の高さ。1 項目が 2 行なので、叩ける高さは 3.3em
        /// （文字の大きさ 30 で 99、縦持ちで約 48 px）になり、<see cref="TmpLinkTap.TapRowLineHeight"/> の 3em を下回らない。
        /// </summary>
        public const string StackedLineHeight = "<line-height=1.65em>";

        /// <summary>2 行に分けたときの値の行の字下げ。名前の頭の ▶ と空白の幅にそろえる。</summary>
        private const string StackedValueIndent = "      ";

        /// <summary>設定の 1 項目。名前・値・値を ◀ ▶ で挟むか。</summary>
        public readonly struct Entry
        {
            public readonly string Label;
            public readonly string Value;
            public readonly bool Arrows;

            public Entry(string label, string value, bool arrows)
            {
                Label = label;
                Value = value;
                Arrows = arrows;
            }
        }

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TmpLinkTap _tap;

        private int _index;
        private Action _onClosed;

        /// <summary>◀ ▶ と行を叩けるリンク付きで書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _drawnForTouch;

        /// <summary>
        /// 書いたときのフルスクリーンか。Web 版はブラウザが要求を受けてから切り替わるので、変わったら書き直す。
        /// Esc でブラウザ側から抜けたときも同じ。
        /// </summary>
        private bool _drawnFullscreen;

        /// <summary>名前と値を 2 行に分けて書いたか。画面の向きが変わったら書き直す。</summary>
        private bool _drawnStacked;

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

        private void Awake()
        {
            // 行が 9 つになり、英語や指の ◀ ▶ では 30pt の本文が枠の下に届く。はみ出す分だけ縮める (#6)。
            if (_body != null)
            {
                _body.fontSizeMax = _body.fontSize;
                _body.fontSizeMin = Mathf.Min(MinBodyFontSize, _body.fontSize);
                _body.enableAutoSizing = true;
            }
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

            if (InputMode.IsTouch != _drawnForTouch || Screen.fullScreen != _drawnFullscreen
                || ScreenShape.IsPortrait != _drawnStacked)
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
                case RowSensitivity:
                    CameraPrefs.SensitivityLevel = CameraPrefs.StepLevel(CameraPrefs.SensitivityLevel, direction);
                    return true;
                case RowInvert:
                    CameraPrefs.InvertY = !CameraPrefs.InvertY;
                    return true;
                case RowFullscreen:
                    // ブラウザはキーやタップの処理の中でしか全画面を許さない。Ctrl+F と同じ切り替えを使う。
                    ScreenModeHotkey.Toggle();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 音量を 1 段（10%）動かす。既定の音量は段の途中（0.55 など）にあるので、
        /// 表示している段（<see cref="Level"/>）から動かし、1 回で 2 段動いたり動かなかったりしないようにする。
        /// </summary>
        public static float Step(float value, int direction)
        {
            return Mathf.Clamp(Level(value) + direction, 0, 10) / 10f;
        }

        /// <summary>音量を表示する段（0〜10）。</summary>
        public static int Level(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt(value * 10f), 0, 10);
        }

        /// <summary>音量を棒と % で書く。</summary>
        public static string Bar(float value)
        {
            int filled = Level(value);
            return new string('■', filled) + new string('□', 10 - filled) + "  " + (filled * 10) + "%";
        }

        /// <summary>カメラの感度の段（1〜10）を棒と数で書く。</summary>
        public static string SensitivityBar(int level)
        {
            int filled = CameraPrefs.ClampLevel(level);
            return new string('■', filled) + new string('□', CameraPrefs.MaxLevel - filled) + "  " + filled;
        }

        private static string OnOff(bool on)
        {
            return on ? L.Pick("オン", "On") : L.Pick("オフ", "Off");
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
            _drawnFullscreen = Screen.fullScreen;
            _drawnStacked = ScreenShape.IsPortrait;
            bool links = _drawnForTouch;
            AudioManager audio = AudioManager.Instance;
            string bgm = audio != null ? Bar(audio.BgmVolume) : "-";
            string se = audio != null ? Bar(audio.SeVolume) : "-";
            string ambient = audio != null ? Bar(audio.AmbientVolume) : "-";
            string language = L.Get("ui.settings.language." + L.Locale, L.Locale);
            string quality = QualityLabel(QualityManager.Current);

            // 音量の ◀ ▶ は指のときだけ出す。キーでは ←→ で変えるので、棒だけで足りる。
            bool volumeArrows = links && audio != null;
            // 行の番号は Adjust と叩いた行の受け口と同じ Row の定数で引く。
            var entries = new Entry[RowClose];
            entries[RowBgm] = new Entry(L.Get("ui.settings.bgm_volume", "音楽の音量"), bgm, volumeArrows);
            entries[RowSe] = new Entry(L.Get("ui.settings.se_volume", "効果音の音量"), se, volumeArrows);
            entries[RowAmbient] = new Entry(L.Pick("環境音の音量", "Ambient Volume"), ambient, volumeArrows);
            entries[RowLanguage] = new Entry(L.Get("ui.settings.language", "言語"), language, true);
            entries[RowQuality] = new Entry(L.Get("ui.settings.quality", "画質"), quality, true);
            entries[RowSensitivity] = new Entry(L.Get("ui.settings.look_sensitivity", "カメラの感度"),
                SensitivityBar(CameraPrefs.SensitivityLevel), links);
            entries[RowInvert] = new Entry(L.Get("ui.settings.invert_y", "カメラ上下を反転"), OnOff(CameraPrefs.InvertY), true);
            entries[RowFullscreen] = new Entry(L.Get("ui.settings.fullscreen", "フルスクリーン"), OnOff(_drawnFullscreen), true);
            _body.text = Compose(entries, _index, links, _drawnStacked);
        }

        /// <summary>
        /// 本文を組む。entries は「閉じる」より前の行を順に並べたもの。selected は選んでいる行。
        /// stacked なら名前と値を 2 行に分け、行の間隔を指の幅まで広げる。
        /// </summary>
        public static string Compose(Entry[] entries, int selected, bool links, bool stacked)
        {
            var builder = new System.Text.StringBuilder(1024);
            if (stacked)
            {
                builder.Append(StackedLineHeight);
            }

            for (int row = 0; row < entries.Length; row++)
            {
                Entry entry = entries[row];
                AppendRow(builder, row, row == selected, entry.Label, entry.Value, entry.Arrows, links, stacked);
            }

            builder.Append('\n');
            AppendRow(builder, RowClose, RowClose == selected, L.Pick("閉じる", "Close"), string.Empty, false, links,
                stacked);
            builder.Append("\n<size=70%><alpha=#99>");
            builder.Append(links
                ? L.Pick("◀ ▶ を叩いて変更　「閉じる」で戻る", "Tap ◀ ▶ to change   Close: back")
                : L.Pick("↑↓ で選択　←→ で変更　Esc で戻る", "Up/Down: select   Left/Right: change   Esc: back"));
            builder.Append("<alpha=#FF></size>");
            return builder.ToString();
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

        /// <summary>
        /// 1 行を足す。選んでいる行にだけ ▶ と色を付ける。arrows なら値を ◀ ▶ で挟む。
        /// links なら名前を <c>&lt;link="choice:N"&gt;</c>、◀ を "dec:N"、▶ を "inc:N" で囲み、叩けるようにする。
        /// stacked なら値を次の行に字下げして書く。
        /// </summary>
        public static void AppendRow(System.Text.StringBuilder builder, int row, bool selected, string label,
            string value, bool arrows, bool links, bool stacked = false)
        {
            builder.Append(selected ? "<color=#FFD98A>" : string.Empty);
            AppendLinked(builder, links ? "choice:" : null, row, (selected ? "▶ " : "   ") + label);
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append(stacked ? "\n" + StackedValueIndent : "　　");
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
