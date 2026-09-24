using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// モブの描き方の段（MobScheduler.PickTier）と、道の途中に出す点（PointAlong）を守る (#22)。
    /// 近い 4 人だけ影と輪郭まで、次の 4 人は色だけ、残りは描かない。WebGL の描画予算をここで押さえている。
    /// </summary>
    public sealed class MobSchedulerTests
    {
        private const int NearCount = MobScheduler.DefaultNearCount;
        private const float NearDistance = MobScheduler.DefaultNearDistance;
        private const int DrawnCount = MobScheduler.DefaultDrawnCount;
        private const float DrawnDistance = MobScheduler.DefaultDrawnDistance;

        private static MobTier Pick(int rank, float distance, bool held = false, MobTier current = MobTier.Hidden)
        {
            return MobScheduler.PickTier(rank, distance, held, current, NearCount, NearDistance, DrawnCount, DrawnDistance);
        }

        [Test]
        public void 上限の人数が出ていても描くのは近い順に決まった人数だけ()
        {
            int near = 0;
            int mid = 0;
            for (int rank = 0; rank < MobScheduler.MaxActive; rank++)
            {
                // 全員がすぐそばにいても、段の人数は順位で切る。
                switch (Pick(rank, 5f))
                {
                    case MobTier.Near:
                        near++;
                        break;
                    case MobTier.Mid:
                        mid++;
                        break;
                }
            }

            Assert.AreEqual(NearCount, near, "影と輪郭まで描く人数");
            Assert.AreEqual(DrawnCount - NearCount, mid, "色だけ描く人数");
        }

        [TestCase(0, 10f, MobTier.Near)]
        [TestCase(0, 30f, MobTier.Mid)]
        [TestCase(0, 50f, MobTier.Hidden)]
        [TestCase(5, 10f, MobTier.Mid)]
        [TestCase(8, 10f, MobTier.Hidden)]
        public void 順位と距離で段を決める(int rank, float distance, MobTier expected)
        {
            Assert.AreEqual(expected, Pick(rank, distance));
        }

        [Test]
        public void 話しかけられている人は遠くても近い段()
        {
            Assert.AreEqual(MobTier.Near, Pick(MobScheduler.MaxActive - 1, 200f, true));
        }

        [Test]
        public void 境目ではいまの段にとどまる()
        {
            float justOutsideNear = NearDistance + MobScheduler.Hysteresis * 0.5f;
            Assert.AreEqual(MobTier.Near, Pick(0, justOutsideNear, false, MobTier.Near), "描いている人は少し遠くても Near のまま");
            Assert.AreEqual(MobTier.Mid, Pick(0, justOutsideNear, false, MobTier.Mid), "Mid の人は少し近づいただけでは Near にしない");

            float justOutsideDrawn = DrawnDistance + MobScheduler.Hysteresis * 0.5f;
            Assert.AreEqual(MobTier.Mid, Pick(5, justOutsideDrawn, false, MobTier.Mid));
            Assert.AreEqual(MobTier.Hidden, Pick(5, justOutsideDrawn, false, MobTier.Hidden), "見えていない人を境目の外で出さない");
        }

        [Test]
        public void 道の途中の点は長さの割合で進む()
        {
            var corners = new[] { Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f) };
            AssertNear(Vector3.zero, MobScheduler.PointAlong(corners, 3, 0f));
            AssertNear(new Vector3(10f, 0f, 0f), MobScheduler.PointAlong(corners, 3, 0.5f));
            AssertNear(new Vector3(10f, 0f, 5f), MobScheduler.PointAlong(corners, 3, 0.75f));
            AssertNear(new Vector3(10f, 0f, 10f), MobScheduler.PointAlong(corners, 3, 1f));
            AssertNear(new Vector3(10f, 0f, 10f), MobScheduler.PointAlong(corners, 3, 2f));
        }

        [Test]
        public void 道の点が足りなければ落ちない()
        {
            AssertNear(Vector3.zero, MobScheduler.PointAlong(null, 0, 0.5f));
            var one = new[] { new Vector3(1f, 2f, 3f) };
            AssertNear(one[0], MobScheduler.PointAlong(one, 1, 0.5f));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-3f, "期待 " + expected + " 実際 " + actual);
        }
    }
}
