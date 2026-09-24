using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 前提（prerequisites）の付いた autoStart のクエストは、前提がそろうまで受注しない (#91)。
    /// 新しいゲーム（Load / ResetForNewGame / GameManager.BeginNewGame）は同じ AutoStartUnlocked を通る。
    ///
    /// 以前の Load は前提を見ずに受注していたので、その頃のセーブには公園・夕焼け・夜の散歩・写真の散歩が
    /// 最初から受注中で残っている。Restore は、1 ステップも済んでいなければ前提がそろうまで待たせ
    /// （失う進行は無い）、1 ステップでも済んでいれば進めた分を消さないよう受注中のまま残す。
    /// </summary>
    public sealed class QuestPrerequisiteGateTests
    {
        private const float EveningHour = 19.5f;

        /// <summary>実データで前提の付いた autoStart。</summary>
        private static readonly string[] GatedAutoStarts = { "q_park", "q_sunset", "q_sq_night_walk", "q_sq_photo_walk" };

        [TearDown]
        public void TearDown()
        {
            DayStats.Reset();
        }

        private static QuestSystem RealQuests(float hour)
        {
            var quests = new QuestSystem { Clock = () => hour };
            quests.LoadFromResources();
            Assert.AreEqual(13, quests.All.Count, "Resources/KCD/Quests のクエストの数が 13 ではない");
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

        private static List<string> ActiveIds(QuestSystem quests)
        {
            var active = new List<string>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                if (quests.IsActive(quests.All[i].Id))
                {
                    active.Add(quests.All[i].Id);
                }
            }

            return active;
        }

        private static void AssertNoGatedAutoStartIsActive(QuestSystem quests, string when)
        {
            for (int i = 0; i < GatedAutoStarts.Length; i++)
            {
                Assert.IsFalse(quests.IsActive(GatedAutoStarts[i]), when + "に前提のそろっていない " + GatedAutoStarts[i] + " を受注している");
            }
        }

        [Test]
        public void RealData_GatedAutoStartsAreTheFourWithPrerequisites()
        {
            // 下のテストが見ている 4 件が、実データの「前提の付いた autoStart」と一致しているか。
            QuestSystem quests = RealQuests(DayRestart.DayStartHour);
            var gated = new List<string>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                QuestData quest = quests.All[i];
                if (quest.AutoStart && quest.Prerequisites.Count > 0)
                {
                    gated.Add(quest.Id);
                }
            }

            CollectionAssert.AreEquivalent(GatedAutoStarts, gated);
        }

        // ---- 新しいゲーム ----

        [Test]
        public void NewGame_DoesNotStartAutoStartsWhosePrerequisitesAreMissing()
        {
            QuestSystem quests = RealQuests(DayRestart.DayStartHour);

            AssertNoGatedAutoStartIsActive(quests, "新しいゲーム");
            CollectionAssert.AreEqual(new[] { "q_orientation" }, ActiveIds(quests));
        }

        [Test]
        public void GatedAutoStart_StartsWhenItsPrerequisiteIsCompleted()
        {
            QuestSystem quests = RealQuests(10f);
            var started = new List<string>();
            quests.QuestStarted += quest => started.Add(quest.Id);

            quests.ReportVisit("gate_main", true);
            quests.ReportVisit("campus_mall", true);
            quests.ReportTalk("prof");

            Assert.IsTrue(quests.IsCompleted("q_orientation"));
            Assert.IsTrue(quests.IsActive("q_sq_photo_walk"), "オリエンテーションを終えても写真の散歩が始まらない");
            CollectionAssert.Contains(started, "q_sq_photo_walk", "受注の知らせ（QuestStarted）が出ていない");
            Assert.IsFalse(quests.IsActive("q_park"), "図書館の前に公園が始まった");

            Assert.IsTrue(quests.StartQuest("q_library"));
            quests.ReportEnter("library");
            quests.ReportTalk("inari");

            Assert.IsTrue(quests.IsCompleted("q_library"));
            Assert.IsTrue(quests.IsActive("q_park"), "図書館を終えても公園が始まらない");
            Assert.IsFalse(quests.IsActive("q_sunset"), "公園の前に夕焼けが始まった");
        }

        [Test]
        public void ResetForNewGame_AfterAFinishedRun_StartsOnlyTheOrientation()
        {
            QuestSystem quests = RealQuests(EveningHour);
            var ids = new List<string>();
            var done = new List<int>();
            var states = new List<int>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                ids.Add(quests.All[i].Id);
                done.Add(quests.All[i].Steps.Count);
                states.Add(QuestProgress.StateCompleted);
            }

            quests.Restore(Rows(ids.ToArray(), done.ToArray(), states.ToArray()));
            Assert.IsTrue(quests.IsCompleted("q_sq_night_walk"));

            quests.ResetForNewGame();

            AssertNoGatedAutoStartIsActive(quests, "はじめから");
            CollectionAssert.AreEqual(new[] { "q_orientation" }, ActiveIds(quests));
        }

        [Test]
        public void BeginNewGame_DoesNotStartGatedAutoStarts()
        {
            // NewGameTests.BeginNewGame_PutsEverythingBackToTheFirstMorning と同じ作法（EditMode では Awake が走らない）。
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("GameManagerForTest");

            try
            {
                GameManager manager = go.AddComponent<GameManager>();

                manager.BeginNewGame();

                Assert.IsNotNull(manager.Quests);
                AssertNoGatedAutoStartIsActive(manager.Quests, "BeginNewGame");
                CollectionAssert.AreEqual(new[] { "q_orientation" }, ActiveIds(manager.Quests));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = ignoring;
            }
        }

        // ---- 古いセーブ（前提を見ずに受注していた頃）----

        /// <summary>オリエンテーションを終え、図書館を受けたところで保存した古いセーブ。前提の付いた autoStart も受注中で残っている。</summary>
        private static QuestProgress OldSaveAfterOrientation()
        {
            return Rows(
                new[] { "q_orientation", "q_library", "q_park", "q_sunset", "q_sq_night_walk", "q_sq_photo_walk" },
                new[] { 3, 0, 0, 0, 0, 0 },
                new[]
                {
                    QuestProgress.StateCompleted, QuestProgress.StateActive, QuestProgress.StateActive,
                    QuestProgress.StateActive, QuestProgress.StateActive, QuestProgress.StateActive,
                });
        }

        [Test]
        public void Restore_OldSave_UntouchedGatedAutoStartsWaitForTheirPrerequisites()
        {
            QuestSystem quests = RealQuests(EveningHour);

            quests.Restore(OldSaveAfterOrientation());

            Assert.IsFalse(quests.IsActive("q_park"), "図書館の前の公園が受注中のまま");
            Assert.IsFalse(quests.IsActive("q_sunset"), "公園の前の夕焼けが受注中のまま");
            Assert.IsFalse(quests.IsActive("q_sq_night_walk"), "夕焼けの前の夜の散歩が受注中のまま");
            Assert.IsTrue(quests.IsActive("q_sq_photo_walk"), "前提（オリエンテーション）はそろっているので受注中のまま");
            Assert.IsTrue(quests.IsActive("q_library"));

            // 以前は 19 時に正門へ行くだけで夜の散歩を達成できた（夕焼けも見ずに ach_full_day）。
            quests.ReportVisit("gate_main", true);
            Assert.IsFalse(quests.IsCompleted("q_sq_night_walk"), "夕焼けの前に夜の散歩を達成できる");
        }

        [Test]
        public void Restore_OldSave_HeldQuestStartsOnceThePrerequisiteIsDone()
        {
            QuestSystem quests = RealQuests(EveningHour);
            quests.Restore(OldSaveAfterOrientation());
            var started = new List<string>();
            quests.QuestStarted += quest => started.Add(quest.Id);

            quests.ReportEnter("library");
            quests.ReportTalk("inari");

            Assert.IsTrue(quests.IsCompleted("q_library"));
            Assert.IsTrue(quests.IsActive("q_park"), "図書館を終えても公園が始まらない");
            CollectionAssert.Contains(started, "q_park", "受注の知らせ（QuestStarted）が出ていない");
        }

        [Test]
        public void Restore_OldSave_LeavesPickedLeavesToCountWhenTheParkStarts()
        {
            // 待たせているあいだに拾った葉は、公園を受注したときに数える（QuestHeldItems）。待たせても拾った分は失わない。
            QuestSystem quests = RealQuests(EveningHour);
            quests.Restore(OldSaveAfterOrientation());

            for (int i = 0; i < 5; i++)
            {
                quests.ReportCollect("leaf");
            }

            Assert.IsFalse(quests.IsActive("q_park"));

            quests.ReportEnter("library");
            quests.ReportTalk("inari");

            Assert.IsTrue(quests.IsCompleted("q_park"), "待っているあいだに拾った葉が数えられていない");
            Assert.IsTrue(quests.IsActive("q_sunset"));
        }

        [Test]
        public void Restore_OldSave_GatedAutoStartWithADoneStep_KeepsItsProgress()
        {
            // 写真の散歩を 1 か所撮ったところで、オリエンテーションはまだ途中。進めた分は消さない。
            QuestSystem quests = RealQuests(EveningHour);

            quests.Restore(Rows(
                new[] { "q_orientation", "q_sq_photo_walk" },
                new[] { 2, 1 },
                new[] { QuestProgress.StateActive, QuestProgress.StateActive }));

            Assert.IsFalse(quests.IsCompleted("q_orientation"));
            Assert.IsTrue(quests.IsActive("q_sq_photo_walk"), "撮った 1 か所ぶんの進行が消えた");
            QuestData walk = quests.Find("q_sq_photo_walk");
            Assert.IsTrue(walk.Steps[0].Completed);
            Assert.AreEqual("s2", walk.CurrentStep.Id);
        }

        [Test]
        public void Restore_HeldQuest_IsSavedAsNotStarted()
        {
            QuestSystem quests = RealQuests(EveningHour);
            quests.Restore(OldSaveAfterOrientation());

            QuestProgress saved = quests.Capture();

            int row = saved.QuestIds.IndexOf("q_park");
            Assert.GreaterOrEqual(row, 0);
            Assert.AreEqual(QuestProgress.StateNotStarted, saved.States[row]);
        }

        [Test]
        public void Restore_RequestedQuestWithMissingPrerequisites_IsLeftActive()
        {
            // 会話の依頼（autoStart でない）は StartQuest が前提を見てから受けるので、古いバグでは生まれない。
            // 壊れたセーブでも待たせると依頼の話題に話しかけ直すしかなくなるので、受注中のまま残す。
            QuestSystem quests = RealQuests(EveningHour);

            quests.Restore(Rows(
                new[] { "q_orientation", "q_gym" },
                new[] { 0, 0 },
                new[] { QuestProgress.StateActive, QuestProgress.StateActive }));

            Assert.IsTrue(quests.IsActive("q_gym"));
        }

        [Test]
        public void Restore_DoesNotAnnounceTheHeldQuestAsStarted()
        {
            QuestSystem quests = RealQuests(EveningHour);
            var started = new List<string>();
            quests.QuestStarted += quest => started.Add(quest.Id);

            quests.Restore(OldSaveAfterOrientation());

            CollectionAssert.DoesNotContain(started, "q_park");
            CollectionAssert.DoesNotContain(started, "q_sunset");
            CollectionAssert.DoesNotContain(started, "q_sq_night_walk");
        }
    }
}
