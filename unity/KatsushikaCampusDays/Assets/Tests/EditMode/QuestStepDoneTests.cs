using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 途中のステップを達成したときの一言（done）。地点を踏むだけのステップで、何が分かったかを伝える (#410)。
    /// </summary>
    public sealed class QuestStepDoneTests
    {
        private const string TwoStepJson =
            "{\"id\":\"q_two\",\"autoStart\":true,\"steps\":[" +
            "{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"a\",\"done\":\"見つからない\",\"done_en\":\"Not here\"}," +
            "{\"id\":\"s2\",\"type\":\"visit\",\"target\":\"b\"}]}";

        private static string QuestsDir => Path.Combine(Application.dataPath, "Data", "Quests");

        private static QuestData Parse(string json)
        {
            return QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        [Test]
        public void FromJson_ReadsDoneTextInBothLanguages()
        {
            QuestStep step = Parse(TwoStepJson).Steps[0];

            Assert.AreEqual("見つからない", step.DoneText);
            Assert.AreEqual("Not here", step.DoneTextEn);
            Assert.AreEqual(string.Empty, Parse(TwoStepJson).Steps[1].DoneText, "done が無ければ空");
        }

        [Test]
        public void StepCompleted_FiresForTheMiddleStepButNotTheLast()
        {
            var quests = new QuestSystem();
            quests.Load(new List<QuestData> { Parse(TwoStepJson) });
            var fired = new List<string>();
            quests.StepCompleted += (quest, step) => fired.Add(quest.Id + "/" + step.Id);

            quests.ReportVisit("a");
            CollectionAssert.AreEqual(new[] { "q_two/s1" }, fired);

            quests.ReportVisit("b");
            CollectionAssert.AreEqual(new[] { "q_two/s1" }, fired, "最後のステップは QuestCompleted が知らせる");
            Assert.IsTrue(quests.IsCompleted("q_two"));
        }

        /// <summary>
        /// 最後のステップの done は出ない（クエスト完了のトーストが出る）ので書かない。
        /// 英語表示で日本語が混ざらないよう、done には done_en を対にする。
        /// </summary>
        [Test]
        public void ShippedQuests_PutDoneOnlyOnMiddleStepsWithEnglish()
        {
            string[] files = Directory.GetFiles(QuestsDir, "*.json");
            Assert.IsNotEmpty(files);
            int withDone = 0;
            foreach (string file in files)
            {
                QuestData quest = Parse(File.ReadAllText(file));
                for (int i = 0; i < quest.Steps.Count; i++)
                {
                    QuestStep step = quest.Steps[i];
                    if (string.IsNullOrEmpty(step.DoneText))
                    {
                        Assert.IsEmpty(step.DoneTextEn, $"{quest.Id}/{step.Id}: done_en だけがある");
                        continue;
                    }

                    withDone++;
                    Assert.Less(i, quest.Steps.Count - 1, $"{quest.Id}/{step.Id}: 最後のステップの done は出ない");
                    Assert.IsNotEmpty(step.DoneTextEn, $"{quest.Id}/{step.Id}: done_en が無い");
                }
            }

            Assert.Greater(withDone, 0, "done を書いたステップがある");
        }
    }
}
