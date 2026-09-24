using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 画面を出したフレームの入力を捨てる判定（#6）。
    /// タイトルで押した Enter をキャラクター選択が同じフレームで拾うと、選べないまま入場してしまう。
    /// </summary>
    public sealed class ScreenActivationGuardTests
    {
        [Test]
        public void IgnoresInput_OnTheFrameTheScreenAppears()
        {
            Assert.IsTrue(KCDInput.IgnoresInput(120, 120), "出したフレームの Enter は捨てる");
        }

        [Test]
        public void AcceptsInput_FromTheNextFrame()
        {
            Assert.IsFalse(KCDInput.IgnoresInput(120, 121), "次のフレームからは操作できる");
            Assert.IsFalse(KCDInput.IgnoresInput(120, 600));
        }

        [Test]
        public void IgnoresInput_WhileTheScreenIsNotShown()
        {
            Assert.AreEqual(-1, KCDInput.NoFrame);
            Assert.IsTrue(KCDInput.IgnoresInput(KCDInput.NoFrame, 0));
            Assert.IsTrue(KCDInput.IgnoresInput(KCDInput.NoFrame, 999));
        }

        [Test]
        public void IgnoresInput_WhenTheFrameCounterDoesNotAdvance()
        {
            // 同じフレームで何度 Update が走っても決定しない。
            Assert.IsTrue(KCDInput.IgnoresInput(120, 119));
        }
    }
}
