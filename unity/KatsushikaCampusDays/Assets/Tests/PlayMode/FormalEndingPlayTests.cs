using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 本編のエンディング (#176)。夕日のクエストを達成すると、稲荷の夕日の話が流れ、暗転して 3 枚のページが出て、
    /// 最後のページを閉じるとキャンパスに戻る（時間が動き、操作の封鎖が外れ、日付は進まない）。
    /// </summary>
    public sealed class FormalEndingPlayTests : SceneTestBase
    {
        /// <summary>夕日の水盤に行く時刻。q_sunset の minHour（18 時）の後で、一日の終わり（20 時）の前。</summary>
        private const float SunsetHour = 18.5f;

        /// <summary>会話・ページ・暗転の明けを待つ上限（実時間の秒）。</summary>
        private const float StepTimeoutSeconds = 20f;

        /// <summary>会話を送る回数の上限。t_sunset の行数より十分に多い。</summary>
        private const int MaxLines = 100;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_SunsetQuest_PlaysTheTalkThenThePagesAndReturnsToCampus()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            GameManager manager = GameManager.Instance;
            FormalEnding ending = Object.FindAnyObjectByType<FormalEnding>();
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            DialogueSystem dialogue = DialogueSystem.Instance;
            Assert.IsNotNull(ending, "キャンパスに FormalEnding が無い（SceneBuilder → FormalEndingFactory）");
            Assert.IsNotNull(cycle, "キャンパスに DayNightCycle が無い");
            Assert.IsNotNull(dialogue, "キャンパスに DialogueSystem が無い");
            Assert.IsFalse(ending.HasPlayed);
            int day = manager.DayNumber;

            cycle.SetHours(SunsetHour);
            yield return null;

            QuestSystem quests = manager.Quests;
            quests.Restore(SunsetReady(quests));
            Assert.IsTrue(quests.IsActive(FormalEnding.SunsetQuestId), "前提をそろえても夕日のクエストが受注中にならない");
            Assert.IsFalse(FormalEnding.IsAnyShowing, "読み込みでエンディングが始まった");

            quests.ReportVisit("library_pond", true);
            Assert.IsTrue(quests.IsCompleted(FormalEnding.SunsetQuestId), "18 時過ぎに水盤へ行っても夕日を達成しない");
            Assert.IsTrue(FormalEnding.IsAnyShowing, "夕日を達成してもエンディングが始まらない");
            Assert.IsTrue(KCDInput.GameplayBlocked, "会話を待っている間に操作できる");

            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!dialogue.IsPlaying && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(dialogue.IsPlaying, StepTimeoutSeconds + " 秒待っても夕日の話が始まらない");
            for (int i = 0; i < MaxLines && dialogue.IsPlaying; i++)
            {
                dialogue.Advance();
                yield return null;
            }

            Assert.IsFalse(dialogue.IsPlaying, "夕日の話が終わらない");

            for (int page = 0; page < FormalEnding.PageCount; page++)
            {
                deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
                while (ending.CurrentPage != page && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.AreEqual(page, ending.CurrentPage, (page + 1) + " 枚目のページが出ない");
                Assert.AreEqual(0f, Time.timeScale, "ページの間に時間が止まっていない");
                ending.AdvancePage();
                yield return null;
            }

            deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (FormalEnding.IsAnyShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(FormalEnding.IsAnyShowing, "最後のページを閉じてもエンディングが終わらない");
            Assert.IsTrue(ending.HasPlayed);
            Assert.AreEqual(-1, ending.CurrentPage, "閉じたのにページが出たまま");
            Assert.AreEqual(1f, Time.timeScale, "キャンパスに戻ったのに時間が止まったまま");
            Assert.IsFalse(KCDInput.GameplayBlocked, "キャンパスに戻ったのに操作が封鎖されたまま");
            Assert.AreEqual(day, manager.DayNumber, "エンディングで日付が進んだ");

            ending.Play();
            Assert.IsFalse(FormalEnding.IsAnyShowing, "二度目が出た");

            AssertNoErrors("本編のエンディング");
        }

        /// <summary>夕日の前提をすべて完了にし、夕日を受注中（0 ステップ）にした進行。</summary>
        private static QuestProgress SunsetReady(QuestSystem quests)
        {
            QuestData sunset = quests.Find(FormalEnding.SunsetQuestId);
            Assert.IsNotNull(sunset, FormalEnding.SunsetQuestId + " が無い");

            var progress = new QuestProgress();
            foreach (string id in sunset.Prerequisites)
            {
                QuestData quest = quests.Find(id);
                Assert.IsNotNull(quest, "夕日の前提 " + id + " が無い");
                progress.QuestIds.Add(id);
                progress.StepsDone.Add(quest.Steps.Count);
                progress.States.Add(QuestProgress.StateCompleted);
            }

            progress.QuestIds.Add(sunset.Id);
            progress.StepsDone.Add(0);
            progress.States.Add(QuestProgress.StateActive);
            return progress;
        }
    }
}
