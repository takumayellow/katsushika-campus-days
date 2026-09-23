using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 制限時間つきクエスト（体育館まで 60 秒）の進行。受注で 1 回だけ数え始め、時間内に入れば達成、
    /// 時間切れなら失敗で止まり（黙って数え直さない）、依頼主に話しかけると再挑戦になる。
    /// </summary>
    public sealed class QuestChallengeTests
    {
        private const string GymJson =
            "{\"id\":\"q_gym\",\"title\":\"体育館まで 60 秒\",\"order\":5,\"giver\":\"prof\"," +
            "\"rewardText\":\"R\",\"rewardType\":\"achievement\",\"rewardId\":\"ach_sprinter\"," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"gym\",\"timeLimit\":60}]}";

        private const string LibraryJson =
            "{\"id\":\"q_library\",\"order\":2,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"library\"}]}";

        private const string NoGiverJson =
            "{\"id\":\"q_dash\",\"order\":9,\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"poi\",\"timeLimit\":30}]}";

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

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

        private static QuestStep GymStep(QuestSystem quests)
        {
            return quests.Find("q_gym").Steps[0];
        }

        [Test]
        public void FromJson_ReadsGiverAndRewardIntoTimedStep()
        {
            QuestData quest = Parse(GymJson);

            Assert.AreEqual("prof", quest.Giver);
            Assert.AreEqual(QuestData.RewardAchievement, quest.RewardType);
            Assert.AreEqual("ach_sprinter", quest.RewardId);
            Assert.IsTrue(quest.Steps[0].IsTimed);
            Assert.AreEqual("prof", quest.Steps[0].Giver, "ステップにも依頼主を写す");
            Assert.AreEqual(ChallengeState.Idle, quest.Steps[0].Timer.State, "受注前は数えない");
        }

        [Test]
        public void StartQuest_StartsTheTimerOnce()
        {
            QuestSystem quests = Make(GymJson);
            int started = 0;
            quests.TimerStarted += q => started++;

            Assert.AreEqual(-1f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f, "受注前は数えない");
            Assert.IsTrue(quests.StartQuest("q_gym"));

            Assert.AreEqual(1, started);
            Assert.AreEqual(60f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f);

            quests.Tick(10f, false);
            Assert.AreEqual(50f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f, "毎フレーム数え直さず減っていく");
            Assert.AreEqual(1, started);
        }

        [Test]
        public void EnterInTime_CompletesWithAchievementReward()
        {
            QuestSystem quests = Make(GymJson);
            QuestData completed = null;
            int timedOut = 0;
            quests.QuestCompleted += q => completed = q;
            quests.StepTimedOut += q => timedOut++;

            quests.StartQuest("q_gym");
            quests.Tick(27f, false);
            quests.ReportEnter("gym");

            Assert.IsTrue(quests.IsCompleted("q_gym"));
            Assert.IsFalse(quests.IsActive("q_gym"));
            Assert.IsNotNull(completed);
            Assert.AreEqual("q_gym", completed.Id);
            Assert.AreEqual(QuestData.RewardAchievement, completed.RewardType);
            Assert.AreEqual("ach_sprinter", completed.RewardId);
            Assert.AreEqual(ChallengeState.Cleared, completed.Steps[0].Timer.State);
            Assert.AreEqual(33f, completed.Steps[0].Timer.Remaining, 1e-4f, "残り何秒で着いたか");

            quests.Tick(100f, false);
            Assert.AreEqual(0, timedOut, "達成のあとは時間切れにならない");
        }

        [Test]
        public void EnteringAnotherBuilding_DoesNotComplete()
        {
            QuestSystem quests = Make(GymJson);
            quests.StartQuest("q_gym");

            quests.ReportEnter("library");

            Assert.IsTrue(quests.IsActive("q_gym"));
            Assert.IsTrue(GymStep(quests).Timer.IsRunning);
        }

        [Test]
        public void Timeout_FailsOnceAndDoesNotSilentlyRestart()
        {
            QuestSystem quests = Make(GymJson);
            int timedOut = 0;
            int started = 0;
            quests.StepTimedOut += q => timedOut++;
            quests.TimerStarted += q => started++;

            quests.StartQuest("q_gym");
            quests.Tick(59.5f, false);
            Assert.AreEqual(0, timedOut);

            quests.Tick(1f, false);
            Assert.AreEqual(1, timedOut);

            // 以前はここで計時を捨て、次のフレームから黙って 60 秒を数え直していた。
            for (int i = 0; i < 300; i++)
            {
                quests.Tick(0.5f, false);
            }

            QuestStep step = GymStep(quests);
            Assert.AreEqual(1, timedOut, "時間切れは 1 回だけ");
            Assert.AreEqual(1, started, "勝手に数え直さない");
            Assert.AreEqual(ChallengeState.Failed, step.Timer.State);
            Assert.AreEqual(-1f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f, "失敗中は残り秒を出さない");
            Assert.IsTrue(quests.IsActive("q_gym"), "受注中のまま残し、再挑戦を待つ");
            Assert.IsFalse(quests.IsCompleted("q_gym"));
            Assert.IsTrue(step.AwaitingGiver);
        }

        [Test]
        public void EnterAfterTimeout_DoesNotComplete()
        {
            QuestSystem quests = Make(GymJson);
            quests.StartQuest("q_gym");
            quests.Tick(61f, false);

            quests.ReportEnter("gym");

            Assert.IsFalse(quests.IsCompleted("q_gym"), "時間切れのあとに着いても達成にならない");
            Assert.IsTrue(quests.IsActive("q_gym"));
            Assert.IsFalse(GymStep(quests).Completed);
        }

        [Test]
        public void TalkingToGiver_AfterTimeout_Retries()
        {
            QuestSystem quests = Make(GymJson);
            int started = 0;
            QuestData completed = null;
            quests.TimerStarted += q => started++;
            quests.QuestCompleted += q => completed = q;

            quests.StartQuest("q_gym");
            quests.Tick(61f, false);
            quests.ReportTalk("prof");

            Assert.AreEqual(2, started);
            Assert.IsTrue(GymStep(quests).Timer.IsRunning);
            Assert.AreEqual(60f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f, "はじめから数え直す");

            quests.Tick(40f, false);
            quests.ReportEnter("gym");

            Assert.IsTrue(quests.IsCompleted("q_gym"));
            Assert.IsNotNull(completed);
        }

        [Test]
        public void TalkingToSomeoneElse_DoesNotRetry()
        {
            QuestSystem quests = Make(GymJson);
            quests.StartQuest("q_gym");
            quests.Tick(61f, false);

            quests.ReportTalk("kaname");

            Assert.IsTrue(GymStep(quests).Timer.HasFailed);
        }

        [Test]
        public void TalkingToGiver_WhileRunning_RestartsFromTheStartLine()
        {
            QuestSystem quests = Make(GymJson);
            quests.StartQuest("q_gym");
            quests.Tick(45f, false);

            quests.ReportTalk("prof");

            Assert.AreEqual(60f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f);
        }

        [Test]
        public void TalkingToGiver_AfterCompletion_DoesNothing()
        {
            QuestSystem quests = Make(GymJson);
            int started = 0;
            quests.TimerStarted += q => started++;

            quests.StartQuest("q_gym");
            quests.ReportEnter("gym");
            quests.ReportTalk("prof");

            Assert.AreEqual(1, started);
            Assert.IsTrue(quests.IsCompleted("q_gym"));
            Assert.AreEqual(ChallengeState.Cleared, GymStep(quests).Timer.State);
        }

        [Test]
        public void PausedTick_DoesNotCount()
        {
            QuestSystem quests = Make(GymJson);
            int timedOut = 0;
            quests.StepTimedOut += q => timedOut++;
            quests.StartQuest("q_gym");

            // 会話・クエストログ・写真モード・屋内への移動中は操作できないので数えない。
            quests.Tick(100f, true);

            Assert.AreEqual(0, timedOut);
            Assert.AreEqual(60f, quests.RemainingSeconds(quests.Find("q_gym")), 1e-4f);
        }

        [Test]
        public void TrackedQuest_PrefersTheRunningChallenge()
        {
            QuestSystem quests = Make(GymJson, LibraryJson);
            quests.StartQuest("q_library");
            quests.StartQuest("q_gym");

            Assert.AreEqual("q_gym", quests.TrackedQuest.Id, "order が大きくても計時中なら残り秒を見せる");

            quests.Tick(61f, false);
            Assert.AreEqual("q_gym", quests.TrackedQuest.Id, "時間切れのあとも再挑戦の案内を見せる（order 順に戻さない）");

            quests.ReportTalk("prof");
            quests.ReportEnter("gym");
            Assert.AreEqual("q_library", quests.TrackedQuest.Id, "達成したら order 順に戻る");
        }

        [Test]
        public void TrackedQuest_RunningChallengeBeatsFailedOne()
        {
            QuestSystem quests = Make(GymJson, LibraryJson, NoGiverJson);
            quests.StartQuest("q_library");
            quests.StartQuest("q_gym");
            quests.Tick(61f, false);
            Assert.AreEqual("q_gym", quests.TrackedQuest.Id);

            quests.StartQuest("q_dash");
            Assert.AreEqual("q_dash", quests.TrackedQuest.Id, "計時中のものが再挑戦待ちより先");
        }

        [Test]
        public void TrackedQuest_AfterRestore_ShowsTheChallengeWaitingForItsGiver()
        {
            QuestSystem quests = Make(GymJson, LibraryJson);
            quests.StartQuest("q_library");
            quests.StartQuest("q_gym");
            QuestProgress saved = quests.Capture();

            QuestSystem loaded = Make(GymJson, LibraryJson);
            loaded.Restore(saved);

            Assert.IsTrue(loaded.Find("q_gym").Steps[0].AwaitingGiver);
            Assert.AreEqual("q_gym", loaded.TrackedQuest.Id, "ロード直後は「教授に話しかけると始まる」を見せる");
        }

        [Test]
        public void EnterTargetWhileNotTiming_RaisesRetryEventOnly()
        {
            QuestSystem quests = Make(GymJson, LibraryJson);
            var retry = new List<string>();
            quests.ChallengeRetryNeeded += q => retry.Add(q.Id);
            quests.StartQuest("q_library");
            quests.StartQuest("q_gym");

            quests.Tick(61f, false);
            quests.ReportEnter("gym");
            Assert.AreEqual(new[] { "q_gym" }, retry.ToArray(), "時間切れのあとに体育館へ入ったら再挑戦の案内を出す");
            Assert.IsFalse(quests.IsCompleted("q_gym"));

            quests.ReportEnter("library");
            Assert.AreEqual(1, retry.Count, "別の建物では出さない");

            quests.ReportTalk("prof");
            quests.ReportEnter("gym");
            Assert.AreEqual(1, retry.Count, "計時中に入ったら達成で、案内は出さない");
            Assert.IsTrue(quests.IsCompleted("q_gym"));

            quests.ReportEnter("gym");
            Assert.AreEqual(1, retry.Count, "達成後は出さない");
        }

        [Test]
        public void UntimedQuest_HasNoCountdown()
        {
            QuestSystem quests = Make(LibraryJson);
            int started = 0;
            quests.TimerStarted += q => started++;
            quests.StartQuest("q_library");

            Assert.AreEqual(0, started);
            Assert.AreEqual(-1f, quests.RemainingSeconds(quests.Find("q_library")), 1e-4f);
            Assert.IsFalse(quests.Find("q_library").Steps[0].AwaitingGiver);
        }

        [Test]
        public void Restore_WaitsForTheGiverInsteadOfCountingFromLoad()
        {
            QuestSystem before = Make(GymJson);
            before.StartQuest("q_gym");
            before.Tick(10f, false);
            QuestProgress progress = before.Capture();

            QuestSystem after = Make(GymJson);
            int timedOut = 0;
            after.StepTimedOut += q => timedOut++;
            after.Restore(progress);

            QuestStep step = GymStep(after);
            Assert.IsTrue(after.IsActive("q_gym"));
            Assert.AreEqual(ChallengeState.Idle, step.Timer.State);
            Assert.IsTrue(step.AwaitingGiver);
            Assert.AreEqual(-1f, after.RemainingSeconds(after.Find("q_gym")), 1e-4f);

            after.Tick(120f, false);
            Assert.AreEqual(0, timedOut, "読み込んだだけでは時間切れにしない");

            after.ReportTalk("prof");
            Assert.AreEqual(60f, after.RemainingSeconds(after.Find("q_gym")), 1e-4f);
        }

        [Test]
        public void Restore_TimedStepWithoutGiver_StartsCountingAgain()
        {
            QuestSystem before = Make(NoGiverJson);
            before.StartQuest("q_dash");
            before.Tick(20f, false);
            QuestProgress progress = before.Capture();

            QuestSystem after = Make(NoGiverJson);
            after.Restore(progress);

            Assert.AreEqual(30f, after.RemainingSeconds(after.Find("q_dash")), 1e-4f, "依頼主がいなければ読み込んだ時点から数える");
        }

        [Test]
        public void Data_TimedQuestsCanBeRetriedFromTheirGiver()
        {
            // 実データ: 制限時間つきのクエストには依頼主がいて、その NPC に受注中も話しかけられる（再挑戦の入口）。
            string[] files = Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json");
            int timedQuests = 0;

            foreach (string file in files)
            {
                QuestData quest = QuestData.FromJson(MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                Assert.IsNotNull(quest, file);

                bool timed = false;
                for (int i = 0; i < quest.Steps.Count; i++)
                {
                    timed |= quest.Steps[i].IsTimed;
                }

                if (!timed)
                {
                    continue;
                }

                timedQuests++;
                Assert.IsFalse(string.IsNullOrEmpty(quest.Giver), quest.Id + " は制限時間つきなのに giver が無い");

                string dialoguePath = Path.Combine(DataRoot, "Dialogue", quest.Giver + ".json");
                Assert.IsTrue(File.Exists(dialoguePath), quest.Id + " の giver の会話が無い: " + dialoguePath);

                var dialogue = MiniJson.Deserialize(File.ReadAllText(dialoguePath)) as Dictionary<string, object>;
                bool starts = false;
                bool whileActive = false;
                List<object> topics = MiniJson.GetArray(dialogue, "topics");
                for (int i = 0; i < topics.Count; i++)
                {
                    if (topics[i] is Dictionary<string, object> topic)
                    {
                        starts |= MiniJson.GetString(topic, "startsQuest") == quest.Id;
                        whileActive |= MiniJson.GetString(topic, "requiresActiveQuest") == quest.Id;
                    }
                }

                Assert.IsTrue(starts, quest.Giver + " の会話に " + quest.Id + " を始める話題が無い");
                Assert.IsTrue(whileActive, quest.Giver + " の会話に " + quest.Id + " の受注中の話題が無い（再挑戦できない）");
            }

            Assert.Greater(timedQuests, 0, "q_gym が制限時間つきで読めていない");
        }

        [Test]
        public void Data_ChallengeStringsExistAndFormat()
        {
            foreach (string locale in new[] { "ja", "en" })
            {
                var root = MiniJson.Deserialize(File.ReadAllText(Path.Combine(DataRoot, "Localization", locale + ".json")))
                    as Dictionary<string, object>;
                Dictionary<string, object> strings = MiniJson.GetObject(root, "strings");
                Assert.IsNotNull(strings, locale);

                foreach (string key in new[]
                         {
                             "ui.hud.time_remaining", "ui.hud.time_up", "ui.hud.challenge_start",
                             "ui.hud.challenge_cleared", "ui.hud.challenge_failed", "ui.hud.challenge_ready",
                             "ui.hud.challenge_retry", "ui.hud.achievement_unlocked", "ui.npc.prof",
                             "ach.ach_sprinter.name"
                         })
                {
                    Assert.IsTrue(strings.ContainsKey(key), locale + " に " + key + " が無い");
                    string text = MiniJson.GetString(strings, key);
                    Assert.DoesNotThrow(() => string.Format(text, 12.5f), locale + " の " + key + " の書式が壊れている");
                }
            }
        }

        [Test]
        public void Data_AchievementRewardsHaveNames()
        {
            var root = MiniJson.Deserialize(File.ReadAllText(Path.Combine(DataRoot, "Localization", "ja.json")))
                as Dictionary<string, object>;
            Dictionary<string, object> strings = MiniJson.GetObject(root, "strings");

            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                if (quest == null || quest.RewardType != QuestData.RewardAchievement)
                {
                    continue;
                }

                Assert.IsTrue(strings.ContainsKey("ach." + quest.RewardId + ".name"),
                    quest.Id + " の称号 " + quest.RewardId + " に名前が無い（達成トーストに出せない）");
            }
        }
    }
}
