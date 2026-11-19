using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 本物のキャンパスで、会話を読んでいるあいだ時計が止まること、会話で flag を立てる相手
    /// （7F の白板・306 の PC）をミニマップの輪が指せることを確かめる（#410）。
    /// </summary>
    public sealed class DialogueClockPlayTests : SceneTestBase
    {
        private const string LectureBuilding = "lecture";

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 会話中は時計が止まり閉じると進む()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            DialogueSystem dialogue = DialogueSystem.Instance;
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            Assert.IsNotNull(dialogue, "DialogueSystem が無い");
            cycle.Paused = false;

            CloseAll(dialogue);
            Assert.IsTrue(dialogue.TalkTo("prof"), "教授と話し始められない");
            float before = cycle.Hours;
            yield return PlayModeScenes.Advance(60, 4f);
            Assert.IsTrue(dialogue.IsPlaying, "会話がひとりでに閉じた");
            Assert.AreEqual(before, cycle.Hours, 1e-5f, "会話中に時計が進んだ");

            CloseAll(dialogue);
            yield return PlayModeScenes.Advance(60, 4f);
            Assert.Greater(cycle.Hours, before, "会話を閉じても時計が進まない");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 会話でflagを立てる相手を輪が指す()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "PlayerController が無い");
            Vector3 from = player.transform.position;

            Vector3 entrance = Vector3.zero;
            float best = float.MaxValue;
            foreach (EntranceTrigger trigger in Object.FindObjectsByType<EntranceTrigger>())
            {
                float distance = (trigger.transform.position - from).sqrMagnitude;
                if (trigger.BuildingId == LectureBuilding && distance < best)
                {
                    best = distance;
                    entrance = trigger.transform.position;
                }
            }

            Assert.Less(best, float.MaxValue, "講義棟の入口が無い");
            QuestObjectiveLocator.Invalidate();
            foreach (string flag in new[] { "hello_world_reply", "first_program_ran" })
            {
                var step = new QuestStep { Kind = QuestStepKind.Flag, Target = flag };
                Assert.IsTrue(QuestObjectiveLocator.TryLocate(step, from, out Vector3 goal), flag + " の行き先が見つからない");
                Assert.Less((goal - entrance).magnitude, 0.01f,
                    flag + " は外からは講義棟の入口を指すはず: " + goal + " / 入口 " + entrance);
            }
        }

        /// <summary>開いている会話を閉じる。選択肢が出たら最初を選ぶ。</summary>
        private static void CloseAll(DialogueSystem dialogue)
        {
            for (int i = 0; i < 200 && dialogue.IsPlaying; i++)
            {
                if (dialogue.IsChoosing)
                {
                    dialogue.Choose(0);
                }
                else
                {
                    dialogue.Advance();
                }
            }

            Assert.IsFalse(dialogue.IsPlaying, "会話が閉じない");
        }
    }
}
