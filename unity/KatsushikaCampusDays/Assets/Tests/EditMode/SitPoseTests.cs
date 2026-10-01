using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 座りポーズの体格合わせ（SitPose.Solve）。標準の体は座面に腰を下ろして足を床すれすれに置き、
    /// 頭身の低い体は座面より沈まずに腰を持ち上げて足を垂らすこと、背の高い体は足が床に着くこと。
    /// 体の寸法は blender/kcd_chara/params.py と rig.py の Hips・膝の高さ。
    /// </summary>
    public sealed class SitPoseTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void ReferenceBodySitsOnTheSeatWithFeetNearTheFloor()
        {
            SitPose.Layout layout = SitPose.Solve(0.80f, 0.434f, 0.09f);

            Assert.AreEqual(-0.34f, layout.HipShift, Tolerance, "腰の下げ幅");
            AssertNear(new Vector3(0.13f, 0.026f, 0.38f), layout.Foot, "足");
            AssertNear(new Vector3(0.13f, 0.45f, 0.55f), layout.Knee, "膝");
            AssertNear(new Vector3(0.16f, 0.52f, 0.25f), layout.Hand, "手");
        }

        [Test]
        public void ChibiBodySitsOnTheSeatInsteadOfSinking()
        {
            // 坊っちゃん: Hips 0.227 m・膝 0.103 m。立ったときの腰が座面 0.44 m より低い。
            const float hip = 0.227f;
            SitPose.Layout layout = SitPose.Solve(hip, 0.103f, 0.05f);

            Assert.Greater(layout.HipShift, 0f, "腰を持ち上げずに下げている（座面にめり込む）");
            Assert.GreaterOrEqual(hip + layout.HipShift, SitPose.SeatSurface, "腰が座面より下にある");
            Assert.Greater(layout.Foot.y, 0.3f, "短い脚の足を床まで引き伸ばしている");
            Assert.Less(layout.Foot.y, hip + layout.HipShift, "足が腰より上にある");
            Assert.Less(layout.Foot.z, layout.Knee.z, "足が膝より前に出ている");
            Assert.GreaterOrEqual(layout.Foot.x, 0.05f, "両足が脚の付け根より内側に寄っている");
        }

        [Test]
        public void TallBodyKeepsFeetOnTheFloor()
        {
            // 教授: Hips 0.882 m・膝 0.479 m。座った腰より膝のほうが高いので、足は床の高さに置く。
            SitPose.Layout layout = SitPose.Solve(0.882f, 0.479f, 0.10f);

            Assert.Less(layout.HipShift, -0.34f, "背が高いのに腰の下げ幅が標準の体以下");
            Assert.AreEqual(0.02f, layout.Foot.y, 0.0001f, "足が床から浮いている");
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, what + " x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, what + " y");
            Assert.AreEqual(expected.z, actual.z, Tolerance, what + " z");
        }
    }
}
