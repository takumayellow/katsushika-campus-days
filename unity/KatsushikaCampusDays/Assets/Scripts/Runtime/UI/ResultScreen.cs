using System;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 一日の終わりのリザルト。ランクと達成内訳を出し、「もう一日歩く」か「タイトルへ」を選ぶ。
    /// PauseMenu と同じくキーボードだけで完結させる。
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

        private ResultData _data;
        private Action _onContinue;
        private Action _onToTitle;
        private int _index;
        private float _openedAt;

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
            KCDInput.GameplayBlocked = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _root.SetActive(true);
            IsAnyOpen = true;

            AudioManager.Instance?.PlayUi("ui_open");
            AudioManager.Instance?.PlaySe("se_chime");
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
            KCDInput.GameplayBlocked = false;
            KCDInput.MarkModalClosed();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            HUD.Instance?.SetGameplayUIVisible(true);
        }

        private void OnDestroy()
        {
            // 開いたまま壊されても時間と入力は戻す。
            if (IsAnyOpen)
            {
                IsAnyOpen = false;
                Time.timeScale = 1f;
                KCDInput.GameplayBlocked = false;
            }
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime - _openedAt < InputGuardSeconds)
            {
                return;
            }

            int step = KCDInput.MenuVertical;
            if (step != 0)
            {
                _index = (_index + step + ChoiceKeys.Length) % ChoiceKeys.Length;
                AudioManager.Instance?.PlayUi("ui_move");
                Redraw();
            }

            if (KCDInput.SubmitPressed)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Action chosen = _index == 0 ? _onContinue : _onToTitle;
                Close();
                chosen?.Invoke();
            }
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

            if (_choices != null)
            {
                var builder = new System.Text.StringBuilder(128);
                for (int i = 0; i < ChoiceKeys.Length; i++)
                {
                    builder.Append(i == _index ? "<color=#FFD98A>▶ " : "   ");
                    builder.Append(L.Get(ChoiceKeys[i], ChoiceFallbacks[i]));
                    builder.Append(i == _index ? "</color>" : string.Empty);
                    builder.Append(i == ChoiceKeys.Length - 1 ? string.Empty : "      ");
                }

                _choices.text = builder.ToString();
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
