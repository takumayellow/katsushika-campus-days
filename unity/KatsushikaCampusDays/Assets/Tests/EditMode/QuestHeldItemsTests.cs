using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 受注する前に拾った物も collect ステップに数える (#65)。
    ///
    /// 以前は拾った瞬間に受注中のクエストへ報告するだけだった。拾った物はその場から消えるので、
    /// 実験ノート（lab1 に入る → c_lab_goggles を拾う）を受ける前にゴーグルを拾うと、二度と達成できなかった。
    /// 牛乳（q_lunch）と理科大グリーンの葉（q_park）も、受注前に拾えば同じことになる。
    /// </summary>
    public sealed class QuestHeldItemsTests
    {
        private const string OrientationJson =
            "{\"id\":\"q_first\",\"order\":1,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"prof\"}]}";

        private const string LabNotebookJson =
            "{\"id\":\"q_sq_lab_notebook\",\"order\":13,\"autoStart\":true,\"prerequisites\":[\"q_first\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"lab1\"}," +
            "{\"id\":\"s2\",\"type\":\"collect\",\"target\":\"c_lab_goggles\"}," +
            "{\"id\":\"s3\",\"type\":\"talk\",\"target\":\"sora\"}]}";

        private const string ParkJson =
            "{\"id\":\"q_park\",\"order\":6,\"autoStart\":true,\"prerequisites\":[\"q_first\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"collect\",\"target\":\"leaf\",\"count\":5}]}";

        private const string OneLeafJson =
            "{\"id\":\"q_one_leaf\",\"order\":1,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"collect\",\"target\":\"leaf\"}]}";

        private const string FiveLeavesAfterOneJson =
            "{\"id\":\"q_five_leaves\",\"order\":2,\"autoStart\":true,\"prerequisites\":[\"q_one_leaf\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"collect\",\"target\":\"leaf\",\"count\":5}]}";

        private const string LunchJson =
            "{\"id\":\"q_lunch\",\"order\":3,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"collect\",\"target\":\"milk\"}," +
            "{\"id\":\"s2\",\"type\":\"talk\",\"target\":\"kaname\"}]}";

        private const string TimedLeafJson =
            "{\"id\":\"q_leaf_dash\",\"order\":8,\"autoStart\":true,\"prerequisites\":[\"q_first\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"collect\",\"target\":\"leaf\",\"timeLimit\":30}]}";

        private static QuestSystem Make(params string[] jsons)
        {
            var quests = new QuestSystem();
            quests.Load(Parse(jsons));
            return quests;
        }

        private static List<QuestData> Parse(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(QuestData.FromJson(MiniJson.Deserialize(jsons[i]) as Dictionary<string, object>));
            }

            return list;
        }

        [Test]
        public void GogglesPickedUpBeforeTheQuest_CountWhenTheStepComesUp()
        {
            QuestSystem quests = Make(OrientationJson, LabNotebookJson);
            Assert.IsFalse(quests.IsActive("q_sq_lab_notebook"), "前提を終える前から受注している");

            quests.ReportCollect("c_lab_goggles");
            quests.ReportTalk("prof");
            Assert.IsTrue(quests.IsActive("q_sq_lab_notebook"));

            quests.ReportEnter("lab1");

            QuestData notebook = quests.Find("q_sq_lab_notebook");
            Assert.IsTrue(notebook.Steps[1].Completed, "受注前に拾ったゴーグルが数えられていない");
            Assert.AreEqual("s3", notebook.CurrentStep.Id, "ゴーグルの次（空に話す）へ進んでいない");

            quests.ReportTalk("sora");
            Assert.IsTrue(quests.IsCompleted("q_sq_lab_notebook"), "実験ノートを最後まで達成できない");
        }

        [Test]
        public void LeavesPickedUpBeforeTheQuest_CountTowardsTheFive()
        {
            QuestSystem quests = Make(OrientationJson, ParkJson);
            quests.ReportCollect("leaf");
            quests.ReportCollect("leaf");
            quests.ReportCollect("leaf");

            quests.ReportTalk("prof");
            QuestStep step = quests.Find("q_park").Steps[0];
            Assert.AreEqual(3, step.Progress, "受注前に拾った 3 枚が数えられていない");

            quests.ReportCollect("leaf");
            quests.ReportCollect("leaf");
            Assert.IsTrue(quests.IsCompleted("q_park"), "5 枚そろっても達成にならない");
        }

        [Test]
        public void AllFiveBeforeTheQuest_CompletesAsSoonAsItStarts()
        {
            QuestSystem quests = Make(OrientationJson, ParkJson);
            var completed = new List<string>();
            quests.QuestCompleted += q => completed.Add(q.Id);
            for (int i = 0; i < 5; i++)
            {
                quests.ReportCollect("leaf");
            }

            quests.ReportTalk("prof");

            Assert.IsTrue(quests.IsCompleted("q_park"));
            CollectionAssert.AreEqual(new[] { "q_first", "q_park" }, completed, "完了の知らせが足りないか順番が違う");
        }

        [Test]
        public void OnePickUp_IsNotCountedTwiceForAQuestStartedByIt()
        {
            // 1 枚目で q_one_leaf が終わり、同じ報告の途中で q_five_leaves を受注する。
            // 受注のときの追いつきで 1 枚を数えたうえ、同じループでもう 1 度足すと 2 枚になる。
            QuestSystem quests = Make(OneLeafJson, FiveLeavesAfterOneJson);

            quests.ReportCollect("leaf");

            Assert.IsTrue(quests.IsCompleted("q_one_leaf"));
            Assert.AreEqual(1, quests.Find("q_five_leaves").Steps[0].Progress, "1 枚を 2 枚に数えている");

            quests.ReportCollect("leaf");
            Assert.AreEqual(2, quests.Find("q_five_leaves").Steps[0].Progress);
        }

        [Test]
        public void TimedCollectSteps_OnlyCountWhatIsPickedUpWhileTheClockRuns()
        {
            QuestSystem quests = Make(OrientationJson, TimedLeafJson);
            quests.ReportCollect("leaf");

            quests.ReportTalk("prof");

            Assert.AreEqual(0, quests.Find("q_leaf_dash").Steps[0].Progress,
                "前もって拾っておけば済む挑戦になっている");
        }

        [Test]
        public void LoadingAnEarlierSave_KeepsWhatWasPickedUpThisSession()
        {
            QuestSystem quests = Make(LunchJson);
            QuestProgress before = quests.Capture();

            // 牛乳を拾ってから、拾う前のセーブを読み込む。牛乳はもう消えている。
            quests.ReportCollect("milk");
            Assert.IsTrue(quests.Find("q_lunch").Steps[0].Completed);
            quests.Restore(before);

            QuestData lunch = quests.Find("q_lunch");
            Assert.IsTrue(lunch.Steps[0].Completed, "読み込んだら拾った牛乳が数えられなくなった");
            Assert.AreEqual("s2", lunch.CurrentStep.Id);
        }

        [Test]
        public void NewGame_ForgetsWhatWasPickedUp()
        {
            QuestSystem quests = Make(LunchJson);
            quests.ReportCollect("milk");
            Assert.AreEqual(1, quests.HeldCount("milk"));

            // 「はじめから」（ResetForNewGame）は Load で中身を入れ替える。前の周回の拾得を持ち越さない。
            quests.Load(Parse(LunchJson), true);

            Assert.AreEqual(0, quests.HeldCount("milk"));
            Assert.AreEqual(0, quests.Find("q_lunch").Steps[0].Progress);
        }

        [Test]
        public void HeldCount_CountsEveryPickUp()
        {
            QuestSystem quests = Make(OrientationJson);
            quests.ReportCollect("leaf");
            quests.ReportCollect("leaf");
            quests.ReportCollect(string.Empty);

            Assert.AreEqual(2, quests.HeldCount("leaf"));
            Assert.AreEqual(0, quests.HeldCount("milk"));
            Assert.AreEqual(0, quests.HeldCount(null));
        }
    }
}
