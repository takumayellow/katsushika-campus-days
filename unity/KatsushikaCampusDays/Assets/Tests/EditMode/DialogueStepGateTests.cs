using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// クエストの段で出す話題（requiresStep）と、答え直せる選択肢（retry）(#389)。
    /// 7F の白板の問いは、黒板を 5 枚読み終えた段でだけ出し、外れを選んでも次にまた聞く。
    /// 先に答えさせると、段が来る前に立てたフラグは数えられず、話題も答え済みで消えてしまう。
    /// </summary>
    public sealed class DialogueStepGateTests
    {
        private const string QuestJson =
            "{\"id\":\"q_bytes\",\"steps\":[" +
            "{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"board_a\"}," +
            "{\"id\":\"s2\",\"type\":\"flag\",\"target\":\"f_reply\"}," +
            "{\"id\":\"s3\",\"type\":\"talk\",\"target\":\"teacher\"}]}";

        private const string BoardJson =
            "{\"id\":\"board_b\",\"topics\":[" +
            "{\"id\":\"t_answer\",\"requiresStep\":\"q_bytes/s2\",\"lines\":[\"続きを書く？\"],\"choices\":[" +
            "{\"text\":\"WORLD\",\"setsFlag\":\"f_reply\",\"lines\":[\"書いた\"]}," +
            "{\"text\":\"GOODBYE\",\"retry\":true,\"lines\":[\"違う気がする\"]}]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"白板だ\"]}]}";

        [SetUp]
        public void SetUp() => NpcAffinity.Reset();

        [TearDown]
        public void TearDown() => NpcAffinity.Reset();

        private static DialogueData Board()
        {
            return DialogueData.FromJson(MiniJson.Deserialize(BoardJson) as Dictionary<string, object>);
        }

        private static QuestSystem Quests()
        {
            var quests = new QuestSystem();
            quests.Load(new[] { QuestData.FromJson(MiniJson.Deserialize(QuestJson) as Dictionary<string, object>) }, true);
            return quests;
        }

        private static DialogueTopic Select(QuestSystem quests)
        {
            return DialogueSystem.SelectTopic(Board(), quests, new HashSet<string>(), 0, _ => false);
        }

        [Test]
        public void ParsesRequiresStepAndRetry()
        {
            DialogueTopic topic = Board().Topics[0];

            Assert.AreEqual("q_bytes/s2", topic.RequiresStep);
            Assert.IsFalse(topic.Choices[0].Retry);
            Assert.IsTrue(topic.Choices[1].Retry);
        }

        [Test]
        public void StepTopicWaitsUntilThatStepIsCurrent()
        {
            QuestSystem quests = Quests();
            Assert.AreEqual("t_idle", Select(quests).Id, "受注前は出さない");

            quests.StartQuest("q_bytes");
            Assert.AreEqual("t_idle", Select(quests).Id, "前の段のうちは出さない");

            quests.ReportTalk("board_a");
            Assert.AreEqual("t_answer", Select(quests).Id, "その段になったら受け皿より先に出す");

            quests.ReportFlag("f_reply");
            Assert.AreEqual("t_idle", Select(quests).Id, "段が進んだら出さない");
        }

        [Test]
        public void CurrentStepIdIsEmptyUnlessTheQuestIsActive()
        {
            QuestSystem quests = Quests();
            Assert.AreEqual(string.Empty, quests.CurrentStepId("q_bytes"));
            Assert.AreEqual(string.Empty, quests.CurrentStepId("q_missing"));

            quests.StartQuest("q_bytes");
            Assert.AreEqual("s1", quests.CurrentStepId("q_bytes"));
        }

        [Test]
        public void MalformedRequiresStepNeverMatches()
        {
            QuestSystem quests = Quests();
            quests.StartQuest("q_bytes");

            Assert.IsFalse(DialogueSystem.StepConditionMet("q_bytes", quests));
            Assert.IsFalse(DialogueSystem.StepConditionMet("q_bytes/", quests));
            Assert.IsFalse(DialogueSystem.StepConditionMet("/s1", quests));
            Assert.IsTrue(DialogueSystem.StepConditionMet("q_bytes/s1", quests));
            Assert.IsFalse(DialogueSystem.StepConditionMet("q_bytes/s1", null));
        }

        [Test]
        public void RetryChoiceDoesNotMarkTheTopicAnswered()
        {
            DialogueTopic topic = Board().Topics[0];

            DialogueSystem.ApplyChoice("board_b", "board_b/t_answer", topic.Choices[1]);
            Assert.IsFalse(NpcAffinity.HasAnswered("board_b/t_answer"), "外れを選んでも次にまた聞く");

            DialogueSystem.ApplyChoice("board_b", "board_b/t_answer", topic.Choices[0]);
            Assert.IsTrue(NpcAffinity.HasAnswered("board_b/t_answer"));
        }

        /// <summary>
        /// 実データの q_sq_hello_world: 黒板を読む段には、その段でだけ出る話題が板にある。
        /// 白板の段のフラグは、その段の話題の答え直せない選択肢だけが立てる。
        /// </summary>
        [Test]
        public void HelloWorldBoardsAnswerOnlyTheirOwnStep()
        {
            string data = Path.Combine(Application.dataPath, "Data");
            QuestData quest = QuestData.FromJson(Read(Path.Combine(data, "Quests", "q_sq_hello_world.json")));

            foreach (QuestStep step in quest.Steps)
            {
                string gate = quest.Id + "/" + step.Id;
                if (step.Kind == QuestStepKind.Talk && step.Target.StartsWith("board_"))
                {
                    DialogueData board = Dialogue(data, step.Target);
                    Assert.IsTrue(board.Topics.Exists(t => t.RequiresStep == gate), step.Target + " に " + gate + " の話題が無い");
                }
                else if (step.Kind == QuestStepKind.Flag)
                {
                    DialogueData board = Dialogue(data, "board_7F");
                    DialogueTopic answer = board.Topics.Find(t => t.RequiresStep == gate);
                    Assert.IsNotNull(answer, "board_7F に " + gate + " の話題が無い");
                    List<DialogueChoice> setters = answer.Choices.FindAll(c => c.SetsFlag == step.Target);
                    Assert.AreEqual(1, setters.Count);
                    Assert.IsFalse(setters[0].Retry);
                    Assert.IsTrue(answer.Choices.TrueForAll(c => c.SetsFlag == step.Target || c.Retry), "外れの選択肢は答え直せる");
                }
            }
        }

        private static Dictionary<string, object> Read(string path)
        {
            return MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
        }

        private static DialogueData Dialogue(string data, string id)
        {
            return DialogueData.FromJson(Read(Path.Combine(data, "Dialogue", id + ".json")));
        }
    }
}
