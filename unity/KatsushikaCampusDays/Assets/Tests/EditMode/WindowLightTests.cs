using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KCD.Tests
{
    /// <summary>
    /// 夜に研究棟・実験棟の窓がぽつぽつ灯ること (#166)。全部ではなく 1〜2 割、上の階を多めに。
    /// 窓の明かりは WindowLightStage（KCD.Editor）が Campus.unity に焼き、WindowLights が時刻で点ける。
    /// </summary>
    public sealed class WindowLightTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        private Scene _scene;
        private WindowLights _lights;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                WindowLights found = root.GetComponentInChildren<WindowLights>(true);
                if (found != null)
                {
                    _lights = found;
                    break;
                }
            }
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void Campus_HasWindowLights()
        {
            Assert.IsNotNull(_lights, ScenePath + " に WindowLights が無い（SceneBuilder.BuildAll で組み直す）");
            var filter = _lights.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter.sharedMesh, "窓の明かりのメッシュが無い");
            Assert.AreEqual(_lights.Windows.Count * 4, filter.sharedMesh.vertexCount, "点ける窓 1 枚に 4 頂点ではない");
            Assert.IsFalse(_lights.GetComponent<MeshRenderer>().enabled, "朝に始まるのに窓の明かりが点いている");
        }

        [Test]
        public void OnlyOneOrTwoInTenWindowsAreLit()
        {
            Assert.IsNotNull(_lights, ScenePath + " に WindowLights が無い");
            Assert.Greater(_lights.PaneCount, 40, "研究棟・実験棟の窓ガラスを拾えていない");
            float ratio = (float)_lights.Windows.Count / _lights.PaneCount;
            Assert.That(ratio, Is.InRange(0.10f, 0.20f),
                _lights.Windows.Count + " / " + _lights.PaneCount + " 枚が点いている（1〜2 割のはず）");
        }

        [Test]
        public void LitWindowsSitOnTheGlass()
        {
            Assert.IsNotNull(_lights, ScenePath + " に WindowLights が無い");
            List<GlassPane> glass = CollectGlass();
            Assert.AreEqual(_lights.PaneCount, glass.Count, "シーンの窓の数と建物の窓ガラスの数が合わない（組み直す）");

            foreach (Vector3 window in _lights.Windows)
            {
                Assert.Less(Vector3.Distance(window, Nearest(glass, window).Center), 0.05f,
                    window + " の明かりの近くに窓ガラスが無い");
            }
        }

        [Test]
        public void UpperFloorsAreLitMoreThanTheGroundFloor()
        {
            Assert.IsNotNull(_lights, ScenePath + " に WindowLights が無い");
            List<GlassPane> glass = CollectGlass();
            int upper = 0;
            int ground = 0;
            foreach (GlassPane pane in glass)
            {
                if (pane.GroundFloor)
                {
                    ground++;
                }
                else
                {
                    upper++;
                }
            }

            int litUpper = 0;
            int litGround = 0;
            foreach (Vector3 window in _lights.Windows)
            {
                if (Nearest(glass, window).GroundFloor)
                {
                    litGround++;
                }
                else
                {
                    litUpper++;
                }
            }

            Assert.Greater(upper, 0, "上の階の窓が無い");
            Assert.Greater(ground, 0, "1 階の窓が無い");
            float upperRatio = (float)litUpper / upper;
            float groundRatio = (float)litGround / ground;
            Assert.Greater(upperRatio, groundRatio * 1.5f,
                "上の階 " + litUpper + "/" + upper + "、1 階 " + litGround + "/" + ground + "（研究室の階を多めに点けていない）");
        }

        [Test]
        public void Hash_IsStableAndSpread()
        {
            MethodInfo hash = StageType().GetMethod("Hash01", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(hash, "WindowLightStage.Hash01 が無い");

            int below = 0;
            const int Count = 2000;
            for (int i = 0; i < Count; i++)
            {
                var p = new Vector3(i % 40 * 3.1f, i / 40 * 3.8f, 12.5f);
                float a = (float)hash.Invoke(null, new object[] { p });
                float b = (float)hash.Invoke(null, new object[] { p });
                Assert.AreEqual(a, b, "同じ位置で値が変わる");
                Assert.That(a, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                if (a < 0.18f)
                {
                    below++;
                }
            }

            // 窓の並び（等間隔の格子）でも、割合どおりに散らばること。
            Assert.That((float)below / Count, Is.InRange(0.14f, 0.22f), "格子状の位置で値が偏る");
        }

        [TestCase(12f, false)]
        [TestCase(9f, false)]
        [TestCase(19.5f, true)]
        [TestCase(22f, true)]
        [TestCase(2f, true)]
        public void WindowLights_ShowAt_LightsOnlyAtNight(float hours, bool lit)
        {
            var go = new GameObject("WindowLightsProbe", typeof(MeshRenderer));
            try
            {
                WindowLights lights = go.AddComponent<WindowLights>();
                lights.ShowAt(hours);

                var renderer = go.GetComponent<MeshRenderer>();
                Assert.AreEqual(lit, renderer.enabled, hours + " 時");
                if (lit)
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    Assert.AreEqual(LampGlowLights.GlowAt(hours), block.GetFloat(LampGlowLights.GlowId), 1e-4f,
                        hours + " 時の明るさが街灯と揃っていない");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private struct GlassPane
        {
            public Vector3 Center;
            public bool GroundFloor;
        }

        /// <summary>WindowLightStage.FindPanes で、対象の建物の窓ガラスを全部拾う。</summary>
        private List<GlassPane> CollectGlass()
        {
            Type stage = StageType();
            MethodInfo findPanes = stage.GetMethod("FindPanes", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(findPanes, "WindowLightStage.FindPanes が無い（名前を変えたらこのテストも直す）");
            var names = (string[])stage.GetField("BuildingNames", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);

            var glass = new List<GlassPane>();
            foreach (MeshRenderer renderer in BuildingRenderers(names))
            {
                foreach (object pane in (System.Collections.IEnumerable)findPanes.Invoke(null, new object[] { renderer }))
                {
                    Type type = pane.GetType();
                    glass.Add(new GlassPane
                    {
                        Center = (Vector3)type.GetField("Center").GetValue(pane),
                        GroundFloor = (bool)type.GetField("GroundFloor").GetValue(pane),
                    });
                }
            }

            return glass;
        }

        private static GlassPane Nearest(List<GlassPane> glass, Vector3 position)
        {
            GlassPane nearest = default;
            float best = float.MaxValue;
            foreach (GlassPane pane in glass)
            {
                float distance = Vector3.SqrMagnitude(pane.Center - position);
                if (distance < best)
                {
                    best = distance;
                    nearest = pane;
                }
            }

            return nearest;
        }

        private IEnumerable<MeshRenderer> BuildingRenderers(string[] names)
        {
            var wanted = new HashSet<string>(names);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (wanted.Contains(renderer.name) && renderer.TryGetComponent(out MeshFilter filter)
                        && filter.sharedMesh != null)
                    {
                        yield return renderer;
                    }
                }
            }
        }

        private static Type StageType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KCD.Editor")
                {
                    continue;
                }

                Type type = assembly.GetType("KCD.Editor.WindowLightStage");
                Assert.IsNotNull(type, "KCD.Editor に WindowLightStage が無い（名前を変えたらこのテストも直す）");
                return type;
            }

            Assert.Fail("KCD.Editor アセンブリが読み込まれていない");
            return null;
        }
    }
}
