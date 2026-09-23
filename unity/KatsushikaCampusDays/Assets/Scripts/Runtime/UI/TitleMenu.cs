using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>タイトルで Enter が決めるもの (#53)。</summary>
    public enum TitleChoice
    {
        /// <summary>セーブを読んでキャンパスへ（キャラ選択は通らない）。</summary>
        Continue,

        /// <summary>キャラ選択へ。決定すると進行を初期値に戻す（<see cref="GameManager.BeginNewGame"/>）。</summary>
        NewGame
    }

    /// <summary>
    /// タイトル画面。読めるセーブがあれば「つづきから / はじめから」を ←→ で選んで Enter、
    /// 無ければ Enter でキャラクター選択（はじめから）へ移る。F9 はセーブがあれば「つづきから」。
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        private const string SelectedColor = "#FFD98A";

        [SerializeField] private GameObject _titleRoot;
        [SerializeField] private GameObject _selectRoot;
        [SerializeField] private TMP_Text _promptLabel;
        [SerializeField] private CharacterSelect _characterSelect;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _subtitleLabel;
        [SerializeField] private float _blinkSpeed = 2.2f;
        [SerializeField] private SettingsView _settings;
        [SerializeField] private CreditsView _credits;

        private bool _moved;
        private float _promptAlphaBase = 1f;

        /// <summary>
        /// 読めるセーブがあるか。タイトルに入ったときに一度だけ中身まで読んで決める。
        /// ファイルがあるだけ（<see cref="SaveSystem.HasSave"/>）で「つづきから」を出すと、
        /// 壊れたセーブでは選んでも何も起きない (#61)。
        /// </summary>
        private bool _hasSave;

        /// <summary>選んでいる選択肢の番号（<see cref="ChoiceAt"/> で意味に直す）。</summary>
        private int _choice;

        /// <summary>タイトルが出たフレーム。ポーズの「タイトルへ戻る」で押した決定を拾い直さない（#6）。</summary>
        private int _shownFrame = KCDInput.NoFrame;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject titleRoot, GameObject selectRoot, TMP_Text prompt, CharacterSelect select)
        {
            _titleRoot = titleRoot;
            _selectRoot = selectRoot;
            _promptLabel = prompt;
            _characterSelect = select;
        }

        /// <summary>設定とクレジット。SceneBuilder から差し込む。</summary>
        public void BindExtras(SettingsView settings, CreditsView credits)
        {
            _settings = settings;
            _credits = credits;
        }

        /// <summary>題名と副題。言語切替で書き換えるために持つ。</summary>
        public void BindHeadings(TMP_Text title, TMP_Text subtitle)
        {
            _titleLabel = title;
            _subtitleLabel = subtitle;
        }

        private bool IsModalOpen =>
            (_settings != null && _settings.IsOpen) || (_credits != null && _credits.IsOpen);

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
        }

        private void RefreshPrompt()
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = L.Get("ui.title.name", "葛飾キャンパスデイズ");
            }

            if (_subtitleLabel != null)
            {
                _subtitleLabel.text = L.Get("ui.title.subtitle", "東京理科大学 葛飾キャンパス 探索記");
            }

            if (_promptLabel == null)
            {
                return;
            }

            string extras = L.Pick(
                "\nC クレジット　/　O 設定",
                "\nC: Credits   /   O: Settings");

            if (!_hasSave)
            {
                _promptLabel.text = L.Get("ui.title.press_start", "Enter ではじめる") +
                                    "<size=70%>" + extras + "</size>";
                return;
            }

            // 選択肢そのものを出し、Enter は選んでいる方を実行する (#53)。
            string line = ChoiceLine(
                _choice,
                L.Get("ui.title.continue", "つづきから"),
                L.Get("ui.title.new_game", "はじめから"));
            string hint = L.Get("ui.title.continue_hint", "← → でえらぶ　Enter で決定　（F9 でもつづきから）");
            _promptLabel.text = line + "<size=70%>\n" + hint + extras + "</size>";
        }

        /// <summary>選択肢の数。セーブが無ければ「はじめから」の 1 つだけ。</summary>
        public static int ChoiceCount(bool hasSave)
        {
            return hasSave ? 2 : 1;
        }

        /// <summary>
        /// index 番目の選択肢。セーブがあれば 0 がつづきから・1 がはじめから。
        /// セーブが無い、または範囲外なら、はじめから。
        /// </summary>
        public static TitleChoice ChoiceAt(bool hasSave, int index)
        {
            return hasSave && index == 0 ? TitleChoice.Continue : TitleChoice.NewGame;
        }

        /// <summary>選択を step だけ動かす。端から先は反対の端へ回る。</summary>
        public static int MoveIndex(int index, int step, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int moved = (index + step) % count;
            return moved < 0 ? moved + count : moved;
        }

        /// <summary>
        /// セーブがあるときの選択肢の行。並びは <see cref="ChoiceAt"/> と同じで、選んでいる方にだけ ▶ と色を付ける。
        /// </summary>
        public static string ChoiceLine(int selected, string continueLabel, string newGameLabel)
        {
            var builder = new System.Text.StringBuilder(96);
            int count = ChoiceCount(true);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append("　　");
                }

                string label = ChoiceAt(true, i) == TitleChoice.Continue ? continueLabel : newGameLabel;
                if (i == selected)
                {
                    builder.Append("<color=").Append(SelectedColor).Append(">▶ ").Append(label).Append("</color>");
                }
                else
                {
                    builder.Append("　").Append(label);
                }
            }

            return builder.ToString();
        }

        private void Start()
        {
            _shownFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            KCDInput.GameplayBlocked = false;

            // 既定は「つづきから」。遊んだ人がタイトルに戻ってきて Enter を押したとき、進行を消さない側。
            _hasSave = SaveSystem.Read() != null;
            _choice = 0;

            if (_selectRoot != null)
            {
                _selectRoot.SetActive(false);
            }

            if (_promptLabel != null)
            {
                _promptAlphaBase = _promptLabel.color.a;
                RefreshPrompt();
            }
        }

        private void Update()
        {
            BlinkPrompt();

            if (_moved || IsModalOpen || KCDInput.ModalClosedThisFrame ||
                KCDInput.IgnoresInput(_shownFrame))
            {
                return;
            }

            if (KCDInput.CreditsPressed && _credits != null)
            {
                _credits.Open();
                return;
            }

            if (KCDInput.SettingsPressed && _settings != null)
            {
                _settings.Open(null);
                return;
            }

            int step = KCDInput.MenuHorizontal;
            if (step == 0)
            {
                step = KCDInput.MenuVertical;
            }

            int count = ChoiceCount(_hasSave);
            if (step != 0 && count > 1)
            {
                _choice = MoveIndex(_choice, step, count);
                AudioManager.Instance?.PlayUi("ui_move");
                RefreshPrompt();
            }

            if (KCDInput.SubmitPressed)
            {
                Choose(ChoiceAt(_hasSave, _choice));
            }
            else if (KCDInput.QuickLoadPressed && _hasSave)
            {
                Choose(TitleChoice.Continue);
            }
        }

        private void Choose(TitleChoice choice)
        {
            if (choice == TitleChoice.NewGame)
            {
                EnterCharacterSelect();
                return;
            }

            // キャラ・時刻・日数・クエスト・一日の記録はここで、位置と屋内はキャンパスの最初のフレームで戻す (#61)。
            if (SaveSystem.PrepareContinue())
            {
                _moved = true;
                AudioManager.Instance?.PlayUi("ui_confirm");
                GameManager.Instance.EnterCampus();
                return;
            }

            // タイトルに入ったあとで読めなくなった（消された・書き換えられた）。黙ってはじめからに進めず、
            // 選択肢を消して「Enter ではじめる」に戻す。はじめからに進むかは、もう一度の Enter で本人が決める。
            _hasSave = false;
            _choice = 0;
            AudioManager.Instance?.PlayUi("ui_close");
            RefreshPrompt();
        }

        private void BlinkPrompt()
        {
            if (_promptLabel == null || _moved)
            {
                return;
            }

            // 選択肢を出しているときは点滅させない。選んでいる方が読みにくくなる。
            float pulse = _hasSave ? 1f : 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * _blinkSpeed);
            Color color = _promptLabel.color;
            _promptLabel.color = new Color(color.r, color.g, color.b, _promptAlphaBase * pulse);
        }

        private void EnterCharacterSelect()
        {
            _moved = true;
            AudioManager.Instance?.PlayUi("ui_confirm");

            if (_titleRoot != null)
            {
                _titleRoot.SetActive(false);
            }

            if (_selectRoot != null)
            {
                _selectRoot.SetActive(true);
            }

            if (_characterSelect != null)
            {
                _characterSelect.SetActiveSelection(true);
            }
        }
    }
}
