using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    public sealed class StuckRecoveryTests
    {
        [Test]
        public void CandidateOffsets_StartsWithSmallLiftThenRingsInNearestOrder()
        {
            Vector3[] offsets = StuckRecovery.CandidateOffsets(0.5f, 3, 8);

            Assert.AreEqual(1 + 3 * 8, offsets.Length);
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), offsets[0]);

            float previous = 0f;
            for (int i = 1; i < offsets.Length; i++)
            {
                Assert.AreEqual(0f, offsets[i].y, 1e-6f, "水平の輪は高さを変えない");
                float radius = new Vector2(offsets[i].x, offsets[i].z).magnitude;
                Assert.GreaterOrEqual(radius + 1e-5f, previous, "近い順に並ぶ");
                previous = radius;
            }

            Assert.AreEqual(1.5f, previous, 1e-5f, "最後の輪が最大探索距離");
        }

        [Test]
        public void CandidateOffsets_RingDirectionsAreEvenlySpread()
        {
            Vector3[] offsets = StuckRecovery.CandidateOffsets(1f, 1, 4);

            // 1 輪目は位相 0 なので +Z, +X, -Z, -X の順。
            Assert.AreEqual(0f, offsets[1].x, 1e-5f);
            Assert.AreEqual(1f, offsets[1].z, 1e-5f);
            Assert.AreEqual(1f, offsets[2].x, 1e-5f);
            Assert.AreEqual(0f, offsets[2].z, 1e-5f);
            Assert.AreEqual(-1f, offsets[3].z, 1e-5f);
            Assert.AreEqual(-1f, offsets[4].x, 1e-5f);
        }

        [Test]
        public void CandidateOffsets_InvalidArgumentsGiveNoCandidates()
        {
            Assert.AreEqual(0, StuckRecovery.CandidateOffsets(0f, 3, 8).Length);
            Assert.AreEqual(0, StuckRecovery.CandidateOffsets(0.5f, 0, 8).Length);
            Assert.AreEqual(0, StuckRecovery.CandidateOffsets(0.5f, 3, 0).Length);
        }

        [Test]
        public void ContactBand_WallContactWithinSkinWidthIsNotOverlap()
        {
            const float skin = 0.028f;
            Assert.Greater(StuckRecovery.ContactBand(skin), skin, "skinWidth までの食い込みは接触として許す");
            Assert.IsFalse(StuckRecovery.IsRealOverlap(skin, skin), "壁に押し付けて skinWidth 沈んだだけでは押し出さない");
            Assert.IsFalse(StuckRecovery.IsRealOverlap(0.001f, skin), "床に立っているだけでは押し出さない");
            Assert.IsTrue(StuckRecovery.IsRealOverlap(0.2f, skin), "本体が深く入り込んだら押し出す");
        }

        [Test]
        public void ContactBand_StaysWellInsideTheBody()
        {
            // プレイヤーは半径 0.28 m・skinWidth 0.028 m。接触帯が半径の半分を超えると本当の食い込みを見逃す。
            Assert.Less(StuckRecovery.ContactBand(0.028f), 0.28f * 0.5f);
            Assert.AreEqual(0f, StuckRecovery.ContactBand(-1f), "負の skinWidth は 0 扱い");
            Assert.IsTrue(StuckRecovery.IsRealOverlap(0.001f, 0f));
        }

        [Test]
        public void SolidMask_ExcludesOwnLayerAndKeepsOthers()
        {
            var go = new GameObject("player");
            try
            {
                go.layer = 8;
                int mask = StuckRecovery.SolidMask(go);
                Assert.AreEqual(0, mask & (1 << 8), "自分のレイヤーは除く");
                Assert.AreNotEqual(0, mask & 1, "Default レイヤーは残す");
                Assert.AreEqual(0, mask & (1 << 2), "Ignore Raycast は元から入らない");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SolidMask_ExcludesNpcsButKeepsBuildingsAndGround()
        {
            int npc = LayerMask.NameToLayer("NPC");
            int building = LayerMask.NameToLayer("Building");
            int ground = LayerMask.NameToLayer("Ground");
            int player = LayerMask.NameToLayer("Player");
            Assert.GreaterOrEqual(npc, 0, "NPC レイヤーが TagManager に無い");
            Assert.GreaterOrEqual(building, 0);
            Assert.GreaterOrEqual(ground, 0);
            Assert.GreaterOrEqual(player, 0);

            var go = new GameObject("player");
            try
            {
                go.layer = player;
                int mask = StuckRecovery.SolidMask(go);
                Assert.AreEqual(0, mask & (1 << npc), "歩いてくる NPC と重なっても埋まったことにしない");
                Assert.AreEqual(0, mask & (1 << player));
                Assert.AreNotEqual(0, mask & (1 << building), "建物には埋まる");
                Assert.AreNotEqual(0, mask & (1 << ground), "地面には埋まる");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
