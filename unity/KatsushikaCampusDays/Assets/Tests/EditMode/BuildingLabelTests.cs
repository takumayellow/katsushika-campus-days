using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 建物の名札（BuildingLabel）の遠近。近くでは濃く、fadeIn〜fadeOut m の間で薄れ、fadeOut m から先は描画ごと止めること、
    /// 大きさは reference m で等倍、遠いほど大きくして min〜max に収めること。大きさの式はここ、既定値での画面の字の高さは
    /// CampusBuildingLabelTests が見る。
    /// </summary>
    public sealed class BuildingLabelTests
    {
        private const float Tolerance = 0.0001f;
        private const float FadeIn = 120f;
        private const float FadeOut = 220f;
        private const float Reference = 40f;
        private const float MinScale = 0.6f;
        private const float MaxScale = 3.5f;

        // ---- 不透明度 ----

        [TestCase(0f, 1f)]
        [TestCase(60f, 1f)]
        [TestCase(120f, 1f)]
        [TestCase(170f, 0.5f)]
        [TestCase(220f, 0f)]
        [TestCase(500f, 0f)]
        public void AlphaAt_FadesBetweenFadeInAndFadeOut(float distance, float expected)
        {
            Assert.AreEqual(expected, BuildingLabel.AlphaAt(distance, FadeIn, FadeOut), Tolerance, distance + " m の不透明度");
        }

        [Test]
        public void AlphaAt_NeverGetsDarkerFartherAway()
        {
            float previous = BuildingLabel.AlphaAt(0f, FadeIn, FadeOut);
            for (float distance = 0f; distance <= 300f; distance += 1f)
            {
                float alpha = BuildingLabel.AlphaAt(distance, FadeIn, FadeOut);
                Assert.That(alpha, Is.InRange(0f, 1f), distance + " m で不透明度がはみ出した");
                Assert.LessOrEqual(alpha, previous, distance + " m で遠いのに濃くなった");
                previous = alpha;
            }
        }

        [Test]
        public void IsHidden_OnlyWhenAlmostTransparent()
        {
            Assert.IsTrue(BuildingLabel.IsHidden(0f));
            Assert.IsTrue(BuildingLabel.IsHidden(0.01f));
            Assert.IsFalse(BuildingLabel.IsHidden(0.02f), "まだ読める薄さで消えた");
            Assert.IsFalse(BuildingLabel.IsHidden(1f));
        }

        [Test]
        public void IsHidden_FromFadeOutOnward()
        {
            Assert.IsFalse(BuildingLabel.IsHidden(BuildingLabel.AlphaAt(FadeIn, FadeIn, FadeOut)), "近くの名札が消えた");
            Assert.IsFalse(BuildingLabel.IsHidden(BuildingLabel.AlphaAt(210f, FadeIn, FadeOut)));
            Assert.IsTrue(BuildingLabel.IsHidden(BuildingLabel.AlphaAt(FadeOut, FadeIn, FadeOut)),
                "見えない名札を描き続けている");
            Assert.IsTrue(BuildingLabel.IsHidden(BuildingLabel.AlphaAt(1000f, FadeIn, FadeOut)));
        }

        // ---- 大きさ ----

        [TestCase(0f, 0.6f)]
        [TestCase(20f, 0.6f)]
        [TestCase(24f, 0.6f)]
        [TestCase(40f, 1f)]
        [TestCase(80f, 2f)]
        [TestCase(140f, 3.5f)]
        [TestCase(220f, 3.5f)]
        public void ScaleAt_GrowsWithDistanceWithinTheLimits(float distance, float expected)
        {
            Assert.AreEqual(expected, BuildingLabel.ScaleAt(distance, Reference, MinScale, MaxScale), Tolerance,
                distance + " m の大きさ");
        }

        [Test]
        public void NameFollowsLocale()
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale("en");
                    Assert.AreEqual("Library", BuildingLabel.Resolve("ui.building.library", "図書館"), "英語表示で名札が日本語のまま");
                    L.SetLocale("ja");
                    Assert.AreEqual("図書館", BuildingLabel.Resolve("ui.building.library", "図書館"));
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        [Test]
        public void NameFallsBackWhenKeyIsMissing()
        {
            Assert.AreEqual("第9研究棟", BuildingLabel.Resolve("ui.building.no_such_building", "第9研究棟"));
            Assert.AreEqual("第9研究棟", BuildingLabel.Resolve(string.Empty, "第9研究棟"));
        }
    }
}
