using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>入口ごとの判定箱と歩き入りの幅 (#39)。数値は blender/kcd_lib/entrances.py の STANDARD / SMALL に合わせる。</summary>
    public sealed class EntranceTriggerTests
    {
        /// <summary>プレイヤーの CharacterController の半径（ActorFactory.BodyRadius）。</summary>
        private const float PlayerRadius = 0.28f;

        [Test]
        public void StandardDoor_KeepsPreviousSizes()
        {
            Assert.IsFalse(EntranceTrigger.IsSmallDoor("library"));
            Assert.AreEqual(1.2f, EntranceTrigger.WalkInHalfWidthFor("library"), 1e-5f);
            Vector3 box = EntranceTrigger.BoxSizeFor("library");
            Assert.AreEqual(EntranceTrigger.BoxSize.x, box.x, 1e-5f, "ふつうの入口の箱は幅 4 m のまま");
            Assert.AreEqual(EntranceTrigger.BoxSize.y, box.y, 1e-5f);
            Assert.AreEqual(EntranceTrigger.BoxSize.z, box.z, 1e-5f);
        }

        [Test]
        public void Greenhouse_UsesNarrowWalkInAndBox()
        {
            Assert.IsTrue(EntranceTrigger.IsSmallDoor("greenhouse"));

            float walkIn = EntranceTrigger.WalkInHalfWidthFor("greenhouse");
            Assert.AreEqual(0.5f, walkIn, 1e-5f);
            Assert.LessOrEqual(walkIn + PlayerRadius, EntranceTrigger.SmallOpeningHalfWidth,
                "歩き入りの端に立ってもプレイヤーの体が開口 (半幅 0.9 m) に収まる");

            Vector3 box = EntranceTrigger.BoxSizeFor("greenhouse");
            Assert.AreEqual(2.2f, box.x, 1e-5f);
            Assert.Less(box.x, 2f * EntranceTrigger.SmallOuterHalfWidth, "箱は風除室の外形 (3.0 m) より狭い");
            Assert.AreEqual(EntranceTrigger.BoxSize.y, box.y, 1e-5f);
            Assert.AreEqual(EntranceTrigger.BoxSize.z, box.z, 1e-5f);
        }

        [TestCase("research1")]
        [TestCase("kyoso")]
        [TestCase("lecture")]
        [TestCase("research2")]
        [TestCase("library")]
        [TestCase("gym")]
        [TestCase("lab1")]
        [TestCase("lab2")]
        [TestCase("greenhouse")]
        public void WalkInZone_StaysInsideOpeningAndBox(string id)
        {
            float opening = EntranceTrigger.IsSmallDoor(id)
                ? EntranceTrigger.SmallOpeningHalfWidth
                : EntranceTrigger.StandardOpeningHalfWidth;
            float walkIn = EntranceTrigger.WalkInHalfWidthFor(id);

            Assert.Greater(walkIn, 0f, "歩き入りが切れていない");
            Assert.Less(walkIn, opening, "歩き入りは扉の開口より内側");
            Assert.Less(walkIn, EntranceTrigger.BoxSizeFor(id).x * 0.5f, "歩き入りは判定箱の中");
        }
    }
}
