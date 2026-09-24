using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 一日を早送りしても止まらない (#67)。朝から夜の手前まで時計を刻んで進め、一日の終わりにリザルトが出て、
    /// 「もう一日歩く」で 2 日目の朝に戻り、時計がまた進む。途中で例外もエラーのログも出ない。
    /// </summary>
    public sealed class DayFastForwardTests : SceneTestBase
    {
        /// <summary>時計を進める刻み（時間）。チャイムやクエストの時間の判定を飛ばさないよう、1 時間を 4 回に分ける。</summary>
        private const float HourStep = 0.25f;

        /// <summary>早送りを止める時刻。result.json の day_end_hour（20:00）の手前で止め、最後は時計に任せて越えさせる。</summary>
        private const float StopBeforeDayEnd = 19.98f;

        /// <summary>リザルトが出るまで・朝に戻るまでを待つ上限（実時間の秒）。</summary>
        private const float StepTimeoutSeconds = 20f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_FastForwardToDayEnd_ShowsResultAndStartsTheNextMorning()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            GameManager manager = GameManager.Instance;
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            DayEndEvaluator evaluator = Object.FindAnyObjectByType<DayEndEvaluator>();
            Assert.IsNotNull(cycle, "キャンパスに DayNightCycle が無い");
            Assert.IsNotNull(evaluator, "キャンパスに DayEndEvaluator が無い");
            Assert.IsNotNull(evaluator.Screen, "DayEndEvaluator に ResultScreen が差し込まれていない（SceneBuilder）");
            Assert.AreEqual(DayRestart.FirstDay, manager.DayNumber, "「はじめから」なのに 1 日目でない");
            Assert.IsTrue(manager.HasEnteredCampus, "DayNightCycle.Start が入場済みにしていない");

            for (float hours = cycle.Hours + HourStep; hours < StopBeforeDayEnd; hours += HourStep)
            {
                cycle.SetHours(hours);
                yield return null;
                Assert.IsFalse(evaluator.Screen.IsOpen,
                    cycle.Hours.ToString("F2") + " 時でもうリザルトが出た（day_end_hour より前）");
            }

            cycle.SetHours(StopBeforeDayEnd);
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!evaluator.Screen.IsOpen && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(evaluator.Screen.IsOpen,
                StepTimeoutSeconds + " 秒待ってもリザルトが出ない。時計 " + cycle.Hours.ToString("F2") +
                " 時 / GameManager " + manager.GameTimeHours.ToString("F2") +
                " 時 / 入場済み " + manager.HasEnteredCampus +
                " / 操作の封鎖 " + KCDInput.GameplayBlocked + " / timeScale " + Time.timeScale);
            Assert.AreEqual(0f, Time.timeScale, "リザルトの間に時間が止まっていない");

            yield return null;
            evaluator.Screen.Choose(0);

            int nextDay = DayRestart.NextDay(DayRestart.FirstDay);
            deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while ((manager.DayNumber != nextDay || KCDInput.GameplayBlocked) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(nextDay, manager.DayNumber, "「もう一日歩く」を選んでも次の日にならない");
            Assert.IsFalse(KCDInput.GameplayBlocked, "朝に戻ったあとも操作が封鎖されたまま（暗転が明けない）");
            Assert.IsFalse(evaluator.Screen.IsOpen, "朝に戻ったのにリザルトが開いたまま");
            Assert.AreEqual(1f, Time.timeScale, "朝に戻ったのに時間が止まったまま");
            Assert.AreEqual(DayRestart.DayStartHour, manager.GameTimeHours, 0.1f, "朝の時刻に戻っていない");

            float morning = cycle.Hours;
            yield return PlayModeScenes.Advance(30, 1f);
            Assert.Greater(cycle.Hours, morning, "2 日目の朝から時計が進まない");
            Assert.IsFalse(evaluator.Screen.IsOpen, "朝に戻った直後にまたリザルトが出た");

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            WorldBounds bounds = Object.FindAnyObjectByType<WorldBounds>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsNotNull(bounds, "キャンパスに WorldBounds が無い");
            Vector3 position = player.transform.position;
            Assert.IsTrue(WorldBounds.IsWithin(position, bounds.Area) && position.y > bounds.KillY,
                "朝に戻した位置 " + position.ToString("F2") + " が WorldBounds " + bounds.Area + " の外");

            AssertNoErrors("一日の早送り");
        }
    }
}
