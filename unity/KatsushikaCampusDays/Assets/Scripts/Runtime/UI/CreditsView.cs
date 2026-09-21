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

        /// <summary>表示する本文。テストや他の画面からも同じ文面を使えるように公開する。</summary>
        public static string BuildText()
        {
            var builder = new System.Text.StringBuilder(1024);
            builder.Append(L.Get("ui.credits.map", "地図データ © OpenStreetMap contributors (ODbL)"));
            builder.Append("\n");
            builder.Append(L.Pick(
                "Open Database License の条件で利用しています。https://www.openstreetmap.org/copyright",
                "Used under the Open Database License. https://www.openstreetmap.org/copyright"));
            builder.Append("\n\n");
            builder.Append(L.Get("ui.credits.setting", "舞台: 東京理科大学 葛飾キャンパス"));
            builder.Append("\n");
            builder.Append(L.Pick(
                "「坊っちゃん」「マドンナちゃん」は東京理科大学の公式キャラクターです。本作は非公式・非営利のファンメイドで、モデルは独自に制作しています。",
                "\"Botchan\" and \"Madonna-chan\" are official characters of Tokyo University of Science. This is an unofficial, non-commercial fan work with original models."));
            builder.Append("\n");
            builder.Append(L.Get("ui.credits.disclaimer", "このゲームは非公式のファン作品です。登場する人物・アイテムは架空のものです。"));
            builder.Append("\n\n");
            builder.Append(L.Pick(
                "制作: Unity 6 (URP) と Blender 4.5 の手続き生成。音楽・効果音は数値合成のオリジナルです。",
                "Made with Unity 6 (URP) and procedural generation in Blender 4.5. Music and sound effects are original, synthesized numerically."));
            builder.Append("\n");
            builder.Append(L.Pick(
                "フォント: Noto Sans JP（SIL Open Font License 1.1）",
                "Font: Noto Sans JP (SIL Open Font License 1.1)"));
            builder.Append("\n\n<size=75%><alpha=#99>");
            builder.Append(L.Pick("Esc / Enter で戻る", "Esc / Enter: back"));
            builder.Append("</alpha></size>");
            return builder.ToString();
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
