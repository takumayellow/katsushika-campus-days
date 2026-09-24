using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// フォトモードの自由カメラの 1 フレームの移動量 (#12)。
    /// 前後左右は向きのヨーに沿った水平、上下は真上。斜めや上下を足しても速さは変わらない。
    /// </summary>
    public sealed class PhotoFreeCameraTests
    {
        private const float Speed = PhotoFreeCamera.MoveSpeed;
        private const float DeltaTime = 0.1f;

        [Test]
        public void MotionFor_ForwardFollowsTheYaw()
        {
            AssertNear(new Vector3(0f, 0f, Speed * DeltaTime),
                PhotoFreeCamera.MotionFor(0f, new Vector2(0f, 1f), 0f, Speed, DeltaTime));
            AssertNear(new Vector3(Speed * DeltaTime, 0f, 0f),
                PhotoFreeCamera.MotionFor(90f, new Vector2(0f, 1f), 0f, Speed, DeltaTime));
            AssertNear(new Vector3(0f, 0f, Speed * DeltaTime),
                PhotoFreeCamera.MotionFor(90f, new Vector2(-1f, 0f), 0f, Speed, DeltaTime), "90 度向いたときの左は +Z");
        }

        [Test]
        public void MotionFor_VerticalGoesStraightUpOrDown()
        {
            AssertNear(new Vector3(0f, Speed * DeltaTime, 0f),
                PhotoFreeCamera.MotionFor(45f, Vector2.zero, 1f, Speed, DeltaTime));
            AssertNear(new Vector3(0f, -Speed * DeltaTime, 0f),
                PhotoFreeCamera.MotionFor(45f, Vector2.zero, -1f, Speed, DeltaTime));
        }

        [Test]
        public void MotionFor_DiagonalAndVerticalTogetherKeepTheSpeed()
        {
            Vector3 motion = PhotoFreeCamera.MotionFor(30f, new Vector2(0.7071f, 0.7071f), 1f, Speed, DeltaTime);

            Assert.AreEqual(Speed * DeltaTime, motion.magnitude, 1e-4f);
            Assert.Greater(motion.y, 0f);
        }

        [Test]
        public void MotionFor_SmallInputMovesSlower()
        {
            Vector3 motion = PhotoFreeCamera.MotionFor(0f, new Vector2(0f, 0.5f), 0f, Speed, DeltaTime);

            Assert.AreEqual(0.5f * Speed * DeltaTime, motion.magnitude, 1e-4f, "スティックを浅く倒したら遅く動く");
        }

        [Test]
        public void MotionFor_NoTimeOrNoSpeedDoesNotMove()
        {
            Assert.AreEqual(Vector3.zero, PhotoFreeCamera.MotionFor(0f, Vector2.up, 1f, Speed, 0f));
            Assert.AreEqual(Vector3.zero, PhotoFreeCamera.MotionFor(0f, Vector2.up, 1f, Speed, -0.1f));
            Assert.AreEqual(Vector3.zero, PhotoFreeCamera.MotionFor(0f, Vector2.up, 1f, 0f, DeltaTime));
            Assert.AreEqual(Vector3.zero, PhotoFreeCamera.MotionFor(0f, Vector2.zero, 0f, Speed, DeltaTime));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f,
                (message != null ? message + ": " : string.Empty) + "expected " + expected + " but was " + actual);
        }
    }
}
