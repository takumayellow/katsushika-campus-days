using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// セーブからのクエスト進行の復元 (#61)。壊れた行・食い違った行を読んでも例外を出さず、
    /// 読み込む前の進行を持ち越さず、セーブのあとに足された autoStart のクエストも始まること。
    /// </summary>
    public sealed class QuestRestoreTests
    {
        private const string MultiJson =
            "{\"id\":\"q_multi\",\"order\":3,\"steps\":[" +
            "{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"a\"}," +
            "{\"id\":\"s2\",\"type\":\"enter\",\"target\":\"library\"}," +
            "{\"id\":\"s3\",\"type\":\"visit\",\"target\":\"poi\"}]}";

        private const string LibraryJson =
            "{\"id\":\"q_library\",\"order\":2,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"library\"}]}";

        private const string AutoJson =
            "{\"id\":\"q_auto\",\"order\":1,\"autoStart\":true,\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"b\"}]}";

        /// <summary>q_library を終えると自動で始まる。</summary>
        private const string FollowUpJson =
            "{\"id\":\"q_follow\",\"order\":4,\"autoStart\":true,\"prerequisites\":[\"q_library\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"c\"}]}";

        private static QuestData Parse(string json)
        {
            return QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        private static QuestSystem Make(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(Parse(jsons[i]));
            }

            var quests = new QuestSystem();
            quests.Load(list);
            return quests;
        }

        private static QuestProgress Rows(string[] ids, int[] stepsDone, int[] states)
        {
            var progress = new QuestProgress();
            progress.QuestIds.AddRange(ids);
            progress.StepsDone.AddRange(stepsDone);
            progress.States.AddRange(states);
            return progress;
        }

        private static int DoneSteps(QuestData quest)
        {
            int done = 0;
            for (int i = 0; i < quest.Steps.Count; i++)
            {
                if (quest.Steps[i].Completed)
                {
                    done++;
                }
            }

            return done;
        }

        [Test]
        public void Restore_NullLists_DoesNotThrowAndOnlyStartsAutoStartQuests()
        {
            QuestSystem quests = Make(AutoJson, LibraryJson);
            quests.StartQuest("q_library");
            var broken = new QuestProgress { QuestIds = null, StepsDone = null, States = null };

            Assert.DoesNotThrow(() => quests.Restore(broken));

            Assert.IsFalse(quests.IsActive("q_library"), "行の無いセーブでは受注していない");
            Assert.IsTrue(quests.IsActive("q_auto"), "autoStart は始まっている");
        }

        [Test]
        public void Restore_MismatchedColumns_ReadsOnlyTheRowsThatAreComplete()
        {
            QuestSystem quests = Make(MultiJson, LibraryJson);

            quests.Restore(Rows(new[] { "q_multi", "q_library" }, new[] { 1 }, new[] { 1, 1 }));

            Assert.IsTrue(quests.IsActive("q_multi"));
            Assert.AreEqual(1, DoneSteps(quests.Find("q_multi")));
            Assert.IsFalse(quests.IsActive("q_library"), "StepsDone の無い 2 行目は読まない");
        }

        [Test]
        public void Restore_CompletedQuest_HasEveryStepDone()
        {
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { 0 }, new[] { QuestProgress.StateCompleted }));

            QuestData quest = quests.Find("q_multi");
            Assert.IsTrue(quests.IsCompleted("q_multi"));
            Assert.IsFalse(quests.IsActive("q_multi"));
            Assert.AreEqual(3, DoneSteps(quest), "完了なのに途中のステップを見せない");
            Assert.IsTrue(quest.IsComplete);
        }

        [Test]
        public void Restore_ActiveQuestWithEveryStepDone_CountsAsCompleted()
        {
            // 受注中のまま全部済んだ行は、CurrentStep が null で Report が二度と完了にできない。
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { 3 }, new[] { QuestProgress.StateActive }));

            Assert.IsTrue(quests.IsCompleted("q_multi"));
            Assert.IsFalse(quests.IsActive("q_multi"));
        }

        [Test]
        public void Restore_TooManyStepsDone_IsClampedToTheQuest()
        {
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { 99 }, new[] { QuestProgress.StateActive }));

            Assert.IsTrue(quests.IsCompleted("q_multi"));
            Assert.AreEqual(3, DoneSteps(quests.Find("q_multi")));
        }

        [Test]
        public void Restore_NegativeStepsDone_StartsFromTheFirstStep()
        {
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { -2 }, new[] { QuestProgress.StateActive }));

            Assert.IsTrue(quests.IsActive("q_multi"));
            Assert.AreEqual(0, DoneSteps(quests.Find("q_multi")));
            Assert.AreEqual("s1", quests.Find("q_multi").CurrentStep.Id);
        }

        [Test]
        public void Restore_NotStartedQuest_HasNoStepsDone()
        {
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { 2 }, new[] { QuestProgress.StateNotStarted }));

            Assert.IsFalse(quests.IsActive("q_multi"));
            Assert.IsFalse(quests.IsCompleted("q_multi"));
            Assert.AreEqual(0, DoneSteps(quests.Find("q_multi")), "受注していないのに途中から始めない");
        }

        [Test]
        public void Restore_UnknownState_IsReadAsNotStarted()
        {
            QuestSystem quests = Make(MultiJson);

            quests.Restore(Rows(new[] { "q_multi" }, new[] { 1 }, new[] { 7 }));

            Assert.IsFalse(quests.IsActive("q_multi"));
            Assert.IsFalse(quests.IsCompleted("q_multi"));
            Assert.AreEqual(0, DoneSteps(quests.Find("q_multi")));
        }

        [Test]
        public void Restore_UnknownAndRepeatedIds_AreSkipped()
        {
            QuestSystem quests = Make(MultiJson);

            Assert.DoesNotThrow(() => quests.Restore(Rows(
                new[] { "q_removed", "q_multi", "q_multi" },
                new[] { 0, 1, 3 },
                new[] { QuestProgress.StateActive, QuestProgress.StateActive, QuestProgress.StateCompleted })));

            Assert.IsTrue(quests.IsActive("q_multi"), "同じ id の 2 行目で上書きしない");
            Assert.IsFalse(quests.IsCompleted("q_multi"));
            Assert.AreEqual(1, DoneSteps(quests.Find("q_multi")));
        }

        [Test]
        public void Restore_ForgetsProgressOfQuestsThatAreNotInTheSave()
        {
            QuestSystem quests = Make(MultiJson);
            quests.StartQuest("q_multi");
            quests.ReportTalk("a");
            Assert.AreEqual(1, DoneSteps(quests.Find("q_multi")), "前提: 1 ステップ進めた");

            quests.Restore(new QuestProgress());

            Assert.IsFalse(quests.IsActive("q_multi"));
            Assert.AreEqual(0, DoneSteps(quests.Find("q_multi")), "読み込む前の進行を持ち越さない");
        }

        [Test]
        public void Restore_AutoStartQuestAddedAfterTheSave_StartsWithoutAnnouncing()
        {
            QuestSystem before = Make(LibraryJson);
            before.StartQuest("q_library");
            QuestProgress saved = before.Capture();

            QuestSystem after = Make(LibraryJson, AutoJson);
            int started = 0;
            after.QuestStarted += q => started++;
            after.Restore(saved);

            Assert.IsTrue(after.IsActive("q_library"));
            Assert.IsTrue(after.IsActive("q_auto"), "セーブに行の無い autoStart は始める");
            Assert.AreEqual(0, started, "ロードで受注の通知は出さない");
        }

        [Test]
        public void Restore_AutoStartQuestWithUnmetPrerequisite_WaitsForIt()
        {
            QuestSystem before = Make(LibraryJson);
            before.StartQuest("q_library");
            QuestProgress saved = before.Capture();

            QuestSystem after = Make(LibraryJson, FollowUpJson);
            after.Restore(saved);

            Assert.IsFalse(after.IsActive("q_follow"), "q_library を終えるまでは始めない");
        }

        [Test]
        public void Restore_AutoStartQuestWithMetPrerequisite_Starts()
        {
            QuestSystem quests = Make(LibraryJson, FollowUpJson);

            quests.Restore(Rows(new[] { "q_library" }, new[] { 1 }, new[] { QuestProgress.StateCompleted }));

            Assert.IsTrue(quests.IsActive("q_follow"));
        }

        [Test]
        public void Restore_AutoStartQuestInTheSave_KeepsItsSavedState()
        {
            // 完了したものを autoStart だからと言って受注し直さない。
            QuestSystem quests = Make(AutoJson);

            quests.Restore(Rows(new[] { "q_auto" }, new[] { 1 }, new[] { QuestProgress.StateCompleted }));

            Assert.IsTrue(quests.IsCompleted("q_auto"));
            Assert.IsFalse(quests.IsActive("q_auto"));
        }

        [Test]
        public void CaptureAndRestore_KeepsPartialProgress()
        {
            QuestSystem before = Make(MultiJson, LibraryJson);
            before.StartQuest("q_multi");
            before.ReportTalk("a");
            before.StartQuest("q_library");
            before.ReportEnter("library");
            QuestProgress saved = before.Capture();

            QuestSystem after = Make(MultiJson, LibraryJson);
            after.Restore(saved);

            Assert.IsTrue(after.IsActive("q_multi"));
            Assert.AreEqual("s3", after.Find("q_multi").CurrentStep.Id, "図書館に入った分も q_multi の 2 つめとして進んでいる");
            Assert.IsTrue(after.IsCompleted("q_library"));
            CollectionAssert.AreEqual(saved.QuestIds, after.Capture().QuestIds);
            CollectionAssert.AreEqual(saved.StepsDone, after.Capture().StepsDone);
            CollectionAssert.AreEqual(saved.States, after.Capture().States);
        }
    }
}
