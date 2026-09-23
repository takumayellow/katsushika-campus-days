using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 話題の選び方 (#39)。クエストを頼む話題（startsQuest）は、そのクエストを受注中・完了済みなら出さない。
    /// 使い終えた話題はセーブに載らないので、これが無いとロード直後に同じ依頼をもう一度される。
    /// </summary>
    public sealed class DialogueTopicTests
    {
        private const string KanameJson =
            "{\"id\":\"kaname\",\"topics\":[" +
            "{\"id\":\"t_start\",\"once\":true,\"startsQuest\":\"q_lunch\",\"lines\":[\"a\"]}," +
            "{\"id\":\"t_active\",\"requiresActiveQuest\":\"q_lunch\",\"lines\":[\"b\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"c\"]}]}";

        private const string LunchJson =
            "{\"id\":\"q_lunch\",\"order\":3,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"kyoso\"}]}";

        private static DialogueData Kaname()
        {
            return DialogueData.FromJson(MiniJson.Deserialize(KanameJson) as Dictionary<string, object>);
        }

        private static QuestSystem Quests()
        {
            var quests = new QuestSystem();
            quests.Load(new[] { QuestData.FromJson(MiniJson.Deserialize(LunchJson) as Dictionary<string, object>) });
            return quests;
        }

        [Test]
        public void NotStartedYet_OffersTheRequest()
        {
            DialogueTopic topic = DialogueSystem.SelectTopic(Kaname(), Quests(), new HashSet<string>());
            Assert.AreEqual("t_start", topic.Id);
        }

        [Test]
        public void ActiveQuest_SkipsTheRequestEvenIfNotSpent()
        {
            QuestSystem quests = Quests();
            quests.StartQuest("q_lunch");

            // ロード直後を想定: 使い終えた話題の記録は空。
            DialogueTopic topic = DialogueSystem.SelectTopic(Kaname(), quests, new HashSet<string>());
            Assert.AreEqual("t_active", topic.Id);
        }

        [Test]
        public void CompletedQuest_SkipsTheRequestEvenIfNotSpent()
        {
            QuestSystem quests = Quests();
            quests.StartQuest("q_lunch");
            quests.ReportEnter("kyoso");
            Assert.IsTrue(quests.IsCompleted("q_lunch"));

            DialogueTopic topic = DialogueSystem.SelectTopic(Kaname(), quests, new HashSet<string>());
            Assert.AreEqual("t_idle", topic.Id, "依頼をもう一度されない");
        }

        [Test]
        public void SpentOnceTopic_IsStillSkipped()
        {
            var spent = new HashSet<string> { "kaname/t_start" };
            DialogueTopic topic = DialogueSystem.SelectTopic(Kaname(), Quests(), spent);
            Assert.AreEqual("t_idle", topic.Id);
        }
    }
}
