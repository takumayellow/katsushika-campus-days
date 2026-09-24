using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 時刻に合わせた色温度・彩度・ビネット（PostProcessDriver.Grade）。
    /// キーの時刻ではその値、キーの間は線形、24 時で折り返し、屋内は時刻によらず中立。
    /// ここがずれると、ロードや屋内の出入りで画面の色が跳ねる。
    /// </summary>
    public sealed class PostProcessDriverTests
    {
        private const float Tolerance = 0.0001f;
        private const float BaseVignette = 0.2f;

        private static void AssertGrade(float hours, float temperature, float saturation, float vignette)
        {
            PostProcessDriver.Grade(hours, false, BaseVignette, out float t, out float s, out float v);
            Assert.AreEqual(temperature, t, Tolerance, hours + " 時の色温度");
            Assert.AreEqual(saturation, s, Tolerance, hours + " 時の彩度");
            Assert.AreEqual(vignette, v, Tolerance, hours + " 時のビネット");
        }

        [TestCase(0f, -12f, -12f, 0.34f)]
        [TestCase(2f, -12f, -12f, 0.34f)]
        [TestCase(5f, -12f, -12f, 0.34f)]
        [TestCase(6.5f, 8f, 4f, 0.26f)]
        [TestCase(9f, 0f, 10f, 0.22f)]
        [TestCase(12f, 0f, 10f, 0.22f)]
        [TestCase(15f, 0f, 10f, 0.22f)]
        [TestCase(17f, 12f, 12f, 0.24f)]
        [TestCase(18.3f, 25f, 18f, 0.30f)]
        [TestCase(19.5f, -12f, -12f, 0.34f)]
        [TestCase(22f, -12f, -12f, 0.34f)]
        public void Grade_AtTheKeys_UsesTheKeyValues(float hours, float temperature, float saturation, float vignette)
        {
            AssertGrade(hours, temperature, saturation, vignette);
        }

        [Test]
        public void Grade_BetweenKeys_IsLinear()
        {
            // 5 時と 6.5 時のまん中。
            AssertGrade(5.75f, -2f, -4f, 0.30f);
        }

        [Test]
        public void Grade_WrapsAroundMidnight()
        {
            PostProcessDriver.Grade(30f, false, BaseVignette, out float t30, out float s30, out float v30);
            PostProcessDriver.Grade(6f, false, BaseVignette, out float t6, out float s6, out float v6);
            Assert.AreEqual(t6, t30, Tolerance, "30 時が 6 時と同じ色になっていない");
            Assert.AreEqual(s6, s30, Tolerance);
            Assert.AreEqual(v6, v30, Tolerance);

            // 負の時刻は前の日の夜。
            AssertGrade(-1f, -12f, -12f, 0.34f);
        }

        [Test]
        public void Grade_JustBeforeAndAfterMidnight_Match()
        {
            PostProcessDriver.Grade(23.999f, false, BaseVignette, out float tLate, out float sLate, out float vLate);
            PostProcessDriver.Grade(0.001f, false, BaseVignette, out float tEarly, out float sEarly, out float vEarly);

            Assert.AreEqual(tEarly, tLate, Tolerance, "日付をまたぐと色温度が跳ねる");
            Assert.AreEqual(sEarly, sLate, Tolerance, "日付をまたぐと彩度が跳ねる");
            Assert.AreEqual(vEarly, vLate, Tolerance, "日付をまたぐとビネットが跳ねる");
        }

        [Test]
        public void Grade_DuskIsTheWarmestAndNightTheDarkest()
        {
            PostProcessDriver.Grade(12f, false, BaseVignette, out float noonTemperature, out _, out float noonVignette);
            PostProcessDriver.Grade(18.3f, false, BaseVignette, out float duskTemperature, out _, out _);
            PostProcessDriver.Grade(22f, false, BaseVignette, out _, out _, out float nightVignette);

            Assert.Greater(duskTemperature, noonTemperature, "夕暮れが昼より暖かい色になっていない");
            Assert.Greater(nightVignette, noonVignette, "夜のほうが周辺が暗くなっていない");
        }

        [TestCase(3f)]
        [TestCase(12f)]
        [TestCase(18.3f)]
        public void Grade_Inside_IsNeutralWhateverTheHour(float hours)
        {
            PostProcessDriver.Grade(hours, true, BaseVignette, out float t, out float s, out float v);

            Assert.AreEqual(0f, t, Tolerance, "屋内で色温度が時刻に引っ張られている");
            Assert.AreEqual(4f, s, Tolerance);
            Assert.AreEqual(BaseVignette, v, Tolerance, "屋内のビネットがプロファイルの値に戻っていない");
        }

        [Test]
        public void Grade_NaNHour_DoesNotLeakNaN()
        {
            PostProcessDriver.Grade(float.NaN, false, BaseVignette, out float t, out float s, out float v);

            Assert.IsFalse(float.IsNaN(t) || float.IsNaN(s) || float.IsNaN(v), "NaN の時刻で画面の色が NaN になる");
        }
    }
}
