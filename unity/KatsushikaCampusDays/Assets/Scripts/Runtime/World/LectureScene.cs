using UnityEngine;

namespace KCD
{
    /// <summary>
    /// はじめての講義（q_lecture）で 101 の席に着いたら、教授の短い講義（lecture / t_lecture_talk）を流す (#410)。
    /// 席のゾーンに触れた瞬間に s2 が終わるだけだと、何が起きたのか分からないまま「教卓の教授に質問する」へ進んでいた。
    /// 会話中に席に着いたときは、その会話が終わってから流す。GameManager が実行時に付ける。
    /// 席に着いたあと講義の前にセーブして読み直したときは流さない（教授に話しかければ t_lecture_done で同じ話を聞ける）。
    /// </summary>
    public sealed class LectureScene : MonoBehaviour
    {
        public const string QuestId = "q_lecture";

        /// <summary>席に着く段。この段が終わったら講義を流す。</summary>
        public const string SeatStepId = "s2";

        public const string DialogueId = "lecture";

        public const string TopicId = "t_lecture_talk";

        /// <summary>講義のあとの段。講義はこの段のあいだだけ流す（先に教授へ質問したら流さない）。</summary>
        public const string AfterStepId = "s3";

        private QuestSystem _quests;
        private bool _pending;

        /// <summary>この段の完了で講義を流すか（テストから呼ぶ）。</summary>
        public static bool Triggers(string questId, string stepId)
        {
            return questId == QuestId && stepId == SeatStepId;
        }

        private void OnDisable()
        {
            Subscribe(null);
            _pending = false;
        }

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            Subscribe(manager != null ? manager.Quests : null);
            if (!_pending)
            {
                return;
            }

            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null || dialogue.IsPlaying)
            {
                return;
            }

            // 会話が終わるまでに教授へ質問を済ませていたら、講義はもう流さない。
            // PlayTopic は once を見ないので、二度流さないのはこの段の確認による（s3 は教授に話すと終わる）。
            // 話題が無い（データが古い）ときも待ち続けない。
            if (_quests != null && _quests.CurrentStepId(QuestId) == AfterStepId)
            {
                dialogue.PlayTopic(DialogueId, TopicId);
            }

            _pending = false;
        }

        private void Subscribe(QuestSystem quests)
        {
            if (_quests == quests)
            {
                return;
            }

            if (_quests != null)
            {
                _quests.StepCompleted -= OnStepCompleted;
            }

            _quests = quests;
            if (_quests != null)
            {
                _quests.StepCompleted += OnStepCompleted;
            }
        }

        private void OnStepCompleted(QuestData quest, QuestStep step)
        {
            if (quest != null && step != null && Triggers(quest.Id, step.Id))
            {
                _pending = true;
            }
        }
    }
}
