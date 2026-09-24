using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 肩越しカメラの寄せる距離と天井の下に収める高さ (#12)。
    /// </summary>
    public sealed class CameraMathTests
    {
        private const float Skin = 0.05f;
        private const float MinDistance = 0.1f;

        [Test]
        public void AllowedDistance_NoHitKeepsTheDesiredDistance()
        {
            Assert.AreEqual(4.2f, CameraMath.AllowedDistance(4.2f, -1f, Skin, MinDistance), 1e-6f);
        }

        [Test]
        public void AllowedDistance_StopsSkinBeforeTheHit()
        {
            Assert.AreEqual(1.95f, CameraMath.AllowedDistance(4.2f, 2f, Skin, MinDistance), 1e-5f);
        }

        [Test]
        public void AllowedDistance_WallRightBehindTheHeadStillPullsInToTheMinimum()
        {
            // Deoccluder は見る点から 0.8 m より手前に寄らず、そこから壁の中が写っていた。今は 0.1 m まで寄る。
            Assert.AreEqual(MinDistance, CameraMath.AllowedDistance(4.2f, 0f, Skin, MinDistance), 1e-6f);
            Assert.AreEqual(MinDistance, CameraMath.AllowedDistance(4.2f, 0.12f, Skin, MinDistance), 1e-6f);
            Assert.Less(CameraMath.AllowedDistance(4.2f, 0.7f, Skin, MinDistance), 0.8f);
        }

        [Test]
        public void AllowedDistance_NeverFartherThanDesired()
        {
            Assert.AreEqual(3f, CameraMath.AllowedDistance(3f, 5f, Skin, MinDistance), 1e-6f);
            Assert.AreEqual(0.05f, CameraMath.AllowedDistance(0.05f, 0f, Skin, MinDistance), 1e-6f,
                "望む距離が最小距離より近いときは望む距離のまま");
            Assert.AreEqual(0f, CameraMath.AllowedDistance(-1f, -1f, Skin, MinDistance), 1e-6f);
        }

        [Test]
        public void CeilingClampedHeight_KeepsTheCameraMarginBelowTheCeiling()
        {
            Assert.AreEqual(2.9f, CameraMath.CeilingClampedHeight(3.7f, 1.47f, 3.2f, 0.3f), 1e-5f);
            Assert.AreEqual(2.5f, CameraMath.CeilingClampedHeight(2.5f, 1.47f, 3.2f, 0.3f), 1e-6f,
                "天井より十分低ければそのまま");
        }

        [Test]
        public void CeilingClampedHeight_DoesNotPushBelowTheLookAtPoint()
        {
            // 天井が頭のすぐ上（階段の下など）でも、見る点の高さより下には押し下げない。
            Assert.AreEqual(1.47f, CameraMath.CeilingClampedHeight(3.7f, 1.47f, 1.6f, 0.3f), 1e-6f);
            Assert.AreEqual(0.8f, CameraMath.CeilingClampedHeight(0.8f, 1.47f, 1.6f, 0.3f), 1e-6f,
                "見る点より低いカメラは動かさない");
        }

        [Test]
        public void CeilingClampedHeight_NoCeilingLeavesTheHeight()
        {
            Assert.AreEqual(6f, CameraMath.CeilingClampedHeight(6f, 1.47f, float.PositiveInfinity, 0.3f), 1e-6f);
            Assert.AreEqual(6f, CameraMath.CeilingClampedHeight(6f, 1.47f, float.NaN, 0.3f), 1e-6f);
        }

        [Test]
        public void ClampToSphere_PullsOutsidePointsBackOntoTheSurface()
        {
            Vector3 center = new Vector3(1f, 2f, 3f);
            Vector3 inside = center + new Vector3(0f, 0f, 7.9f);
            Assert.AreEqual(inside, CameraMath.ClampToSphere(center, 8f, inside));

            Vector3 clamped = CameraMath.ClampToSphere(center, 8f, center + new Vector3(0f, 0f, 20f));
            Assert.AreEqual(8f, Vector3.Distance(center, clamped), 1e-4f);
            Assert.AreEqual(0f, clamped.x - center.x, 1e-5f, "向きは変えない");

            Assert.AreEqual(center, CameraMath.ClampToSphere(center, -1f, center + Vector3.one), "負の半径は中心");
        }

        [Test]
        public void MaxOrbitHeight_MatchesTheOutdoorOrbit()
        {
            // 屋外は半径 4.2 m・倍率最大 1.8・縦角最大 62 度で、見る点の上 6.67 m まで上がる。
            Assert.AreEqual(4.2f * 1.8f * Mathf.Sin(62f * Mathf.Deg2Rad), CameraMath.MaxOrbitHeight(4.2f, 1.8f, 62f), 1e-4f);
            Assert.AreEqual(0f, CameraMath.MaxOrbitHeight(4.2f, 1f, -10f), 1e-6f);
        }

        [Test]
        public void IndoorProfile_StaysUnderTheLowestInteriorCeiling()
        {
            // 見る点は床から 1.472 m（背丈 1.6 m × 0.92）、屋内の天井はいちばん低くて 3.2 m、天井から 0.3 m 下まで。
            const float aimHeight = 1.6f * 0.92f;
            const float radius = 4.2f;
            CameraOrbitProfile indoor = CameraOrbitProfile.Indoor;

            Assert.AreEqual(2.8f, indoor.MaxDistance(radius), 1e-4f);
            Assert.AreEqual(1.4f, indoor.MaxHeightAboveTarget(radius), 1e-4f);
            Assert.LessOrEqual(aimHeight + indoor.MaxHeightAboveTarget(radius), 3.2f - 0.3f);
            Assert.LessOrEqual(indoor.PitchRange.y, 30f);
            Assert.GreaterOrEqual(indoor.PitchRange.x, -28f, "屋外より下にも回り込まない");
        }
    }
}
