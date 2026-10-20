using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// クレジット画面。地図データの帰属表示（ODbL）は必須なので、辞書が欠けても既定文で必ず出す。
    /// Esc / Enter で閉じる。指で遊んでいるときは、どこを叩いても閉じる (#187)。
    /// </summary>
    public sealed class CreditsView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TmpLinkTap _tap;

        /// <summary>案内の行を指のときの文面で書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _drawnForTouch;
        private bool _drawnForPortrait;

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body)
        {
            _root = root;
            _heading = heading;
            _body = body;
        }

        /// <summary>どこを叩いても閉じる受け口。SceneBuilder から差し込む。</summary>
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

        /// <summary>マウスのクリックは TmpLinkTap が捨て、ここでは指で遊んでいるときだけ受ける。</summary>
        private void OnTapped(string linkId)
        {
            if (IsOpen && InputMode.IsTouch)
            {
                Close();
            }
        }

        public void Open()
        {
            if (_root == null)
            {
                return;
            }

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
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (InputMode.IsTouch != _drawnForTouch || ScreenShape.IsPortrait != _drawnForPortrait)
            {
                Redraw();
            }

            if (KCDInput.MenuPressed || KCDInput.CancelPressed || KCDInput.SubmitPressed)
            {
                Close();
            }
        }

        /// <summary>
        /// 表示する本文。テストや他の画面からも同じ文面を使えるように公開する。
        /// 中身は docs/CREDITS.md の権利表と合わせる (#74)。本文の枠は 1084x536 px で、26 px の行は 31.2 px、
        /// 80% の行は 25 px の高さを取る。見出しの行だけ 26 px にして補足は 80% で書く。
        /// 行を足すときは、枠の高さに収まるか（PlayMode の CreditsLayoutTests が測る）と、
        /// 1 行 52 字（80% の全角）を超えて折り返さないかを見る。
        /// </summary>
        public static string BuildText(bool touch = false, bool portrait = false)
        {
            string back = touch ? L.Pick("タップで戻る", "Tap to go back") : L.Pick("Esc / Enter で戻る", "Esc / Enter: back");
            // 指か縦長の画面では枠ごと縮むので、戻る案内は 75% にせず本文と同じ大きさで出す (#187)。
            string hint = "<alpha=#99>" + back + "<alpha=#FF>";
            return BuildBody(portrait) + "\n\n" + (touch || portrait ? hint : "<size=75%>" + hint + "</size>");
        }

        /// <summary>案内の行（「Esc / Enter で戻る」）を除いた本文。エンディングの最後のページも使う (#176)。</summary>
        public static string BuildBody(bool portrait = false)
        {
            string note = portrait ? PortraitNoteSize : NoteSize;
            var builder = new System.Text.StringBuilder(2048);
            builder.Append(MapAttribution.Line());
            AppendNote(builder, note, L.Pick(
                "Open Database License の条件で利用しています。https://www.openstreetmap.org/copyright",
                "Used under the Open Database License. https://www.openstreetmap.org/copyright"));
            builder.Append("\n\n");

            builder.Append(L.Pick(
                "音楽: 東京理科大学校歌（作詞 佐治巌 / 作曲 大和憲史）",
                "Music: Tokyo University of Science school song (lyrics 佐治巌 / music 大和憲史)"));
            AppendNote(builder, note, L.Pick(
                "歌唱: 東北きりたん（NEUTRINO）。ピアノ伴奏: ヤマハの自動採譜（Piano Sheet Converter）を手直し。\n"
                + "図書館のピアノ: ショパン「夜想曲 作品9-2」冒頭（演奏 Peter Johnston / Musopen、CC0）\n"
                + "効果音・環境音・ジングルは numpy で合成したオリジナルです。",
                "Vocals: Tohoku Kiritan (NEUTRINO). Piano: Yamaha auto-transcription (Piano Sheet Converter), edited.\n"
                + "Library piano: Chopin, Nocturne Op. 9 No. 2 (opening), by Peter Johnston / Musopen (CC0).\n"
                + "Sound effects, ambience and jingles are original, synthesized with numpy."));
            builder.Append("\n\n");

            builder.Append(L.Get("ui.credits.setting", "舞台: 東京理科大学 葛飾キャンパス"));
            AppendNote(builder, note, L.Pick(
                "「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクターで、3D モデルは独自制作です。\n",
                "\"Botchan\" and \"Madonna-chan\" are official characters of the university; the 3D models are our own.\n")
                + L.Get("ui.credits.disclaimer", "本作は非公式・非営利のファン作品です。登場する学生・教授は架空の人物です。"));
            builder.Append("\n\n");

            builder.Append("<size=").Append(note).Append(">");
            builder.Append(L.Pick(
                "フォント: Noto Sans JP © 2014-2021 Adobe、Liberation Sans © 2010 Google, 2012 Red Hat\n"
                + "（SIL Open Font License 1.1。ライセンス本文は StreamingAssets/Licenses に同梱）\n"
                + "制作: Unity 6 (URP)。建物・キャラクターとそのテクスチャは Blender 4.5 で手続き生成しています。",
                "Fonts: Noto Sans JP © 2014-2021 Adobe, Liberation Sans © 2010 Google, 2012 Red Hat\n"
                + "(SIL Open Font License 1.1; full text in StreamingAssets/Licenses)\n"
                + "Made with Unity 6 (URP). Buildings, characters and their textures are generated in Blender 4.5."));
            builder.Append("</size>");
            return builder.ToString();
        }

        private const string NoteSize = "80%";

        /// <summary>縦長の画面は枠ごと縮んで 80% が 9.4 px（360 幅）まで小さくなるので、補足を少し大きくする (#187)。</summary>
        private const string PortraitNoteSize = "90%";

        /// <summary>見出しの行の下に、補足を size の大きさで足す。</summary>
        private static void AppendNote(System.Text.StringBuilder builder, string size, string note)
        {
            builder.Append("\n<size=").Append(size).Append(">");
            builder.Append(note);
            builder.Append("</size>");
        }

        private void Redraw()
        {
            if (_heading != null)
            {
                _heading.text = L.Get("ui.credits.heading", "クレジット");
            }

            _drawnForTouch = InputMode.IsTouch;
            _drawnForPortrait = ScreenShape.IsPortrait;
            if (_body != null)
            {
                _body.text = BuildText(_drawnForTouch, _drawnForPortrait);
            }
        }
    }
}
