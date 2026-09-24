using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 時刻の条件（minHour）があるステップの場所に、早く着いたときの案内 (#66)。
    /// 以前は QuestSystem が黙って読み飛ばしていたので、夕方の水盤（q_sunset, 18 時）や
    /// 夜の正門（q_sq_night_walk, 19 時）に昼間に行っても何も起きず、「来たのに進まない」だけに見えた。
    /// いまは入った瞬間に 1 回だけ <c>StepTooEarly</c> を出し、追跡表示が「◯時ごろにまた来よう」を出す。
    /// 留まっているあいだの報告し直し（VisitZone.OnTriggerStay）では出さない。
    /// </summary>
    public sealed class QuestTooEarlyTests
    {
        private const string SunsetJson =
            "{\"id\":\"q_sunset\",\"order\":7,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"library_pond\",\"minHour\":18}]}";

        private const string MallJson =
            "{\"id\":\"q_mall\",\"order\":2,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"campus_mall\"}]}";

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static QuestSystem Make(float hour, params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(QuestData.FromJson(MiniJson.Deserialize(jsons[i]) as Dictionary<string, object>));
            }

            var quests = new QuestSystem();
            quests.Clock = () => hour;
            quests.Load(list);
            return quests;
        }

        [Test]
        public void ArrivingBeforeMinHour_TellsOnceAndDoesNotProgress()
        {
            QuestSystem quests = Make(17f, SunsetJson);
            var told = new List<QuestStep>();
            quests.StepTooEarly += (quest, step) => told.Add(step);

            quests.ReportVisit("library_pond", true);

            Assert.AreEqual(1, told.Count, "早く着いたのに何も知らせていない");
            Assert.AreEqual(18f, told[0].MinHour, 1e-4f, "知らせたステップが時刻条件のものでない");
            QuestStep step = quests.Find("q_sunset").Steps[0];
            Assert.AreEqual(0, step.Progress, "時刻前なのに進んだ");
            Assert.IsFalse(step.Completed);
            Assert.IsTrue(quests.IsActive("q_sunset"));
        }

        [Test]
        public void StayingBeforeMinHour_DoesNotRepeatTheMessage()
        {
            QuestSystem quests = Make(17f, SunsetJson);
            int told = 0;
            quests.StepTooEarly += (quest, step) => told++;

            quests.ReportVisit("library_pond", true);
            for (int i = 0; i < 20; i++)
            {
                // VisitZone.OnTriggerStay は 0.25 秒おきに報告し直す。そのたびに出すとトーストが積もる。
                quests.ReportVisit("library_pond", false);
            }

            Assert.AreEqual(1, told, "留まっているあいだにも繰り返し知らせている");
        }

        [Test]
        public void OneArgumentReportVisit_StaysQuiet()
        {
            // 引数 1 つの ReportVisit は今までどおり（入った瞬間かどうか分からないので知らせない）。
            QuestSystem quests = Make(17f, SunsetJson);
            int told = 0;
            quests.StepTooEarly += (quest, step) => told++;

            quests.ReportVisit("library_pond");

            Assert.AreEqual(0, told);
            Assert.AreEqual(0, quests.Find("q_sunset").Steps[0].Progress);
        }

        [Test]
        public void ArrivingAtOrAfterMinHour_CompletesWithoutTheMessage()
        {
            QuestSystem quests = Make(18f, SunsetJson);
            int told = 0;
            quests.StepTooEarly += (quest, step) => told++;

            quests.ReportVisit("library_pond", true);

            Assert.AreEqual(0, told, "時刻になっているのに「また来よう」を出した");
            Assert.IsTrue(quests.IsCompleted("q_sunset"), "18 時ちょうどに着いても達成にならない");
        }

        [Test]
        public void WaitingInsideUntilTheHour_CompletesByTheStayReport()
        {
            float hour = 17.9f;
            QuestSystem quests = Make(0f, SunsetJson);
            quests.Clock = () => hour;
            int told = 0;
            quests.StepTooEarly += (quest, step) => told++;

            quests.ReportVisit("library_pond", true);
            hour = 18.05f;
            quests.ReportVisit("library_pond", false);

            Assert.AreEqual(1, told);
            Assert.IsTrue(quests.IsCompleted("q_sunset"), "水盤で待っていて 18 時を過ぎても達成にならない");
        }

        [Test]
        public void StepsWithoutMinHour_AndOtherPlaces_StayQuiet()
        {
            QuestSystem quests = Make(9f, SunsetJson, MallJson);
            int told = 0;
            quests.StepTooEarly += (quest, step) => told++;

            quests.ReportVisit("campus_mall", true);
            quests.ReportVisit("gate_main", true);

            Assert.AreEqual(0, told, "時刻の条件と関係ない場所で知らせている");
            Assert.IsTrue(quests.IsCompleted("q_mall"), "時刻の条件の無いステップが進まない");
        }

        [Test]
        public void TooEarly_IsRaisedBeforeChanged()
        {
            // 追跡表示はトーストを出してから描き直す。順番が逆でも壊れはしないが、ほかの通知と揃える。
            QuestSystem quests = Make(17f, SunsetJson, MallJson);
            var order = new List<string>();
            quests.StepTooEarly += (quest, step) => order.Add("early");
            quests.Changed += () => order.Add("changed");

            quests.ReportVisit("library_pond", true);

            CollectionAssert.AreEqual(new[] { "early" }, order, "進んでいないのに Changed が出ている");
        }

        [Test]
        public void RealQuests_WithMinHour_AreTheOnesTheMessageIsFor()
        {
            // 実データで時刻の条件を持つのは q_sunset（18 時）と q_sq_night_walk（19 時）。
            // どちらも visit なので、VisitZone の入った瞬間の報告で案内が出る。
            var found = new Dictionary<string, float>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(
                    MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                foreach (QuestStep step in quest.Steps)
                {
                    if (step.MinHour > 0f)
                    {
                        Assert.AreEqual(QuestStepKind.Visit, step.Kind,
                            quest.Id + " の時刻条件つきステップが visit でない（入った瞬間の案内が出ない）");
                        found[quest.Id] = step.MinHour;
                    }
                }
            }

            Assert.AreEqual(18f, found["q_sunset"], 1e-4f);
            Assert.AreEqual(19f, found["q_sq_night_walk"], 1e-4f);
        }

        [TestCase("ja", 18, "18時ごろにまた来よう")]
        [TestCase("ja", 19, "19時ごろにまた来よう")]
        [TestCase("en", 18, "Come back around 18:00")]
        [TestCase("en", 19, "Come back around 19:00")]
        public void Localization_HasTheMessageInBothLanguages(string locale, int hour, string expected)
        {
            string path = Path.Combine(DataRoot, "Localization", locale + ".json");
            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " を読めない");
            Dictionary<string, object> strings = MiniJson.GetObject(root, "strings");
            Assert.IsNotNull(strings, path + " に strings が無い");

            string template = MiniJson.GetString(strings, "ui.hud.too_early");
            Assert.IsFalse(string.IsNullOrEmpty(template), locale + " に ui.hud.too_early が無い");
            Assert.AreEqual(expected, string.Format(template, hour));
        }
    }
}
