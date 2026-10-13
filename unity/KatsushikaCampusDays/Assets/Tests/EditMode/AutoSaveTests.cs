using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 自動セーブ (#61)。頼まれたらすぐ書かず、キャンパスにいて封鎖も裏エンドも一日の終わりの待ちも無い最初のフレームで書くこと、
    /// クエストを達成したら頼むこと、セーブからの復元では頼まないこと。
    /// Tick が実際に書く道（SaveSystem.Save がファイルを書く）はここでは通さず、書かない側だけを確かめる。
    /// </summary>
    public sealed class AutoSaveTests
    {
        private const string LibraryJson =
            "{\"id\":\"q_library\",\"order\":2,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"library\"}]}";

        private const string MultiJson =
            "{\"id\":\"q_multi\",\"order\":3,\"steps\":[" +
            "{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"library\"}," +
            "{\"id\":\"s2\",\"type\":\"enter\",\"target\":\"gym\"}]}";

        private readonly object _dialogue = new object();

        [SetUp]
        public void SetUp()
        {
            AutoSave.Cancel();
            KCDInput.ClearAllBlocks();
        }

        [TearDown]
        public void TearDown()
        {
            // どちらも static でテストをまたいで残る。
            AutoSave.Cancel();
            KCDInput.ClearAllBlocks();
        }

        private static QuestSystem Make(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(QuestData.FromJson(MiniJson.Deserialize(jsons[i]) as Dictionary<string, object>));
            }

            var quests = new QuestSystem();
            quests.Load(list);
            return quests;
        }

        // ---- いつ書くか ----

        [TestCase(true, true, false, false, false, true, TestName = "ShouldWrite_RequestedOnCampusWithNothingInTheWay")]
        [TestCase(false, true, false, false, false, false, TestName = "ShouldWrite_NotRequested")]
        [TestCase(true, false, false, false, false, false, TestName = "ShouldWrite_OffCampus")]
        [TestCase(true, true, true, false, false, false, TestName = "ShouldWrite_WhileBlocked")]
        [TestCase(true, true, false, true, false, false, TestName = "ShouldWrite_DuringTheDormEnding")]
        [TestCase(true, true, false, false, true, false, TestName = "ShouldWrite_WhileTheDayEndIsPending")]
        public void ShouldWrite(bool pending, bool onCampus, bool blocked, bool dormEnding, bool dayEndPending, bool expected)
        {
            Assert.AreEqual(expected, AutoSave.ShouldWrite(pending, onCampus, blocked, dormEnding, dayEndPending));
        }

        [Test]
        public void ANightThatCrossedMidnightInADialogue_IsNotSavedBeforeTheResult()
        {
            // 19:30 に会話を始め、会話の中でクエストを達成し、0:30 に会話を閉じる。
            // 封鎖が外れたフレームで、リザルトより先に自動セーブが回ることがある（Update の順は決まっていない）。
            const float dayStart = DayRestart.DayStartHour;
            const float dayEnd = 20f;
            bool latched = false;
            foreach (float hours in new[] { 19.5f, 20.5f, 23.9f, 0.5f })
            {
                latched = DayEndEvaluator.LatchDayEnd(latched, hours, dayStart, dayEnd);
            }

            Assert.IsTrue(DayEndEvaluator.ShouldEndDay(latched, false, false), "封鎖が外れたらリザルトを出す");
            Assert.IsFalse(AutoSave.ShouldWrite(true, true, false, false, latched), "リザルトより先に 0:30 を書かない");

            // 0:30 のセーブを読み直すと、掛け金は立たないまま朝を迎え、その夜は翌日の 20 時まで終わらない。
            Assert.IsFalse(DayEndEvaluator.LatchDayEnd(false, 0.5f, dayStart, dayEnd));
        }

        [Test]
        public void ShouldReportFailure_OnlyTheFirstOfAStreak()
        {
            Assert.IsTrue(AutoSave.ShouldReportFailure(false, false), "初めての失敗は知らせる");
            Assert.IsFalse(AutoSave.ShouldReportFailure(false, true), "続けて失敗している間は黙る");
            Assert.IsFalse(AutoSave.ShouldReportFailure(true, false), "成功は知らせない");
            Assert.IsFalse(AutoSave.ShouldReportFailure(true, true), "失敗のあとの成功も知らせない");
        }

        [Test]
        public void RequestAndCancel()
        {
            Assert.IsFalse(AutoSave.Pending);

            AutoSave.Request();
            AutoSave.Request();
            Assert.IsTrue(AutoSave.Pending, "続けて頼んでも 1 回分として待つ");

            AutoSave.Cancel();
            Assert.IsFalse(AutoSave.Pending);
        }

        [Test]
        public void Tick_WhileBlocked_KeepsTheRequestForLater()
        {
            AutoSave.Request();
            KCDInput.Block(_dialogue);

            AutoSave.Tick(true);

            Assert.IsTrue(AutoSave.Pending, "会話や暗転の最中は書かずに待つ");
            KCDInput.Unblock(_dialogue);
            Assert.IsTrue(AutoSave.ShouldWrite(AutoSave.Pending, true, KCDInput.GameplayBlocked, false, false),
                "封鎖が外れた最初のフレームで書く");
        }

        [Test]
        public void Tick_OffCampus_KeepsTheRequest()
        {
            AutoSave.Request();

            AutoSave.Tick(false);

            Assert.IsTrue(AutoSave.Pending);
        }

        // ---- クエストの達成 ----

        [Test]
        public void Watch_CompletingAQuest_RequestsASave()
        {
            QuestSystem quests = Make(LibraryJson);
            AutoSave.Watch(quests);
            quests.StartQuest("q_library");

            quests.ReportEnter("library");

            Assert.IsTrue(quests.IsCompleted("q_library"));
            Assert.IsTrue(AutoSave.Pending);
        }

        [Test]
        public void Watch_FinishingOnlyAStep_DoesNotRequest()
        {
            QuestSystem quests = Make(MultiJson);
            AutoSave.Watch(quests);
            quests.StartQuest("q_multi");

            quests.ReportEnter("library");

            Assert.IsTrue(quests.IsActive("q_multi"));
            Assert.IsFalse(AutoSave.Pending, "途中の段階では書かない");
        }

        [Test]
        public void Watch_CompletingDuringADialogue_WaitsForTheDialogueToClose()
        {
            QuestSystem quests = Make(LibraryJson);
            AutoSave.Watch(quests);
            quests.StartQuest("q_library");
            KCDInput.Block(_dialogue);

            quests.ReportEnter("library");
            AutoSave.Tick(true);

            Assert.IsTrue(AutoSave.Pending, "会話の途中は進行が書きかけのことがあるので待つ");
        }

        [Test]
        public void Watch_RestoringASave_DoesNotRequest()
        {
            QuestSystem quests = Make(LibraryJson);
            AutoSave.Watch(quests);
            var progress = new QuestProgress();
            progress.QuestIds.Add("q_library");
            progress.StepsDone.Add(1);
            progress.States.Add(QuestProgress.StateCompleted);

            quests.Restore(progress);

            Assert.IsTrue(quests.IsCompleted("q_library"));
            Assert.IsFalse(AutoSave.Pending, "読み込んだ達成で書き直さない");
        }

        [Test]
        public void Watch_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => AutoSave.Watch(null));
        }
    }
}
