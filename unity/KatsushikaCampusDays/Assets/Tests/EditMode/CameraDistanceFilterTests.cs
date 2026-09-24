using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 遮蔽で寄せたカメラ距離の出し方 (#12)。寄るのは即時、戻るのは待ってから滑らかに。
    /// </summary>
    public sealed class CameraDistanceFilterTests
    {
        private const float Frame = 1f / 60f;

        [Test]
        public void FirstStep_UsesTheAllowedDistance()
        {
            var filter = new CameraDistanceFilter();
            Assert.AreEqual(2f, filter.Step(4f, 2f, Frame), 1e-6f);
            Assert.IsTrue(filter.IsRecovering, "遮られた状態で始まったら戻る途中として扱う");
        }

        [Test]
        public void PullIn_HappensInTheSameFrame()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);

            Assert.AreEqual(1.5f, filter.Step(4f, 1.5f, Frame), 1e-6f, "壁の中を 1 フレームも写さない");
            Assert.AreEqual(0.6f, filter.Step(4f, 0.6f, 0f), 1e-6f, "ポーズ中（deltaTime 0）でも寄る");
        }

        [Test]
        public void Recovery_WaitsForTheHoldThenEasesBack()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);
            filter.Step(4f, 1.5f, Frame);

            // 待ち 0.2 s の間は寄せたまま。
            Assert.AreEqual(1.5f, filter.Step(4f, 4f, 0.15f), 1e-6f);
            Assert.AreEqual(1.5f, filter.Step(4f, 4f, 0.1f), 1e-6f);

            // 待ちが明けたら時定数 0.3 s で戻る。いきなり 4 m には跳ばない。
            float expected = 1.5f + 2.5f * (1f - Mathf.Exp(-0.1f / 0.3f));
            float first = filter.Step(4f, 4f, 0.1f);
            Assert.AreEqual(expected, first, 1e-4f);
            Assert.Less(first, 4f);

            float elapsed = 0f;
            float previous = first;
            while (elapsed < 2.5f)
            {
                float next = filter.Step(4f, 4f, Frame);
                Assert.GreaterOrEqual(next, previous - 1e-6f, "戻る途中で寄り直さない");
                previous = next;
                elapsed += Frame;
            }

            Assert.AreEqual(4f, previous, 1e-6f, "2.5 s あれば戻り切る");
            Assert.IsFalse(filter.IsRecovering);
        }

        [Test]
        public void Recovery_PausedTimeDoesNotAdvance()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);
            filter.Step(4f, 1.5f, Frame);
            filter.Step(4f, 4f, 0.3f);

            Assert.AreEqual(1.5f, filter.Step(4f, 4f, 0f), 1e-6f, "deltaTime 0 では戻らない");
        }

        [Test]
        public void NewObstructionWhileRecovering_PullsInAndRestartsTheHold()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);
            filter.Step(4f, 1.5f, Frame);
            filter.Step(4f, 4f, 0.25f);
            float recovering = filter.Step(4f, 4f, 0.2f);
            Assert.Greater(recovering, 1.5f);

            Assert.AreEqual(1.2f, filter.Step(4f, 1.2f, Frame), 1e-6f);
            Assert.AreEqual(1.2f, filter.Step(4f, 4f, 0.1f), 1e-6f, "寄り直したら待ちも取り直す");
        }

        [Test]
        public void ZoomOutWithoutObstruction_FollowsDirectly()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);

            Assert.AreEqual(6f, filter.Step(6f, 6f, Frame), 1e-6f, "ホイールのズームは待たせない");
            Assert.AreEqual(3f, filter.Step(3f, 3f, Frame), 1e-6f);
            Assert.IsFalse(filter.IsRecovering);
        }

        [Test]
        public void AllowedIsClampedToDesired()
        {
            var filter = new CameraDistanceFilter();
            Assert.AreEqual(3f, filter.Step(3f, 5f, Frame), 1e-6f);
            Assert.AreEqual(0f, filter.Step(3f, -2f, Frame), 1e-6f);
        }

        [Test]
        public void ResetOrNegativeDeltaTime_JumpsToTheAllowedDistance()
        {
            var filter = new CameraDistanceFilter();
            filter.Step(4f, 4f, Frame);
            filter.Step(4f, 1f, Frame);

            filter.Reset();
            Assert.AreEqual(4f, filter.Step(4f, 4f, Frame), 1e-6f, "ワープ直後は待たずに望む距離へ");

            filter.Step(4f, 1f, Frame);
            Assert.AreEqual(4f, filter.Step(4f, 4f, -1f), 1e-6f, "deltaTime が負ならリセット");
            Assert.IsFalse(filter.IsRecovering);
        }
    }
}
