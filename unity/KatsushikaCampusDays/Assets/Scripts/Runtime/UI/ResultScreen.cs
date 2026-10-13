using System;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 一日の終わりのリザルト。ランクと達成内訳を出し、「もう一日歩く」か「タイトルへ」を選ぶ。
    /// PauseMenu と同じくキーボードだけで完結させる。指で遊んでいるときは、選択肢を叩くとそれに決まる (#187)。
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        private static readonly string[] ChoiceKeys = { "ui.result.continue", "ui.result.to_title" };
        private static readonly string[] ChoiceFallbacks = { "もう一日歩く", "タイトルへ" };
        private const float InputGuardSeconds = 0.4f;

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _rankLabel;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TMP_Text _choices;
        [SerializeField] private TmpLinkTap _tap;

        private ResultData _data;
        private Action _onContinue;
        private Action _onToTitle;
        private int _index;
        private float _openedAt;

        /// <summary>選択肢を叩けるリンク付きで書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _choicesForTouch;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>どこかでリザルトが開いているか。他のメニューが上に重ならないよう見る。</summary>
        public static bool IsAnyOpen { get; private set; }

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text rank, TMP_Text body, TMP_Text choices)
        {
            _root = root;
            _heading = heading;
            _rankLabel = rank;
            _body = body;
            _choices = choices;
        }

        /// <summary>選択肢を叩く受け口。SceneBuilder から差し込む。</summary>
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

        /// <summary>集計結果を表示して時間を止める。</summary>
        public void Show(ResultData data, Action onContinue, Action onToTitle)
        {
            if (_root == null)
            {
                return;
            }

            _data = data;
            _onContinue = onContinue;
            _onToTitle = onToTitle;
            _index = 0;
            _openedAt = Time.unscaledTime;

            HUD.Instance?.SetGameplayUIVisible(false);
            // 自分の名前で封鎖する。閉じるときに、ほかの画面の封鎖まで外さない (#62)。
            KCDInput.Block(this);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _root.SetActive(true);
            IsAnyOpen = true;

            AudioManager.Instance?.PlayUi("ui_open");
            AudioManager.Instance?.PlayJingle("jingle_day_end");
            Redraw();
        }

        /// <summary>閉じて時間を戻す。</summary>
        public void Close()
        {
            if (_root == null)
            {
                return;
            }

            _root.SetActive(false);
            IsAnyOpen = false;
            Time.timeScale = 1f;
            KCDInput.Unblock(this);
            KCDInput.MarkModalClosed();
            InputMode.CaptureCursor();
            HUD.Instance?.SetGameplayUIVisible(true);
        }

        private void OnDestroy()
        {
            // 開いたまま壊されても時間と入力は戻す。
            if (IsAnyOpen)
            {
                IsAnyOpen = false;
                Time.timeScale = 1f;
            }

            KCDInput.Unblock(this);
        }

        private void Update()
        {
            if (IsOpen && InputMode.IsTouch != _choicesForTouch)
            {
                Redraw();
            }

            if (!IsOpen || Time.unscaledTime - _openedAt < InputGuardSeconds)
            {
                return;
            }

            int step = StepOf(KCDInput.MenuHorizontal, KCDInput.MenuVertical);
            if (step != 0)
            {
                _index = TitleMenu.MoveIndex(_index, step, ChoiceKeys.Length);
                AudioManager.Instance?.PlayUi("ui_move");
                Redraw();
            }

            if (KCDInput.SubmitPressed)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Choose(_index);
            }
        }

        /// <summary>
        /// 選択肢の移動量。選択肢は横に並ぶので左右で動かし、上下でも動かせるようにしておく。
        /// 左右と上下が同時に来たら左右を取る（タイトル画面と同じ）。
        /// </summary>
        public static int StepOf(int horizontal, int vertical)
        {
            return horizontal != 0 ? horizontal : vertical;
        }

        /// <summary>
        /// 選択肢の行。選んでいる方にだけ ▶ と色を付ける。
        /// links なら各選択肢を <c>&lt;link="choice:N"&gt;</c> で囲み、叩けるようにする。
        /// </summary>
        public static string ChoiceLine(int selected, string[] labels, bool links)
        {
            var builder = new System.Text.StringBuilder(128);
            for (int i = 0; i < labels.Length; i++)
            {
                if (links)
                {
                    builder.Append("<link=\"choice:").Append(i).Append("\">");
                }

                builder.Append(i == selected ? "<color=#FFD98A>▶ " : "   ");
                builder.Append(labels[i]);
                builder.Append(i == selected ? "</color>" : string.Empty);
                if (links)
                {
                    builder.Append("</link>");
                }

                builder.Append(i == labels.Length - 1 ? string.Empty : "      ");
            }

            return builder.ToString();
        }

        /// <summary>叩いたリンクの選択肢の番号。選択肢の外や形の違う id は -1。</summary>
        public static int TapIndex(string linkId)
        {
            return TmpLinkTap.TryParseChoice(linkId, out int index) && index < ChoiceKeys.Length ? index : -1;
        }

        private void OnTapped(string linkId)
        {
            // マウスのクリックでは決めない。キーと同じく、開いた直後の誤操作も拾わない。
            if (!IsOpen || !InputMode.IsTouch || Time.unscaledTime - _openedAt < InputGuardSeconds)
            {
                return;
            }

            int index = TapIndex(linkId);
            if (index < 0)
            {
                return;
            }

            AudioManager.Instance?.PlayUi("ui_confirm");
            Choose(index);
        }

        /// <summary>
        /// 選択肢を選んで閉じる（0 = もう一日歩く、1 = タイトルへ）。決定キーと同じ道で、テストとスモークからも呼ぶ。
        /// 開いていないときと範囲外の番号は何もしない。
        /// </summary>
        public void Choose(int index)
        {
            if (!IsOpen || index < 0 || index >= ChoiceKeys.Length)
            {
                return;
            }

            _index = index;
            Action chosen = index == 0 ? _onContinue : _onToTitle;
            Close();
            chosen?.Invoke();
        }

        private void Redraw()
        {
            if (_data == null)
            {
                return;
            }

            if (_heading != null)
            {
                _heading.text = L.Get("ui.result.heading", "今日の一日");
            }

            if (_rankLabel != null)
            {
                _rankLabel.text = "<size=140%>" + _data.Rank + "</size>\n<size=60%>" +
                                  L.Get("ui.result.rank", "ランク") + "</size>";
            }

            if (_body != null)
            {
                var builder = new System.Text.StringBuilder(512);
                builder.Append("<b>").Append(_data.Title).Append("</b>\n\n");
                builder.Append(L.Format("ui.result.completion", _data.Percent)).Append('\n');
                builder.Append(L.Format("ui.result.quests", _data.Quests, _data.QuestTotal)).Append('\n');
                builder.Append(L.Format("ui.result.collectibles", _data.Collectibles, _data.CollectibleTotal)).Append('\n');
                builder.Append(L.Format("ui.result.photos", _data.Photos, _data.PhotoTotal)).Append('\n');
                builder.Append(L.Format("ui.result.buildings", _data.Buildings, _data.BuildingTotal)).Append('\n');
                builder.Append(L.Format("ui.result.time_walked", FormatHours(_data.HoursWalked))).Append("\n\n");
                builder.Append("<size=85%>").Append(_data.Comment).Append("</size>");
                _body.text = builder.ToString();
            }

            _choicesForTouch = InputMode.IsTouch;
            if (_choices != null)
            {
                var labels = new string[ChoiceKeys.Length];
                for (int i = 0; i < ChoiceKeys.Length; i++)
                {
                    labels[i] = L.Get(ChoiceKeys[i], ChoiceFallbacks[i]);
                }

                _choices.text = ChoiceLine(_index, labels, _choicesForTouch);
            }
        }

        private static string FormatHours(float hours)
        {
            int whole = Mathf.FloorToInt(hours);
            int minutes = Mathf.Clamp(Mathf.RoundToInt((hours - whole) * 60f), 0, 59);
            return L.IsEnglish
                ? whole + "h " + minutes.ToString("00") + "m"
                : whole + "時間" + minutes.ToString("00") + "分";
        }
    }
}
