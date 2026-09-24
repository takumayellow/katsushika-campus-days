using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    public sealed class PlayerControllerTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void AchievedPlanarSpeed_FreeMoveMatchesCommanded()
        {
            var velocity = new Vector3(3f, 0f, 4f);
            float speed = PlayerController.AchievedPlanarSpeed(velocity, velocity * Dt, Dt);
            Assert.AreEqual(5f, speed, 1e-4f);
        }

        [Test]
        public void AchievedPlanarSpeed_HeadOnWallIsZero()
        {
            float speed = PlayerController.AchievedPlanarSpeed(new Vector3(0f, 0f, 5.4f), Vector3.zero, Dt);
            Assert.AreEqual(0f, speed, 1e-6f, "壁に正面から当たったらその場で走り続けない");
        }

        [Test]
        public void AchievedPlanarSpeed_DiagonalWallGivesSlideComponent()
        {
            // +Z 向きの壁に 45 度で当たると、壁に沿った X 成分だけ進む。
            Vector3 velocity = new Vector3(1f, 0f, 1f).normalized * 4f;
            var slide = new Vector3(velocity.x, 0f, 0f) * Dt;
            float speed = PlayerController.AchievedPlanarSpeed(velocity, slide, Dt);
            Assert.AreEqual(4f * Mathf.Sqrt(0.5f), speed, 1e-4f);
        }

        [Test]
        public void AchievedPlanarSpeed_IgnoresVerticalAndClampsToCommanded()
        {
            var velocity = new Vector3(0f, 0f, 2.6f);

            // 段差を上がった分の縦移動は数えない。
            float climbing = PlayerController.AchievedPlanarSpeed(velocity, new Vector3(0f, 0.3f, 2.6f * Dt), Dt);
            Assert.AreEqual(2.6f, climbing, 1e-4f);

            // 入力より大きく動いても入力の速さを超えない。
            float pushed = PlayerController.AchievedPlanarSpeed(velocity, new Vector3(0.5f, 0f, 0f), Dt);
            Assert.AreEqual(2.6f, pushed, 1e-4f);
        }

        [Test]
        public void AchievedPlanarSpeed_NonPositiveDeltaTimeFallsBackToCommanded()
        {
            var velocity = new Vector3(0f, -3f, 2f);
            Assert.AreEqual(2f, PlayerController.AchievedPlanarSpeed(velocity, Vector3.zero, 0f), 1e-6f);
            Assert.AreEqual(2f, PlayerController.AchievedPlanarSpeed(velocity, Vector3.one, -1f), 1e-6f);
        }

        // --- 接地の吸い付き（#30 地面の継ぎ目でガタガタする）---

        /// <summary>接地中の 1 フレームの下り幅（m）。重力だけのときと吸い付きを入れたときで比べる。</summary>
        private static float DropPerFrame(float verticalVelocity, bool wasGrounded, float deltaTime)
        {
            return -PlayerController.SnappedVerticalMotion(verticalVelocity, wasGrounded, deltaTime) * deltaTime;
        }

        [Test]
        public void SnappedVerticalMotion_GroundedDropsGroundSnapPerFrameAtAnyFrameRate()
        {
            // 接地中の縦速度は _groundStick (-2.5) に重力 (-22) を 1 フレーム分足した値。
            foreach (float dt in new[] { 1f / 60f, 1f / 144f, 1f / 240f })
            {
                float velocity = -2.5f - 22f * dt;
                Assert.AreEqual(PlayerController.GroundSnap, DropPerFrame(velocity, true, dt), 1e-5f,
                    "fps が変わっても 1 フレームで下りられる段差は同じ (dt=" + dt + ")");
            }

            // 30 fps 以下では重力と貼り付きだけで GroundSnap より下りるので、そのまま（遅くしない）。
            const float Dt30 = 1f / 30f;
            Assert.GreaterOrEqual(DropPerFrame(-2.5f - 22f * Dt30, true, Dt30), PlayerController.GroundSnap);
        }

        [Test]
        public void SnappedVerticalMotion_GroundSnapCoversEveryGroundLayerStep()
        {
            // blender/kcd_lib/site.py の高さレイヤ: 外周 0.000 〜 モール 0.021。一番大きい段差は 2.1 cm。
            // 入口の石張り (kcd_lib/entrances.py APRON_Z 0.12) から芝 0.006 への 11.4 cm は吸い付ききらないが、
            // 残りは 1.4 cm で skinWidth (0.028) の内側に入るので接地は外れない。
            const float LargestGroundLayerStep = 0.021f;
            Assert.Greater(PlayerController.GroundSnap, LargestGroundLayerStep);

            const float SkinWidth = 0.028f;
            const float ApronStep = 0.114f;
            Assert.Less(ApronStep - PlayerController.GroundSnap, SkinWidth);

            // 意図した段差（水盤の縁石 0.36）は吸い付かせない。
            Assert.Less(PlayerController.GroundSnap, 0.36f);
        }

        [Test]
        public void SnappedVerticalMotion_WithoutSnapGravityAloneIsTooSlowAt240Fps()
        {
            // 吸い付きが無いと 240 fps では 1 フレームに 1.2 cm しか下りられず、
            // 2.1 cm の段でも一瞬浮いて偽のジャンプと着地音が出ていた。
            const float Dt240 = 1f / 240f;
            float velocity = -2.5f - 22f * Dt240;
            Assert.Less(DropPerFrame(velocity, false, Dt240), 0.021f);
            Assert.GreaterOrEqual(DropPerFrame(velocity, true, Dt240), 0.021f);
        }

        [Test]
        public void SnappedVerticalMotion_AirborneAndRisingAreUntouched()
        {
            // 落下中は速くしない（落下の見た目とタイミングを変えない）。
            Assert.AreEqual(-12f, PlayerController.SnappedVerticalMotion(-12f, false, Dt), 1e-6f);
            // ジャンプ直後の上昇はそのまま。
            Assert.AreEqual(7.4f, PlayerController.SnappedVerticalMotion(7.4f, true, Dt), 1e-6f);
            // deltaTime が 0 以下なら割らない。
            Assert.AreEqual(-2.9f, PlayerController.SnappedVerticalMotion(-2.9f, true, 0f), 1e-6f);
        }

        [Test]
        public void SnappedVerticalMotion_NeverSlowsAFastFallEvenWhenGrounded()
        {
            // 重力で既に GroundSnap/dt より速く落ちているときは、その速さのまま。
            float fast = -40f;
            Assert.AreEqual(fast, PlayerController.SnappedVerticalMotion(fast, true, Dt), 1e-6f);
        }

        // --- 着地イベント（段差を下りただけで着地音を鳴らさない）---

        [Test]
        public void ShouldFireLanded_TinyDropDoesNotCount()
        {
            // 2.1 cm の段を下りて 1 フレームだけ浮いた場合。
            Assert.IsFalse(PlayerController.ShouldFireLanded(true, false, Dt));
            Assert.IsFalse(PlayerController.ShouldFireLanded(true, false, 2f * Dt));
        }

        [Test]
        public void ShouldFireLanded_RealFallCounts()
        {
            Assert.IsTrue(PlayerController.ShouldFireLanded(true, false, PlayerController.MinAirTimeForLanding));
            Assert.IsTrue(PlayerController.ShouldFireLanded(true, false, 0.9f));
        }

        [Test]
        public void ShouldFireLanded_AfterJumpAlwaysCounts()
        {
            // ジャンプ時に _lastGroundedTime を -999 にするので、滞空時間は必ず大きくなる。
            float airTime = 1.2f - -999f;
            Assert.IsTrue(PlayerController.ShouldFireLanded(true, false, airTime));
        }

        [Test]
        public void ShouldFireLanded_NotOnTheGroundOrStillGroundedIsFalse()
        {
            Assert.IsFalse(PlayerController.ShouldFireLanded(false, false, 5f), "空中では出さない");
            Assert.IsFalse(PlayerController.ShouldFireLanded(true, true, 5f), "接地し続けている間は出さない");
        }

        [Test]
        public void ShouldFireLanded_MinAirTimeIsLongerThanASnappedStepDown()
        {
            // 吸い付きで下りきれない一番大きい段差（入口の石張り 11.4 cm）でも、
            // 残りの落下は 1 フレームで終わる。30 fps でも 0.033 s < 0.15 s。
            Assert.Greater(PlayerController.MinAirTimeForLanding, 1f / 30f);
            // ただし長すぎると本物のジャンプ以外の落下で着地音が出なくなるので 0.3 s 未満にする。
            Assert.Less(PlayerController.MinAirTimeForLanding, 0.3f);
        }
    }
}
