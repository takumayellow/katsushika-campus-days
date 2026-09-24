using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// クレジット画面。地図データの帰属表示（ODbL）は必須なので、辞書が欠けても既定文で必ず出す。
    /// Esc / Enter で閉じる。
    /// </summary>
    public sealed class CreditsView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body)
        {
            _root = root;
            _heading = heading;
            _body = body;
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

            if (KCDInput.MenuPressed || KCDInput.CancelPressed || KCDInput.SubmitPressed)
            {
                Close();
            }
        }

        /// <summary>
        /// 表示する本文。テストや他の画面からも同じ文面を使えるように公開する。
        /// 中身は docs/CREDITS.md の権利表と合わせる (#74)。本文の枠は 1084x536 px で、26 px の行は 31.2 px、
        /// 80% の行は 25 px の高さを取る。見出しの行だけ 26 px にして補足は 80% で書き、今の 17 行で約 490 px。
        /// 行を足すときは、1 行 52 字（80% の全角）を超えて折り返さないかも見る。
        /// </summary>
        public static string BuildText()
        {
            var builder = new System.Text.StringBuilder(2048);
            builder.Append(MapAttribution.Line());
            AppendNote(builder, L.Pick(
                "Open Database License の条件で利用しています。https://www.openstreetmap.org/copyright",
                "Used under the Open Database License. https://www.openstreetmap.org/copyright"));
            builder.Append("\n\n");

            builder.Append(L.Pick(
                "音楽: 東京理科大学校歌（作詞 佐治巌 / 作曲 大和憲史）",
                "Music: Tokyo University of Science school song (lyrics 佐治巌 / music 大和憲史)"));
            AppendNote(builder, L.Pick(
                "歌唱: 東北きりたん（NEUTRINO）。ピアノ伴奏: ヤマハの自動採譜（Piano Sheet Converter）を手直し。\n"
                + "効果音・環境音・ジングルは numpy で合成したオリジナルです。",
                "Vocals: Tohoku Kiritan (NEUTRINO). Piano: Yamaha auto-transcription (Piano Sheet Converter), edited.\n"
                + "Sound effects, ambience and jingles are original, synthesized with numpy."));
            builder.Append("\n\n");

            builder.Append(L.Get("ui.credits.setting", "舞台: 東京理科大学 葛飾キャンパス"));
            AppendNote(builder, L.Pick(
                "「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクターで、3D モデルは独自制作です。\n"
                + "スターバックス、ファミリーマートなどの店名は各社の商標です。\n",
                "\"Botchan\" and \"Madonna-chan\" are official characters of the university; the 3D models are our own.\n"
                + "Store names such as Starbucks and FamilyMart are trademarks of their owners.\n")
                + L.Get("ui.credits.disclaimer", "本作は非公式・非営利のファン作品です。登場する学生・教授は架空の人物です。"));
            builder.Append("\n\n");

            builder.Append("<size=80%>");
            builder.Append(L.Pick(
                "フォント: Noto Sans JP © 2014-2021 Adobe、Liberation Sans © 2010 Google, 2012 Red Hat\n"
                + "（SIL Open Font License 1.1。ライセンス本文は StreamingAssets/Licenses に同梱）\n"
                + "制作: Unity 6 (URP)。建物・キャラクターとそのテクスチャは Blender 4.5 で手続き生成しています。",
                "Fonts: Noto Sans JP © 2014-2021 Adobe, Liberation Sans © 2010 Google, 2012 Red Hat\n"
                + "(SIL Open Font License 1.1; full text in StreamingAssets/Licenses)\n"
                + "Made with Unity 6 (URP). Buildings, characters and their textures are generated in Blender 4.5."));
            builder.Append("</size>\n\n<size=75%><alpha=#99>");
            builder.Append(L.Pick("Esc / Enter で戻る", "Esc / Enter: back"));
            builder.Append("<alpha=#FF></size>");
            return builder.ToString();
        }

        /// <summary>見出しの行の下に 80% の大きさで補足を足す。</summary>
        private static void AppendNote(System.Text.StringBuilder builder, string note)
        {
            builder.Append("\n<size=80%>");
            builder.Append(note);
            builder.Append("</size>");
        }

        private void Redraw()
        {
            if (_heading != null)
            {
                _heading.text = L.Get("ui.credits.heading", "クレジット");
            }

            if (_body != null)
            {
                _body.text = BuildText();
            }
        }
    }
}
