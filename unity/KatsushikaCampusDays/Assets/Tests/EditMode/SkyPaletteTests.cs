using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 時刻ごとの空の色が、トゥーン調の配色（朝 = 淡い青、昼 = 青、夕 = 橙と桃色、夜 = 紺と星）に
    /// 収まっていること (#8)。SkyPalette.Evaluate は DayNightCycle が毎フレーム呼び、空・霧・環境光・太陽へ配る。
    /// </summary>
    public sealed class SkyPaletteTests
    {
        private const float Morning = 7.5f;
        private const float Noon = 12f;
        private const float Evening = 18f;
        private const float Night = 21f;

        private static float Luma(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        [Test]
        public void Morning_IsPaleBlue()
        {
            SkyState morning = SkyPalette.Evaluate(Morning);
            SkyState noon = SkyPalette.Evaluate(Noon);

            Assert.Greater(morning.Horizon.b, morning.Horizon.r, "朝の地平線が青寄りでない");
            Assert.Greater(Luma(morning.Horizon), 0.75f, "朝の地平線が淡くない（暗い）");
            Assert.Greater(morning.Zenith.b, morning.Zenith.r + 0.3f, "朝の天頂が青くない");
            Assert.Greater(Luma(morning.Zenith), Luma(noon.Zenith), "朝の天頂が昼より淡くない");
            Assert.AreEqual(0f, morning.Stars, 1e-4f, "朝に星が出ている");
            Assert.Greater(morning.SunIntensity, 0.5f, "朝の太陽が暗すぎる");
        }

        [Test]
        public void Noon_IsBlue()
        {
            SkyState noon = SkyPalette.Evaluate(Noon);

            Assert.Greater(noon.Zenith.b, 0.8f, "昼の天頂の青が弱い");
            Assert.Greater(noon.Zenith.b - noon.Zenith.r, 0.5f, "昼の天頂がくすんでいる");
            Assert.Greater(noon.Horizon.b, noon.Horizon.r, "昼の地平線が青寄りでない");
            Assert.AreEqual(0f, noon.Stars, 1e-4f, "昼に星が出ている");
            Assert.AreEqual(0f, noon.LampGlow, 1e-4f, "昼に街灯が点いている");
            Assert.Greater(noon.SunIntensity, 1f, "昼の太陽が暗い");
        }

        [Test]
        public void Evening_IsOrangeAndPink()
        {
            SkyState evening = SkyPalette.Evaluate(Evening);

            Color horizon = evening.Horizon;
            Assert.Greater(horizon.r, 0.9f, "夕方の地平線の赤が弱い");
            Assert.Greater(horizon.r, horizon.g, "夕方の地平線が橙でない (r > g)");
            Assert.Greater(horizon.g, horizon.b, "夕方の地平線が橙でない (g > b)");
            Assert.Greater(evening.Cloud.r, evening.Cloud.b, "夕方の雲が桃色に染まっていない");
            Assert.Greater(evening.SunGlow.r, evening.SunGlow.b, "夕方の太陽のまわりが暖色でない");
            Assert.Greater(evening.SunColor.r, evening.SunColor.b, "夕方の日差しが暖色でない");
            Assert.Greater(evening.AmbientEquator.r, evening.AmbientEquator.b, "夕方の横からの環境光が暖色でない");
        }

        [Test]
        public void Night_IsNavyWithStars()
        {
            SkyState night = SkyPalette.Evaluate(Night);

            Color zenith = night.Zenith;
            Assert.Less(Luma(zenith), 0.1f, "夜の天頂が明るすぎる");
            Assert.Greater(zenith.b, zenith.r, "夜の天頂が紺でない (b > r)");
            Assert.Greater(zenith.b, zenith.g, "夜の天頂が紺でない (b > g)");
            Assert.Greater(night.Stars, 0.8f, "夜に星が出ていない");
            Assert.AreEqual(1f, night.LampGlow, 1e-4f, "夜に街灯が点いていない");
            Assert.AreEqual(0f, night.SunIntensity, 1e-4f, "夜に太陽が光っている");
            Assert.Greater(night.MoonIntensity, 0.1f, "夜に月明かりが無い");
            Assert.Less(Luma(night.AmbientSky), 0.2f, "夜の環境光が明るすぎる");
            Assert.Greater(night.AmbientSky.b, night.AmbientSky.r, "夜の環境光が青寄りでない");
        }

        [Test]
        public void GameDay_BrightensAndDarkensInOrder()
        {
            // 一日の始まり 8:30 から終わり 20:00 (DayEndEvaluator) まで。夜ほど反射を弱め、街灯を灯す。
            Assert.Greater(SkyPalette.Evaluate(DayRestart.DayStartHour).SunIntensity, 0.8f, "ゲーム開始の朝が暗い");
            Assert.Less(SkyPalette.Evaluate(Night).ReflectionIntensity,
                SkyPalette.Evaluate(Noon).ReflectionIntensity, "夜の反射が昼より強い");
            Assert.Greater(SkyPalette.Evaluate(19.5f).LampGlow, 0.9f, "19:30 に街灯が点いていない");
            Assert.Greater(SkyPalette.Evaluate(20f).Stars, 0.8f, "一日の終わり 20:00 に星が出ていない");
        }

        [Test]
        public void Fog_IsTheHorizonColor()
        {
            // 霧に溶けた地面と、空の地平線より下（KCD/Sky は _HorizonColor 一色）を同じ色にする (#40)。
            for (float h = 0f; h < 24f; h += 0.5f)
            {
                SkyState sky = SkyPalette.Evaluate(h);
                Assert.AreEqual(sky.Horizon, sky.Fog, h + " 時の霧の色が地平線の色と違う");
            }
        }

        [Test]
        public void Colors_ChangeWithoutJumps()
        {
            // 36 秒 (= ゲーム内 0.01 時間 × 1 日 720 秒 / 24) ごとに見て、色がいきなり跳ばないこと。
            const float Step = 0.01f;
            SkyState previous = SkyPalette.Evaluate(0f);
            for (int i = 1; i <= 2400; i++)
            {
                float h = i * Step;
                SkyState current = SkyPalette.Evaluate(h);
                AssertClose(previous.Zenith, current.Zenith, 0.02f, h, "天頂");
                AssertClose(previous.Horizon, current.Horizon, 0.02f, h, "地平線");
                AssertClose(previous.SunGlow, current.SunGlow, 0.02f, h, "太陽のまわり");
                AssertClose(previous.Cloud, current.Cloud, 0.02f, h, "雲");
                AssertClose(previous.AmbientSky, current.AmbientSky, 0.02f, h, "環境光");
                Assert.AreEqual(previous.SunIntensity, current.SunIntensity, 0.03f, h + " 時の太陽の明るさが跳んだ");
                Assert.AreEqual(previous.Stars, current.Stars, 0.03f, h + " 時の星が跳んだ");
                Assert.AreEqual(previous.LampGlow, current.LampGlow, 0.03f, h + " 時の街灯が跳んだ");
                previous = current;
            }
        }

        [Test]
        public void Midnight_WrapsAround()
        {
            SkyState start = SkyPalette.Evaluate(0f);
            AssertClose(start.Zenith, SkyPalette.Evaluate(24f).Zenith, 1e-4f, 24f, "24 時の天頂");
            AssertClose(start.Horizon, SkyPalette.Evaluate(23.999f).Horizon, 1e-3f, 23.999f, "23:59 の地平線");
            AssertClose(SkyPalette.Evaluate(23f).Horizon, SkyPalette.Evaluate(-1f).Horizon, 1e-4f, -1f,
                "負の時刻の地平線");
        }

        private static void AssertClose(Color a, Color b, float tolerance, float hours, string what)
        {
            Assert.AreEqual(a.r, b.r, tolerance, hours + " 時の" + what + "の r");
            Assert.AreEqual(a.g, b.g, tolerance, hours + " 時の" + what + "の g");
            Assert.AreEqual(a.b, b.b, tolerance, hours + " 時の" + what + "の b");
            Assert.AreEqual(a.a, b.a, tolerance, hours + " 時の" + what + "の a");
        }
    }
}
