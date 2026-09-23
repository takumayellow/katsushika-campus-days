using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面右上の追跡表示。いま追っているクエストの題と、次にやることを 1 行で出す。
    /// 制限時間つきのステップ（体育館まで 60 秒など）は、残り秒・時間切れ・再挑戦の案内を 2 行目に出す。
    /// </summary>
    public sealed class QuestTrackerView : MonoBehaviour
    {
        /// <summary>時間切れのあと、追跡表示を失敗したクエストに留めておく秒数（トーストと並べて失敗をはっきり見せる）。</summary>
        private const float FailureHoldSeconds = 8f;

        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _stepLabel;
        [SerializeField] private GameObject _root;

        private QuestSystem _quests;

        /// <summary>いま表示しているクエストと残り秒。秒が変わったときだけ文字列を作り直す。</summary>
        private QuestData _shownQuest;
        private int _shownSecond = -1;

        /// <summary>時間切れになったばかりのクエストと、表示を留めておく期限（unscaledTime）。</summary>
        private QuestData _recentFailure;
        private float _recentFailureUntil;

        /// <summary>計時を始めたクエスト。会話を閉じてからスタートの案内を出す。</summary>
        private QuestData _pendingStart;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text title, TMP_Text step)
        {
            _root = root;
            _titleLabel = title;
            _stepLabel = step;
        }

        private void Start()
        {
            _quests = GameManager.Instance.Quests;
            if (_quests == null)
            {
                return;
            }

            _quests.Changed += Refresh;
            _quests.QuestCompleted += OnQuestCompleted;
            _quests.StepTimedOut += OnStepTimedOut;
            _quests.TimerStarted += OnTimerStarted;
            _quests.ChallengeRetryNeeded += OnChallengeRetryNeeded;
            L.LocaleChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= Refresh;
            if (_quests != null)
            {
                _quests.Changed -= Refresh;
                _quests.QuestCompleted -= OnQuestCompleted;
                _quests.StepTimedOut -= OnStepTimedOut;
                _quests.TimerStarted -= OnTimerStarted;
                _quests.ChallengeRetryNeeded -= OnChallengeRetryNeeded;
            }
        }

        private void Update()
        {
            if (_quests == null)
            {
                return;
            }

            // 再挑戦を依頼主に話しかけて始めたときは会話中なので、会話を閉じてから「スタート」を出す。
            if (_pendingStart != null && !KCDInput.GameplayBlocked)
            {
                ShowStartToast(_pendingStart);
                _pendingStart = null;
            }

            if (_recentFailure != null && Time.unscaledTime >= _recentFailureUntil)
            {
                _recentFailure = null;
                Refresh();
                return;
            }

            QuestData tracked = Tracked();
            if (tracked == null || _stepLabel == null)
            {
                return;
            }

            float remaining = _quests.RemainingSeconds(tracked);
            if (remaining < 0f)
            {
                return;
            }

            int second = Mathf.CeilToInt(remaining);
            if (tracked == _shownQuest && second == _shownSecond)
            {
                return;
            }

            _shownQuest = tracked;
            _shownSecond = second;
            _stepLabel.text = StepLine(tracked);
        }

        /// <summary>
        /// 表示するクエスト。時間切れの直後はしばらく失敗したクエストに留める
        /// （そうしないと、order の小さい図書館・食堂のクエストにすぐ切り替わって失敗が見えない）。
        /// いまは QuestSystem.TrackedQuest も再挑戦待ちの制限時間つきクエストを優先するので、
        /// これは制限時間つきクエストが複数あるときに、失敗したばかりの方を見せ続けるための控え。
        /// </summary>
        private QuestData Tracked()
        {
            if (_recentFailure != null && _quests.IsActive(_recentFailure.Id))
            {
                QuestStep step = _recentFailure.CurrentStep;
                if (step != null && step.Timer.HasFailed)
                {
                    return _recentFailure;
                }
            }

            return _quests.TrackedQuest;
        }

        private void OnTimerStarted(QuestData quest)
        {
            if (quest == null)
            {
                return;
            }

            if (_recentFailure == quest)
            {
                _recentFailure = null;
            }

            if (KCDInput.GameplayBlocked)
            {
                _pendingStart = quest;
            }
            else
            {
                ShowStartToast(quest);
            }
        }

        private static void ShowStartToast(QuestData quest)
        {
            QuestStep step = quest.CurrentStep;
            if (step == null || !step.Timer.IsRunning)
            {
                return;
            }

            HUD.Instance?.ShowToast(L.Format("ui.hud.challenge_start", Mathf.CeilToInt(step.Timer.Limit)));
        }

        private void OnStepTimedOut(QuestData quest)
        {
            if (quest == null)
            {
                return;
            }

            _recentFailure = quest;
            _recentFailureUntil = Time.unscaledTime + FailureHoldSeconds;
            if (_pendingStart == quest)
            {
                _pendingStart = null;
            }

            HUD hud = HUD.Instance;
            if (hud == null)
            {
                return;
            }

            // 黙って数え直すのではなく、失敗したことと再挑戦のしかたをはっきり出す。
            hud.ShowToast(L.Get("ui.hud.time_up", "時間切れ！ 挑戦失敗"));
            QuestStep step = quest.CurrentStep;
            if (step != null && !string.IsNullOrEmpty(step.Giver))
            {
                hud.ShowToast(L.Format("ui.hud.challenge_retry", GiverName(step)));
            }
        }

        /// <summary>
        /// 計時していないときに目的地へ着いた（時間切れのあとに体育館へ入った、など）。
        /// 達成にならない理由と再挑戦のしかたを出す。
        /// </summary>
        private void OnChallengeRetryNeeded(QuestData quest)
        {
            QuestStep step = quest?.CurrentStep;
            if (step == null || string.IsNullOrEmpty(step.Giver))
            {
                return;
            }

            HUD.Instance?.ShowToast(L.Format("ui.hud.challenge_retry", GiverName(step)));
        }

        private static string GiverName(QuestStep step)
        {
            return L.Get("ui.npc." + step.Giver, step.Giver);
        }

        /// <summary>ステップの表示。制限時間つきなら、状態に応じて 2 行目を足す。</summary>
        private static string StepLine(QuestData quest)
        {
            QuestStep step = quest?.CurrentStep;
            if (step == null)
            {
                return string.Empty;
            }

            string text = "・" + step.Text;
            if (step.Count > 1)
            {
                text += "　(" + step.Progress + "/" + step.Count + ")";
            }

            if (!step.IsTimed)
            {
                return text;
            }

            if (step.Timer.IsRunning)
            {
                return text + SubLine("#FFB3B3",
                    L.Format("ui.hud.time_remaining", Mathf.CeilToInt(step.Timer.Remaining)));
            }

            if (!step.AwaitingGiver)
            {
                return text;
            }

            // 時間切れなら失敗と再挑戦のしかた、ロード直後なら始め方を出す。
            return step.Timer.HasFailed
                ? text + SubLine("#FF8A8A", L.Format("ui.hud.challenge_failed", GiverName(step)))
                : text + SubLine("#FFE08A", L.Format("ui.hud.challenge_ready", GiverName(step)));
        }

        /// <summary>2 行目。追跡枠の高さに収まるよう少し小さくする。</summary>
        private static string SubLine(string color, string message)
        {
            return "\n<size=80%><color=" + color + ">" + message + "</color></size>";
        }

        private void OnQuestCompleted(QuestData quest)
        {
            HUD hud = HUD.Instance;
            if (hud == null || quest == null)
            {
                return;
            }

            if (_recentFailure == quest)
            {
                _recentFailure = null;
            }

            hud.ShowToast(L.Format("ui.hud.quest_completed", quest.Title));

            // 制限時間つきを間に合わせたなら、残り何秒で着いたかを出す。
            for (int i = 0; i < quest.Steps.Count; i++)
            {
                ChallengeTimer timer = quest.Steps[i].Timer;
                if (timer.State == ChallengeState.Cleared)
                {
                    hud.ShowToast(L.Format("ui.hud.challenge_cleared", timer.Remaining));
                    break;
                }
            }

            if (!string.IsNullOrEmpty(quest.RewardText))
            {
                hud.ShowToast(quest.RewardText);
            }

            if (quest.RewardType == QuestData.RewardAchievement && !string.IsNullOrEmpty(quest.RewardId))
            {
                hud.ShowToast(L.Format("ui.hud.achievement_unlocked",
                    L.Get("ach." + quest.RewardId + ".name", quest.RewardId)));
            }
        }

        private void Refresh()
        {
            QuestData tracked = _quests != null ? Tracked() : null;

            if (_root != null)
            {
                bool visible = tracked != null;
                if (_root.activeSelf != visible)
                {
                    _root.SetActive(visible);
                }
            }

            _shownQuest = tracked;
            _shownSecond = -1;

            if (tracked == null)
            {
                return;
            }

            if (_titleLabel != null)
            {
                _titleLabel.text = tracked.Title;
            }

            if (_stepLabel != null)
            {
                _stepLabel.text = StepLine(tracked);
            }
        }
    }
}
