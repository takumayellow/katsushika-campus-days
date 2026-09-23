using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// NPC / プレイヤーの Animator に渡す Speed と GaitRate（#13 脚が半分しか振れない、#43 足が滑る）。
    /// 歩きのクリップは 2.6 m/s ぶんの歩幅と歩調で作ってあるので、
    /// 「ブレンド値 × 再生速度 = 実際の速さ」になっていれば足は滑らない。
    /// </summary>
    public sealed class NPCWanderTests
    {
        [Test]
        public void SolveGait_AtWalkSpeed_PlaysWalkClipAtFullRate()
        {
            PlayerAnimatorDriver.SolveGait(PlayerController.DefaultWalkSpeed, out float blend, out float rate);

            Assert.AreEqual(2.6f, blend, 1e-5f);
            Assert.AreEqual(1f, rate, 1e-5f);
        }

        [Test]
        public void SolveGait_AboveWalkSpeed_PassesSpeedThrough()
        {
            PlayerAnimatorDriver.SolveGait(5.4f, out float blend, out float rate);

            Assert.AreEqual(5.4f, blend, 1e-5f);
            Assert.AreEqual(1f, rate, 1e-5f);
        }

        [Test]
        public void SolveGait_AtNpcSpeed_ShortensStrideAndCadenceEqually()
        {
            PlayerAnimatorDriver.SolveGait(1.1f, out float blend, out float rate);

            // 歩幅も歩調も sqrt(1.1 / 2.6) = 0.65 倍。待機との中間（0.42 倍の歩幅）にはしない。
            Assert.AreEqual(Mathf.Sqrt(1.1f * 2.6f), blend, 1e-4f);
            Assert.AreEqual(Mathf.Sqrt(1.1f / 2.6f), rate, 1e-4f);
            Assert.Greater(blend, 1.1f);
            Assert.Less(blend, PlayerController.DefaultWalkSpeed);
        }

        [Test]
        public void SolveGait_NeverSlips()
        {
            for (float speed = 0.1f; speed <= 6f; speed += 0.1f)
            {
                PlayerAnimatorDriver.SolveGait(speed, out float blend, out float rate);
                float ground = speed <= PlayerController.DefaultWalkSpeed
                    ? blend * rate            // 歩き以下はクリップの歩幅 × 再生速度
                    : blend;                  // 歩き以上はしきい値どおり
                Assert.AreEqual(speed, ground, 1e-3f, "speed=" + speed);
            }
        }

        [Test]
        public void SolveGait_Stopped_DoesNotBlowUp()
        {
            PlayerAnimatorDriver.SolveGait(0f, out float blend, out float rate);

            Assert.AreEqual(0f, blend, 1e-6f);
            Assert.AreEqual(1f, rate, 1e-6f);

            PlayerAnimatorDriver.SolveGait(-3f, out float back, out float backRate);
            Assert.AreEqual(0f, back, 1e-6f);
            Assert.AreEqual(1f, backRate, 1e-6f);
        }

        [Test]
        public void AnimatorSpeed_UsesTheSameSolver()
        {
            PlayerAnimatorDriver.SolveGait(1.1f, out float blend, out float _);

            Assert.AreEqual(blend, NPCWander.AnimatorSpeed(1.1f, 1.1f), 1e-5f);
        }
    }
}
