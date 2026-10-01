using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 空のマテリアルへの書き込み（SkyMaterial）。KCD/Sky だけを相手にすること、
    /// 長さ 0 の向きを NaN にせず真上に倒すこと、月の w に星の見え方を入れること、
    /// 霧の色を地平線にそろえて距離とモードには触らないこと (#40)。
    /// </summary>
    public sealed class SkyMaterialTests
    {
        private const float Tolerance = 0.0001f;

        private Material _material;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Shader.Find(SkyMaterial.ShaderName);
            Assert.IsNotNull(shader, SkyMaterial.ShaderName + " が見つからない");
            _material = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, Tolerance, what + " の r");
            Assert.AreEqual(expected.g, actual.g, Tolerance, what + " の g");
            Assert.AreEqual(expected.b, actual.b, Tolerance, what + " の b");
            Assert.AreEqual(expected.a, actual.a, Tolerance, what + " の a");
        }

        private static void AssertVector(Vector4 expected, Vector4 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, what + " の x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, what + " の y");
            Assert.AreEqual(expected.z, actual.z, Tolerance, what + " の z");
            Assert.AreEqual(expected.w, actual.w, Tolerance, what + " の w");
        }

        [Test]
        public void IsSky_OnlyForTheSkyShader()
        {
            Assert.IsTrue(SkyMaterial.IsSky(_material));
            Assert.IsFalse(SkyMaterial.IsSky(null));

            var other = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                Assert.IsFalse(SkyMaterial.IsSky(other), "別のシェーダの skybox を KCD/Sky として書き換えてしまう");
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Apply_NullMaterial_DoesNothing()
        {
            Assert.DoesNotThrow(() => SkyMaterial.Apply(null, SkyPalette.Evaluate(12f), Vector3.up, Vector3.up));
        }

        [Test]
        public void Apply_WritesThePaletteColors()
        {
            SkyState sky = SkyPalette.Evaluate(18f);

            SkyMaterial.Apply(_material, sky, DayNightCycle.DefaultSunDirection(18f), DayNightCycle.MoonDirection);

            AssertColor(sky.Zenith, _material.GetColor(SkyMaterial.ZenithColorId), "天頂");
            AssertColor(sky.Horizon, _material.GetColor(SkyMaterial.HorizonColorId), "地平線");
            AssertColor(sky.SunGlow, _material.GetColor(SkyMaterial.SunGlowColorId), "太陽のまわり");
            AssertColor(sky.Cloud, _material.GetColor(SkyMaterial.CloudColorId), "雲");
            AssertColor(sky.Skyline, _material.GetColor(SkyMaterial.SkylineColorId), "街並み");
        }

        [Test]
        public void Apply_NormalizesTheDirectionsAndPutsTheStarsInTheMoonW()
        {
            SkyState sky = SkyPalette.Evaluate(21f);

            SkyMaterial.Apply(_material, sky, new Vector3(3f, 4f, 0f), new Vector3(0f, 0f, -2f));

            AssertVector(new Vector4(0.6f, 0.8f, 0f, 0f), _material.GetVector(SkyMaterial.SunDirectionId), "太陽の向き");
            AssertVector(new Vector4(0f, 0f, -1f, sky.Stars), _material.GetVector(SkyMaterial.MoonDirectionId), "月の向き");
            Assert.AreEqual(sky.Stars, _material.GetFloat(SkyMaterial.StarsId), Tolerance, "星の見え方");
        }

        [Test]
        public void Apply_ZeroDirections_FallBackToStraightUp()
        {
            SkyState sky = SkyPalette.Evaluate(21f);

            SkyMaterial.Apply(_material, sky, Vector3.zero, Vector3.zero);

            Vector4 sun = _material.GetVector(SkyMaterial.SunDirectionId);
            Assert.IsFalse(float.IsNaN(sun.x) || float.IsNaN(sun.y) || float.IsNaN(sun.z), "長さ 0 の向きが NaN になった");
            AssertVector(new Vector4(0f, 1f, 0f, 0f), sun, "太陽の向き");
            AssertVector(new Vector4(0f, 1f, 0f, sky.Stars), _material.GetVector(SkyMaterial.MoonDirectionId), "月の向き");
        }

        [Test]
        public void ApplyEnvironment_MatchesTheFogToTheHorizonAndLeavesTheDistanceAlone()
        {
            Color fogColor = RenderSettings.fogColor;
            Color ambientSky = RenderSettings.ambientSkyColor;
            Color ambientEquator = RenderSettings.ambientEquatorColor;
            Color ambientGround = RenderSettings.ambientGroundColor;
            float reflection = RenderSettings.reflectionIntensity;
            FogMode mode = RenderSettings.fogMode;
            float start = RenderSettings.fogStartDistance;
            float end = RenderSettings.fogEndDistance;

            try
            {
                SkyState sky = SkyPalette.Evaluate(18.5f);
                SkyMaterial.ApplyEnvironment(sky);

                AssertColor(sky.Horizon, RenderSettings.fogColor, "霧（地平線と同じ色のはず）");
                AssertColor(sky.AmbientSky, RenderSettings.ambientSkyColor, "上の環境光");
                AssertColor(sky.AmbientEquator, RenderSettings.ambientEquatorColor, "横の環境光");
                AssertColor(sky.AmbientGround, RenderSettings.ambientGroundColor, "下の環境光");
                Assert.AreEqual(sky.ReflectionIntensity, RenderSettings.reflectionIntensity, Tolerance, "反射の強さ");

                Assert.AreEqual(mode, RenderSettings.fogMode, "霧のモードを書き換えている");
                Assert.AreEqual(start, RenderSettings.fogStartDistance, Tolerance, "霧の始まりを書き換えている");
                Assert.AreEqual(end, RenderSettings.fogEndDistance, Tolerance, "霧の終わりを書き換えている");
            }
            finally
            {
                RenderSettings.fogColor = fogColor;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.reflectionIntensity = reflection;
            }
        }
    }
}
