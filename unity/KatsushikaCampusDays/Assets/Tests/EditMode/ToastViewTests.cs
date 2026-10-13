using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 画面下の一行通知（ToastView）。空の文言は並ばないこと、fade 秒で現れて hold 秒とどまり fade 秒で消えること、
    /// 不透明度が 0〜1 をはみ出さないこと、消し終えたら次へ進むこと。
    /// </summary>
    public sealed class ToastViewTests
    {
        private const float Tolerance = 0.0001f;
        private const float Fade = 0.35f;
        private const float Hold = 2.4f;

        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void Push_IgnoresEmptyMessages()
        {
            _host = new GameObject("Toast");
            ToastView toast = _host.AddComponent<ToastView>();

            toast.Push(null);
            toast.Push(string.Empty);
            Assert.AreEqual(0, toast.PendingCount, "空の通知が並んでいる（何も書いていない帯が出る）");

            toast.Push("図書館を見つけた");
            toast.Push("葉を拾った");
            Assert.AreEqual(2, toast.PendingCount, "続けて来た通知が順番待ちにならない");
        }

        [TestCase(0f, 0f)]
        [TestCase(Fade * 0.5f, 0.5f)]
        [TestCase(Fade, 1f)]
        [TestCase(Fade + Hold * 0.5f, 1f)]
        [TestCase(Fade + Hold, 1f)]
        [TestCase(Fade + Hold + Fade * 0.5f, 0.5f)]
        public void AlphaAt_FadesInHoldsAndFadesOut(float elapsed, float expected)
        {
            Assert.AreEqual(expected, ToastView.AlphaAt(elapsed, Fade, Hold), Tolerance, elapsed + " 秒の不透明度");
        }

        [Test]
        public void AlphaAt_StaysBetweenZeroAndOneUntilItEnds()
        {
            float total = Fade * 2f + Hold;
            for (float elapsed = 0f; !ToastView.IsFinished(elapsed, Fade, Hold); elapsed += 0.01f)
            {
                float alpha = ToastView.AlphaAt(elapsed, Fade, Hold);
                Assert.That(alpha, Is.InRange(0f, 1f), elapsed + " 秒で不透明度がはみ出した");
                Assert.Less(elapsed, total + 0.01f, "終わりの時刻を過ぎても消えない");
            }
        }

        [Test]
        public void IsFinished_AfterFadeInHoldAndFadeOut()
        {
            float total = Fade * 2f + Hold;

            Assert.IsFalse(ToastView.IsFinished(0f, Fade, Hold));
            Assert.IsFalse(ToastView.IsFinished(total - 0.01f, Fade, Hold), "消え切る前に次へ進んでいる");
            Assert.IsTrue(ToastView.IsFinished(total, Fade, Hold), "消え切っても次の通知へ進まない");
            Assert.IsTrue(ToastView.IsFinished(total + 5f, Fade, Hold));
        }
    }
}
