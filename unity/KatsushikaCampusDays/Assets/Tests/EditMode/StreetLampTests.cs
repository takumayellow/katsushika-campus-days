using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// モールの照明柱の夜の明かり (#8)。灯具の位置は site_furniture の頂点から拾い（StreetLampLayout）、
    /// 実ライトを使わずに加算合成の KCD/LampGlow で描く。明るさは SkyPalette の LampGlow。
    /// </summary>
    public sealed class StreetLampTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string LampShaderName = "KCD/LampGlow";

        /// <summary>blender/kcd_lib/props.py の add_lamp と同じ形の頂点（柱の上下の輪・灯具の箱・発光面の箱）。</summary>
        private static void AddLamp(List<Vector3> vertices, Vector3 foot)
        {
            const float Height = 4f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                var ring = new Vector3(Mathf.Cos(angle) * 0.09f, 0f, Mathf.Sin(angle) * 0.09f);
                vertices.Add(foot + ring);
                vertices.Add(foot + ring + Vector3.up * Height);
            }

            AddBox(vertices, foot, 0.55f, 0.30f, Height, Height + 0.22f);
            AddBox(vertices, foot, 0.45f, 0.22f, Height - 0.08f, Height);
        }

        private static void AddBox(List<Vector3> vertices, Vector3 foot, float sizeX, float sizeZ, float y0, float y1)
        {
            foreach (float y in new[] { y0, y1 })
            {
                foreach (float x in new[] { -sizeX * 0.5f, sizeX * 0.5f })
                {
                    foreach (float z in new[] { -sizeZ * 0.5f, sizeZ * 0.5f })
                    {
                        vertices.Add(foot + new Vector3(x, y, z));
                    }
                }
            }
        }

        /// <summary>ベンチ（高さ 0.92 m まで）。</summary>
        private static void AddBench(List<Vector3> vertices, Vector3 foot)
        {
            AddBox(vertices, foot, 1.8f, 0.6f, 0f, 0.45f);
            AddBox(vertices, foot + new Vector3(0f, 0f, 0.25f), 1.8f, 0.08f, 0.45f, 0.92f);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f, what + " の x");
            Assert.AreEqual(expected.y, actual.y, 1e-3f, what + " の y");
            Assert.AreEqual(expected.z, actual.z, 1e-3f, what + " の z");
        }

        [Test]
        public void Layout_FindsEachLampHeadAboveTheBenches()
        {
            // モールの両側の照明柱は横に 4.32 m・縦に 9.46 m（斜めに 10.4 m）離れている（campus.fbx の実測）。
            var vertices = new List<Vector3>();
            AddBench(vertices, new Vector3(3f, 0f, 2f));
            AddLamp(vertices, new Vector3(14.32f, 0f, 14.46f));
            AddLamp(vertices, new Vector3(10f, 0f, 5f));
            AddBench(vertices, new Vector3(12f, 0f, 9f));

            List<Vector3> lamps = StreetLampLayout.FindLamps(vertices);

            Assert.AreEqual(2, lamps.Count, "照明柱 2 本が 2 つの灯具にならない");
            AssertNear(new Vector3(10f, 3.92f, 5f), lamps[0], "1 本目の灯具（発光面の中心）");
            AssertNear(new Vector3(14.32f, 3.92f, 14.46f), lamps[1], "2 本目の灯具（発光面の中心）");
        }

        [Test]
        public void Layout_MeasuresHeightFromTheLowestVertex()
        {
            // 敷地ごと持ち上がっていても、ベンチを灯具と取り違えず、発光面の高さも一緒に上がる。
            var vertices = new List<Vector3>();
            var lift = new Vector3(0f, 2.5f, 0f);
            AddBench(vertices, new Vector3(0f, 0f, 0f) + lift);
            AddLamp(vertices, new Vector3(20f, 0f, 0f) + lift);

            List<Vector3> lamps = StreetLampLayout.FindLamps(vertices);

            Assert.AreEqual(1, lamps.Count);
            AssertNear(new Vector3(20f, 3.92f + 2.5f, 0f), lamps[0], "持ち上げた灯具");
        }

        [Test]
        public void Layout_WithoutLampsFindsNothing()
        {
            var benches = new List<Vector3>();
            AddBench(benches, Vector3.zero);
            AddBench(benches, new Vector3(20f, 0f, 0f));

            Assert.AreEqual(0, StreetLampLayout.FindLamps(benches).Count, "ベンチを灯具と数えた");
            Assert.AreEqual(0, StreetLampLayout.FindLamps(new List<Vector3>()).Count);
            Assert.AreEqual(0, StreetLampLayout.FindLamps(null).Count);
        }

        [Test]
        public void Glow_IsOffInTheDaytime()
        {
            foreach (float hours in new[] { 6.5f, DayRestart.DayStartHour, 12f, 15f, 17.5f })
            {
                float glow = StreetLights.GlowAt(hours);
                Assert.AreEqual(0f, glow, 1e-4f, hours + " 時に街灯が灯っている");
                Assert.IsFalse(StreetLights.IsLit(glow), hours + " 時に街灯を描いている");
            }
        }

        [Test]
        public void Glow_IsOnAtNight()
        {
            foreach (float hours in new[] { 19.5f, 21f, 23.5f, 0f, 3f })
            {
                float glow = StreetLights.GlowAt(hours);
                Assert.GreaterOrEqual(glow, 0.95f, hours + " 時に街灯が暗い");
                Assert.IsTrue(StreetLights.IsLit(glow), hours + " 時に街灯を描いていない");
            }
        }

        [Test]
        public void Glow_ComesOnAfterSunsetAndGoesOffAtDawn()
        {
            // 日没 18:00 から少しずつ灯り、ブルーアワー 19:24 に全灯。夜明けは 6:00 までに消える。途中で跳ねない。
            Assert.LessOrEqual(StreetLights.GlowAt(18f), StreetLights.OffThreshold, "日没の前から灯っている");
            Assert.That(StreetLights.GlowAt(18.7f), Is.InRange(0.4f, 0.9f), "薄暮の街灯の明るさ");

            float previous = -1f;
            for (float hours = 18f; hours <= 19.4f; hours += 0.05f)
            {
                float glow = StreetLights.GlowAt(hours);
                Assert.GreaterOrEqual(glow, previous - 1e-4f, hours + " 時に街灯が暗くなった");
                previous = glow;
            }

            previous = 2f;
            for (float hours = 4.6f; hours <= 6f; hours += 0.05f)
            {
                float glow = StreetLights.GlowAt(hours);
                Assert.LessOrEqual(glow, previous + 1e-4f, hours + " 時に街灯が明るくなった");
                previous = glow;
            }

            Assert.AreEqual(0f, StreetLights.GlowAt(6f), 1e-4f, "夜明けのあとも街灯が灯っている");
        }

        [Test]
        public void LampShader_Compiles()
        {
            Shader shader = Shader.Find(LampShaderName);
            Assert.IsNotNull(shader, LampShaderName + " が見つからない");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), LampShaderName + " のコンパイルに失敗している");
        }

        [Test]
        public void LampShader_StaysLightForWebGL()
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Shaders", "KCD_LampGlow.shader"));
            string code = Regex.Replace(text, @"//[^\n]*", string.Empty);
            StringAssert.DoesNotMatch(@"\bfor\s*\(", code, "KCD_LampGlow.shader にループがある");
            StringAssert.DoesNotMatch(@"\bwhile\s*\(", code, "KCD_LampGlow.shader にループがある");
            StringAssert.DoesNotMatch(@"SAMPLE_TEXTURE|tex2D|texCUBE|\.Sample\s*\(", code,
                "KCD_LampGlow.shader がテクスチャを読んでいる");
            StringAssert.Contains("Blend One One", code, "街灯の明かりが加算合成でない");
        }

        [Test]
        public void Campus_MallLampsGlowWithoutRealLights()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                StreetLights lights = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    lights = root.GetComponentInChildren<StreetLights>(true);
                    if (lights != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(lights, ScenePath + " に StreetLights が無い（KCD/シーンを組み直す）");

                // campus.fbx の照明柱は 22 本（site.py の 12 か所 × モールの両側から、建物にかかる 2 本を除く）。
                IReadOnlyList<Vector3> lamps = lights.Lamps;
                Assert.That(lamps.Count, Is.InRange(16, 24), "照明柱の数が campus.fbx と合わない");
                for (int i = 0; i < lamps.Count; i++)
                {
                    for (int j = i + 1; j < lamps.Count; j++)
                    {
                        Vector3 d = lamps[i] - lamps[j];
                        d.y = 0f;
                        Assert.Greater(d.magnitude, 5f, "照明柱 " + i + " と " + j + " が近すぎる（1 本を 2 つに数えた）");
                    }
                }

                MeshRenderer renderer = lights.GetComponent<MeshRenderer>();
                MeshFilter filter = lights.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "街灯の明かりに MeshFilter が無い");
                Assert.IsNotNull(filter.sharedMesh, "街灯の明かりのメッシュが無い");
                Assert.IsNotNull(renderer.sharedMaterial, "街灯の明かりにマテリアルが無い");
                Assert.AreEqual(LampShaderName, renderer.sharedMaterial.shader.name);
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, "街灯の明かりが影を落とす");

                // 実ライトを足さない（WebGL の Forward は 1 オブジェクト 4 灯まで）。当たり判定も持たない。
                Assert.AreEqual(0, lights.GetComponentsInChildren<Light>(true).Length, "街灯に実ライトがある");
                Assert.AreEqual(0, lights.GetComponentsInChildren<Collider>(true).Length, "街灯の明かりに当たり判定がある");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
