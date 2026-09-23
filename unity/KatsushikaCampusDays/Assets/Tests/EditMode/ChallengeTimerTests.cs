using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>制限時間つきステップの計時（ChallengeTimer）単体の振る舞い。</summary>
    public sealed class ChallengeTimerTests
    {
        [Test]
        public void NewTimer_IsIdle()
        {
            var timer = new ChallengeTimer();

            Assert.AreEqual(ChallengeState.Idle, timer.State);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(timer.HasFailed);
            Assert.IsFalse(timer.Tick(1f), "数え始める前は時間切れにならない");
            Assert.AreEqual(ChallengeState.Idle, timer.State);
        }

        [Test]
        public void Start_RunsFromTheFullLimit()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);

            Assert.AreEqual(ChallengeState.Running, timer.State);
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(60f, timer.Limit, 1e-4f);
            Assert.AreEqual(0f, timer.Elapsed, 1e-4f);
            Assert.AreEqual(60f, timer.Remaining, 1e-4f);

            Assert.IsFalse(timer.Tick(12.5f));
            Assert.AreEqual(47.5f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Start_WithNonPositiveLimit_StaysIdle()
        {
            var timer = new ChallengeTimer();
            timer.Start(0f);
            Assert.AreEqual(ChallengeState.Idle, timer.State);

            timer.Start(-5f);
            Assert.AreEqual(ChallengeState.Idle, timer.State);
            Assert.IsFalse(timer.Tick(100f));
        }

        [Test]
        public void Tick_ReportsTimeoutOnlyOnce()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);

            Assert.IsFalse(timer.Tick(59.5f));
            Assert.IsTrue(timer.Tick(1f), "制限時間を越えたフレームで 1 回だけ true");
            Assert.AreEqual(ChallengeState.Failed, timer.State);
            Assert.IsTrue(timer.HasFailed);
            Assert.AreEqual(0f, timer.Remaining, 1e-4f);
            Assert.AreEqual(60f, timer.Elapsed, 1e-4f, "経過は制限時間で止まる");

            Assert.IsFalse(timer.Tick(1f), "2 回目以降は true にならない");
            Assert.IsFalse(timer.Tick(120f));
        }

        [Test]
        public void Tick_ExactlyAtLimit_Fails()
        {
            var timer = new ChallengeTimer();
            timer.Start(10f);

            Assert.IsTrue(timer.Tick(10f));
            Assert.IsTrue(timer.HasFailed);
        }

        [Test]
        public void AfterTimeout_DoesNotRearmByItself()
        {
            // 以前の不具合: 時間切れのあと勝手に数え直していた。Failed は Start を呼ぶまで Failed のまま。
            var timer = new ChallengeTimer();
            timer.Start(60f);
            timer.Tick(61f);

            for (int i = 0; i < 600; i++)
            {
                timer.Tick(0.5f);
            }

            Assert.AreEqual(ChallengeState.Failed, timer.State);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Tick_IgnoresZeroNegativeAndNaN()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);

            Assert.IsFalse(timer.Tick(0f));
            Assert.IsFalse(timer.Tick(-3f));
            Assert.IsFalse(timer.Tick(float.NaN));

            Assert.AreEqual(ChallengeState.Running, timer.State);
            Assert.AreEqual(60f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Clear_OnlyWhileRunning()
        {
            var timer = new ChallengeTimer();
            Assert.IsFalse(timer.Clear(), "数える前は達成にできない");

            timer.Start(60f);
            timer.Tick(20f);
            Assert.IsTrue(timer.Clear());
            Assert.AreEqual(ChallengeState.Cleared, timer.State);
            Assert.AreEqual(40f, timer.Remaining, 1e-4f, "達成した瞬間の残りを覚えている");

            Assert.IsFalse(timer.Tick(100f), "達成のあとは時間切れにならない");
            Assert.AreEqual(ChallengeState.Cleared, timer.State);
            Assert.IsFalse(timer.Clear(), "2 回目の達成は無い");
        }

        [Test]
        public void Clear_AfterTimeout_IsRefused()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);
            timer.Tick(60f);

            Assert.IsFalse(timer.Clear(), "時間切れのあとに着いても達成にならない");
            Assert.AreEqual(ChallengeState.Failed, timer.State);
        }

        [Test]
        public void Start_AfterTimeout_RetriesFromTheFullLimit()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);
            timer.Tick(60f);

            timer.Start(60f);

            Assert.AreEqual(ChallengeState.Running, timer.State);
            Assert.AreEqual(60f, timer.Remaining, 1e-4f);
            Assert.IsFalse(timer.Tick(30f));
            Assert.AreEqual(30f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Reset_ReturnsToIdle()
        {
            var timer = new ChallengeTimer();
            timer.Start(60f);
            timer.Tick(10f);

            timer.Reset();

            Assert.AreEqual(ChallengeState.Idle, timer.State);
            Assert.AreEqual(0f, timer.Elapsed, 1e-4f);
            Assert.IsFalse(timer.Tick(100f));
        }
    }
}
