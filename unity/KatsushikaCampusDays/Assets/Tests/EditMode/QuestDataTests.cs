using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    public sealed class QuestDataTests
    {
        private static QuestData Parse(string json)
        {
            return QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        [Test]
        public void FromJson_ReadsFieldsAndSteps()
        {
            QuestData quest = Parse("{\"id\":\"q1\",\"title\":\"T\",\"order\":3,\"autoStart\":true," +
                                    "\"prerequisites\":[\"q0\"]," +
                                    "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"npc\",\"count\":2}," +
                                    "{\"type\":\"visit\",\"target\":\"poi\",\"minHour\":18}]}");

            Assert.AreEqual("q1", quest.Id);
            Assert.AreEqual("T", quest.Title);
            Assert.AreEqual(3, quest.Order);
            Assert.IsTrue(quest.AutoStart);
            CollectionAssert.AreEqual(new[] { "q0" }, quest.Prerequisites);
            Assert.AreEqual(2, quest.Steps.Count);
            Assert.AreEqual(QuestStepKind.Talk, quest.Steps[0].Kind);
            Assert.AreEqual(2, quest.Steps[0].Count);
            Assert.AreEqual("s2", quest.Steps[1].Id, "id 省略時は s と連番");
            Assert.AreEqual(QuestStepKind.Visit, quest.Steps[1].Kind);
            Assert.AreEqual(18f, quest.Steps[1].MinHour, 1e-6f);
        }

        [Test]
        public void FromJson_DefaultsWhenOptionalFieldsMissing()
        {
            QuestData quest = Parse("{\"id\":\"q2\",\"steps\":[{\"type\":\"unknown\",\"count\":0}]}");

            Assert.AreEqual("q2", quest.Title, "title 省略時は id");
            Assert.AreEqual(999, quest.Order);
            Assert.IsFalse(quest.AutoStart);
            Assert.AreEqual(QuestStepKind.Flag, quest.Steps[0].Kind, "未知の type は flag");
            Assert.AreEqual(1, quest.Steps[0].Count, "count は 1 未満に落ちない");
        }

        [Test]
        public void FromJson_RejectsMissingIdOrNull()
        {
            Assert.IsNull(QuestData.FromJson(null));
            Assert.IsNull(Parse("{\"title\":\"no id\"}"));
        }

        [Test]
        public void CurrentStep_AdvancesAndIsCompleteWhenAllDone()
        {
            QuestData quest = Parse("{\"id\":\"q3\",\"steps\":[{\"id\":\"a\"},{\"id\":\"b\"}]}");

            Assert.AreEqual("a", quest.CurrentStep.Id);
            Assert.IsFalse(quest.IsComplete);

            quest.Steps[0].Completed = true;
            Assert.AreEqual("b", quest.CurrentStep.Id);

            quest.Steps[1].Completed = true;
            Assert.IsNull(quest.CurrentStep);
            Assert.IsTrue(quest.IsComplete);
        }

        [Test]
        public void IsComplete_IsFalseForQuestWithoutSteps()
        {
            QuestData quest = Parse("{\"id\":\"q4\"}");

            Assert.IsNull(quest.CurrentStep);
            Assert.IsFalse(quest.IsComplete);
        }
    }
}
