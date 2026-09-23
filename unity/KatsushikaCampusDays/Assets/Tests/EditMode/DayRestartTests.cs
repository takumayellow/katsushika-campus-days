using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>「もう一日歩く」で翌朝から歩き直すときの判定（#16）。</summary>
    public sealed class DayRestartTests
    {
        [Test]
        public void DayStartHour_IsMorningAndBeforeDayEnd()
        {
            // Ending/result.json の day_end_hour は 20 時。朝に戻した直後にまたリザルトが出てはいけない。
            Assert.AreEqual(8.5f, DayRestart.DayStartHour, 0.001f);
            Assert.Less(DayRestart.DayStartHour, 20f);
        }

        [Test]
        public void NextDay_CountsUpFromTheFirstDay()
        {
            Assert.AreEqual(2, DayRestart.NextDay(DayRestart.FirstDay));
            Assert.AreEqual(3, DayRestart.NextDay(2));
            Assert.AreEqual(8, DayRestart.NextDay(7));
        }

        [Test]
        public void NextDay_TreatsUncountedAsTheFirstDay()
        {
            // 数え始める前（0）や壊れた値でも「2 日目の朝」から始める。
            Assert.AreEqual(2, DayRestart.NextDay(0));
            Assert.AreEqual(2, DayRestart.NextDay(-5));
        }

        [Test]
        public void NeedsInteriorExit_OnlyWhenInsideWithALoader()
        {
            Assert.IsTrue(DayRestart.NeedsInteriorExit(true, true));
            Assert.IsFalse(DayRestart.NeedsInteriorExit(true, false), "外にいるなら出入り係は呼ばない");
            Assert.IsFalse(DayRestart.NeedsInteriorExit(false, true), "出入り係がいないシーンでは待たない");
            Assert.IsFalse(DayRestart.NeedsInteriorExit(false, false));
        }

        [Test]
        public void ReturnPoint_UsesTheRememberedSpawn()
        {
            var spawn = new Vector3(13.2f, 0.15f, -33.4f);
            var current = new Vector3(1250f, 0.1f, 40f);

            Assert.AreEqual(spawn, DayRestart.ReturnPoint(true, spawn, current));
            Assert.AreEqual(150f, DayRestart.ReturnYaw(true, 150f, 12f), 0.001f);
        }

        [Test]
        public void ReturnPoint_KeepsThePlayerWhereTheyAreWithoutASpawn()
        {
            // スポーンを覚えられなかったとき（プレイヤーが見つからない）に、原点へ飛ばしてしまわない。
            var current = new Vector3(20f, 0.3f, -18f);

            Assert.AreEqual(current, DayRestart.ReturnPoint(false, Vector3.zero, current));
            Assert.AreEqual(12f, DayRestart.ReturnYaw(false, 150f, 12f), 0.001f);
        }

        [Test]
        public void TimerAction_KeepsStepsThatAreNotCounting()
        {
            // 時間制限の無いステップ。
            Assert.AreEqual(DayTimerAction.Keep, DayRestart.TimerAction(false, false, false, false, true));

            // 依頼主に話しかけるのを待っている（Idle）。翌朝も待たせたままでよい。
            Assert.AreEqual(DayTimerAction.Keep, DayRestart.TimerAction(true, false, false, false, true));

            // 済んだ挑戦は蒸し返さない。
            Assert.AreEqual(DayTimerAction.Keep, DayRestart.TimerAction(true, true, false, false, true));
        }

        [Test]
        public void TimerAction_ResetsRunningAndFailedChallengesWithAGiver()
        {
            // 体育館まで 60 秒（q_gym）。依頼主は prof なので、朝は数える前に戻して再挑戦を待つ。
            Assert.AreEqual(DayTimerAction.Reset, DayRestart.TimerAction(true, false, true, false, true));
            Assert.AreEqual(DayTimerAction.Reset, DayRestart.TimerAction(true, false, false, true, true));
        }

        [Test]
        public void TimerAction_RestartsChallengesWithoutAGiver()
        {
            // 依頼主がいないと Idle からは二度と数え始められないので、その場で数え直す。
            Assert.AreEqual(DayTimerAction.Restart, DayRestart.TimerAction(true, false, true, false, false));
            Assert.AreEqual(DayTimerAction.Restart, DayRestart.TimerAction(true, false, false, true, false));
        }

        [Test]
        public void ChallengeTimer_ResetGoesBackToWaitingForTheGiver()
        {
            // 判定どおりに戻したとき、ChallengeTimer が「数える前」に戻ること。
            var timer = new ChallengeTimer();
            timer.Start(60f);
            timer.Tick(61f);
            Assert.IsTrue(timer.HasFailed);

            Assert.AreEqual(
                DayTimerAction.Reset,
                DayRestart.TimerAction(true, false, timer.IsRunning, timer.HasFailed, true));

            timer.Reset();
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(timer.HasFailed);
            Assert.AreEqual(0f, timer.Elapsed, 0.001f);
        }
    }
}
