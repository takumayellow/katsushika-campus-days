using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 太陽の通り道 (#8)。東（+x）の地平線から昇り、南（-z）の空の 90° − 緯度で南中し、西（-x）へ沈む。
    /// 以前は Euler(高さ, 20, 0) で天頂を通り、影が昼に足もとへ潰れていた。空（KCD/Sky）の太陽の円盤と
    /// 夕焼けの向きもこの向きから決まるので、ライトと空がずれないよう同じ関数で押さえる。
    /// </summary>
    public sealed class SunPathTests
    {
        private const float Sunrise = DayNightCycle.DefaultSunriseHour;
        private const float Sunset = DayNightCycle.DefaultSunsetHour;
        private const float Latitude = DayNightCycle.DefaultLatitudeDegrees;

        [Test]
        public void Sunrise_IsOnTheEastHorizon()
        {
            Vector3 sun = DayNightCycle.DefaultSunDirection(Sunrise);
            Assert.AreEqual(0f, sun.y, 1e-3f, "日の出の太陽が地平線に無い");
            Assert.AreEqual(1f, sun.x, 1e-3f, "日の出の太陽が真東に無い");
        }

        [Test]
        public void Noon_IsHighInTheSouth()
        {
            float noon = (Sunrise + Sunset) * 0.5f;
            Vector3 sun = DayNightCycle.DefaultSunDirection(noon);

            Assert.AreEqual(90f - Latitude, DayNightCycle.HeightDegrees(sun), 0.1f, "南中の高さが 90° − 緯度でない");
            Assert.Less(sun.z, -0.5f, "南中の太陽が南（-z）に無い");
            Assert.AreEqual(0f, sun.x, 1e-3f, "南中の太陽が東西にずれている");
            Assert.AreEqual(1f, sun.magnitude, 1e-4f);
        }

        [Test]
        public void Sunset_IsOnTheWestHorizon()
        {
            Vector3 sun = DayNightCycle.DefaultSunDirection(Sunset);
            Assert.AreEqual(0f, sun.y, 1e-3f, "日の入りの太陽が地平線に無い");
            Assert.AreEqual(-1f, sun.x, 1e-3f, "日の入りの太陽が真西に無い");
        }

        [Test]
        public void Night_SunIsBelowTheHorizonAndDark()
        {
            foreach (float hours in new[] { 0f, 3f, 19.5f, 21f, 23.5f })
            {
                Vector3 sun = DayNightCycle.DefaultSunDirection(hours);
                Assert.Less(sun.y, 0f, hours + " 時に太陽が地平線より上にある");
                Assert.AreEqual(0f, DayNightCycle.SunLightIntensity(SkyPalette.Evaluate(hours), sun), 1e-4f,
                    hours + " 時に太陽が光っている");
            }
        }

        [Test]
        public void GameStart_MorningSunLightsFromTheEast()
        {
            float hours = DayRestart.DayStartHour;
            Vector3 sun = DayNightCycle.DefaultSunDirection(hours);
            float height = DayNightCycle.HeightDegrees(sun);

            Assert.Greater(sun.x, 0.5f, "朝の太陽が東に無い");
            Assert.That(height, Is.InRange(20f, 45f), "朝 8:30 の太陽の高さが朝らしくない");
            Assert.Greater(DayNightCycle.SunLightIntensity(SkyPalette.Evaluate(hours), sun), 0.8f, "朝の日差しが弱い");
        }

        [Test]
        public void Evening_SunIsLowInTheWest()
        {
            Vector3 sun = DayNightCycle.DefaultSunDirection(17.5f);
            float height = DayNightCycle.HeightDegrees(sun);

            Assert.Less(sun.x, -0.5f, "夕方の太陽が西に無い");
            Assert.That(height, Is.InRange(3f, 20f), "17:30 の太陽が低くない");
        }

        [Test]
        public void SunLight_FadesAtTheHorizon()
        {
            // 地平線ぎわで日差しがいきなり消えず、月明かりに切り替わる前に暗くなっていくこと。
            float previous = float.MaxValue;
            for (float hours = 17.8f; hours <= 18.6f; hours += 0.1f)
            {
                float intensity = DayNightCycle.SunLightIntensity(
                    SkyPalette.Evaluate(hours), DayNightCycle.DefaultSunDirection(hours));
                Assert.LessOrEqual(intensity, previous + 1e-4f, hours + " 時に日差しが強くなった");
                previous = intensity;
            }

            Assert.AreEqual(0f, previous, 1e-4f, "日の入りのあとも日差しが残っている");
        }

        [Test]
        public void Moon_IsAboveTheHorizon()
        {
            Assert.Greater(DayNightCycle.HeightDegrees(DayNightCycle.MoonDirection), 20f, "月が低すぎる");
            Assert.AreEqual(1f, DayNightCycle.MoonDirection.magnitude, 1e-4f);
        }
    }
}
