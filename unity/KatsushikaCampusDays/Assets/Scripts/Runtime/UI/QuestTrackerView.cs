using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面右上の追跡表示。いま追っているクエストの題と、次にやることを 1 行で出す。
    /// </summary>
    public sealed class QuestTrackerView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _stepLabel;
        [SerializeField] private GameObject _root;

        private QuestSystem _quests;

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
            }
        }

        private void Update()
        {
            QuestData tracked = _quests?.TrackedQuest;
            if (tracked == null || _stepLabel == null)
            {
                return;
            }

            float remaining = _quests.RemainingSeconds(tracked);
            if (remaining >= 0f)
            {
                _stepLabel.text = StepText(tracked) + "　<color=#FFB3B3>" +
                                  L.Format("ui.hud.time_remaining", Mathf.CeilToInt(remaining)) + "</color>";
            }
        }

        private static void OnStepTimedOut(QuestData quest)
        {
            HUD.Instance?.ShowToast(L.Get("ui.hud.time_up", "時間切れ。もう一度はじめから走ろう"));
        }

        private static string StepText(QuestData quest)
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

            return text;
        }

        private void OnQuestCompleted(QuestData quest)
        {
            HUD hud = HUD.Instance;
            if (hud == null || quest == null)
            {
                return;
            }

            hud.ShowToast(L.Format("ui.hud.quest_completed", quest.Title));
            if (!string.IsNullOrEmpty(quest.RewardText))
            {
                hud.ShowToast(quest.RewardText);
            }
        }

        private void Refresh()
        {
            QuestData tracked = _quests?.TrackedQuest;

            if (_root != null)
            {
                bool visible = tracked != null;
                if (_root.activeSelf != visible)
                {
                    _root.SetActive(visible);
                }
            }

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
                _stepLabel.text = StepText(tracked);
            }
        }
    }
}
