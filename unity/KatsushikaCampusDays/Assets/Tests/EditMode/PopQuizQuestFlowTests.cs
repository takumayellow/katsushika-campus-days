using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// サブクエスト「出席は小テストで」（q_sq_pop_quiz）を実データで最初から最後までなぞる (#424)。
    /// はじめての講義のあと、101 の隣の席の学生（tateishi）が黒板を見てきてほしいと頼み、黒板（board_lecture_101）を読むと
    /// 練習問題に答える段へ進み、外れを選んでも答え直せ、正解で好感度が上がって終わる。
    /// 会話は DialogueSystem と同じ順（話しかけた時点で talk を報告し、閉じたときに依頼を受ける）で進める。
    /// </summary>
    public sealed class PopQuizQuestFlowTests
    {
        private const string LectureId = "q_lecture";
        private const string QuizId = "q_sq_pop_quiz";
        private const string Flag = "pop_quiz_answered";

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private QuestSystem _quests;
        private HashSet<string> _spent;

        [SetUp]
        public void SetUp()
        {
            NpcAffinity.Reset();
            _spent = new HashSet<string>();

            // はじめての講義の前提（体育館）はここでは見ない。そこまでの道は QuestAchievabilityTests が見る。
            Dictionary<string, object> lecture = Read("Quests", LectureId);
            lecture["prerequisites"] = new List<object>();
            _quests = new QuestSystem();
            _quests.Load(new[] { QuestData.FromJson(lecture), QuestData.FromJson(Read("Quests", QuizId)) }, true);
        }

        [TearDown]
        public void TearDown() => NpcAffinity.Reset();

        [Test]
        public void 講義のあと隣の席で頼まれ_黒板を読んで小テストに答えると終わる()
        {
            DialogueData seatmate = Dialogue("tateishi");
            DialogueData board = Dialogue("board_lecture_101");

            Assert.AreEqual("t_idle", Talk(seatmate).Id, "講義の前はいつもの話");
            Assert.IsFalse(_quests.IsActive(QuizId), "講義の前には頼まない");
            Assert.AreEqual("t_idle", Talk(board).Id, "受注前の黒板は読むだけ");

            CompleteLecture();
            Assert.IsTrue(_quests.IsCompleted(LectureId));

            Assert.AreEqual("t_start", Talk(seatmate).Id, "講義のあとに話しかけると黒板を見てきてほしいと頼む");
            Assert.IsTrue(_quests.IsActive(QuizId));
            Assert.AreEqual("s1", _quests.CurrentStepId(QuizId));
            Assert.AreEqual("t_wait", Talk(seatmate).Id, "黒板を読むまでは黒板の場所を言う");

            Assert.AreEqual("t_read", Talk(board).Id);
            Assert.AreEqual("s2", _quests.CurrentStepId(QuizId));
            Assert.AreEqual("t_idle", Talk(board).Id, "読んだあとの黒板は読むだけ");

            DialogueTopic quiz = Talk(seatmate);
            Assert.AreEqual("t_quiz", quiz.Id);
            foreach (DialogueChoice wrong in quiz.Choices.FindAll(c => c.Retry))
            {
                Choose(seatmate, quiz, wrong);
                Assert.AreEqual("s2", _quests.CurrentStepId(QuizId), wrong.Text + " では段が進まない");
                Assert.AreEqual(0, NpcAffinity.Get(seatmate.Id), wrong.Text + " では好感度が動かない");
                Assert.AreEqual("t_quiz", Talk(seatmate).Id, wrong.Text + " のあとも、もう一度答えられる");
            }

            Choose(seatmate, quiz, quiz.Choices.Find(c => c.SetsFlag == Flag));
            Assert.IsTrue(_quests.IsCompleted(QuizId), "正解で終わる");
            Assert.AreEqual(1, NpcAffinity.Get(seatmate.Id), "正解で好感度が 1 上がる");
            Assert.AreEqual("t_done", Talk(seatmate).Id);
            Assert.AreEqual("t_done", Talk(seatmate).Id, "終えたあとは同じ話をくり返す（依頼を二度出さない）");
        }

        [Test]
        public void 小テストは16通りだけが正解で_外れは答え直せる()
        {
            QuestData quest = QuestData.FromJson(Read("Quests", QuizId));
            QuestStep flag = quest.Steps.Find(s => s.Kind == QuestStepKind.Flag);
            Assert.IsNotNull(flag);
            Assert.AreEqual(Flag, flag.Target);

            DialogueTopic answer = Dialogue("tateishi").Topics.Find(t => t.RequiresStep == QuizId + "/" + flag.Id);
            Assert.IsNotNull(answer, "tateishi に " + flag.Id + " の段の話題が無い");
            List<DialogueChoice> setters = answer.Choices.FindAll(c => c.SetsFlag == flag.Target);
            Assert.AreEqual(1, setters.Count);
            Assert.IsFalse(setters[0].Retry);
            StringAssert.StartsWith("16", setters[0].Text, "4 席の空きと埋まりは 2 の 4 乗 = 16 通り");
            Assert.Greater(setters[0].Affinity, 0);
            Assert.IsTrue(answer.Choices.TrueForAll(c => c.SetsFlag == flag.Target || c.Retry), "外れの選択肢は答え直せる");
        }

        /// <summary>講義棟に入り、大教室の席に着き、教授と話して、はじめての講義を終える。</summary>
        private void CompleteLecture()
        {
            Assert.IsTrue(_quests.StartQuest(LectureId));
            _quests.ReportEnter("lecture");
            _quests.ReportVisit("poi_lecture_hall_seats", true);
            _quests.ReportTalk("lecture");
        }

        /// <summary>DialogueSystem.TalkTo と Finish の順: 選ぶ → talk を報告して選び直す → 閉じたら使い終えて依頼を受ける。</summary>
        private DialogueTopic Talk(DialogueData data)
        {
            DialogueTopic chosen = DialogueSystem.SelectTopic(data, _quests, _spent, 0, _ => false);
            DialogueTopic topic = DialogueSystem.ReportTalkAndSelect(data, _quests, _spent, chosen, 0, _ => false);
            Assert.IsNotNull(topic, data.Id + " に出す話題が無い");

            string key = data.Id + "/" + topic.Id;
            if (DialogueSystem.SpendsOnFinish(topic, key))
            {
                _spent.Add(key);
            }

            if (!string.IsNullOrEmpty(topic.StartsQuest))
            {
                _quests.StartQuest(topic.StartsQuest);
            }

            return topic;
        }

        /// <summary>DialogueSystem.Choose と同じ: 答えを覚え（retry は覚えない）、フラグを報告する。</summary>
        private void Choose(DialogueData data, DialogueTopic topic, DialogueChoice choice)
        {
            Assert.IsNotNull(choice);
            DialogueSystem.ApplyChoice(data.Id, data.Id + "/" + topic.Id, choice);
            if (!string.IsNullOrEmpty(choice.SetsFlag))
            {
                _quests.ReportFlag(choice.SetsFlag);
            }
        }

        private static Dictionary<string, object> Read(string folder, string id)
        {
            string path = Path.Combine(DataRoot, folder, id + ".json");
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        private static DialogueData Dialogue(string id) => DialogueData.FromJson(Read("Dialogue", id));
    }
}
