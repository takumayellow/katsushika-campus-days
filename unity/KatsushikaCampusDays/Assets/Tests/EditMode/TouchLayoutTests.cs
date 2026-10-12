using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときの UI の大きさ (#187)。横持ちのスマホ（844×390）では、1920×1080 基準の UI は文字が 13 px 前後まで縮む。
    /// 指のときは基準を 1280×720 にして 1.5 倍に描き、大きな枠ははみ出す分だけ縮めて画面に収める。
    /// </summary>
    public sealed class TouchLayoutTests
    {
        private const string TitleScenePath = "Assets/Scenes/Title.unity";
        private const string CampusScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>指のときの専用 Canvas。最初から 1280×720 基準で作っているので切り替えない。</summary>
        private const string TouchCanvasName = "TouchCanvas";

        private static readonly string[] TitlePanels = { "Settings", "Credits" };

        private static readonly string[] CampusPanels =
            { "Toast", "QuestLog", "Pause", "Dialogue", "Settings", "Result", "FormalEnding" };

        // ---- TouchCanvasScale ----

        [Test]
        public void ReferenceFor_TouchIsOneAndAHalfTimesLarger()
        {
            Assert.AreEqual(new Vector2(1920f, 1080f), TouchCanvasScale.ReferenceFor(false));
            Assert.AreEqual(new Vector2(1280f, 720f), TouchCanvasScale.ReferenceFor(true));
            Assert.AreEqual(1.5f, TouchCanvasScale.PointerReference.x / TouchCanvasScale.TouchReference.x, 1e-6f);
            Assert.AreEqual(1.5f, TouchCanvasScale.PointerReference.y / TouchCanvasScale.TouchReference.y, 1e-6f);
        }

        // ---- FitInCanvas ----

        [Test]
        public void ScaleToFit_LeavesAPanelThatFitsAlone()
        {
            Assert.AreEqual(1f, FitInCanvas.ScaleToFit(new Vector2(820f, 560f), new Vector2(1412f, 652f), 16f));
        }

        [Test]
        public void ScaleToFit_ShrinksATallPanelToTheHeight()
        {
            // 横持ちのスマホでのクエストログ。高さ 700 を 652 - 32 に収める。
            float scale = FitInCanvas.ScaleToFit(new Vector2(1000f, 700f), new Vector2(1412f, 652f), 16f);
            Assert.AreEqual(620f / 700f, scale, 1e-5f);
        }

        [Test]
        public void ScaleToFit_ShrinksAWidePanelToTheWidth()
        {
            // 4:3 のタブレットでの会話の枠。幅 1520 を 1108 - 32 に収める。
            float scale = FitInCanvas.ScaleToFit(new Vector2(1520f, 300f), new Vector2(1108f, 831f), 16f);
            Assert.AreEqual(1076f / 1520f, scale, 1e-5f);
        }

        [Test]
        public void ScaleToFit_UsesTheTighterSide()
        {
            float scale = FitInCanvas.ScaleToFit(new Vector2(1240f, 800f), new Vector2(1032f, 632f), 16f);
            Assert.AreEqual(600f / 800f, scale, 1e-5f);
        }

        [TestCase(0f, 100f, 1000f, 600f)]
        [TestCase(100f, 0f, 1000f, 600f)]
        [TestCase(100f, 100f, 0f, 600f)]
        [TestCase(100f, 100f, 20f, 20f)]
        public void ScaleToFit_LeavesTheScaleAloneWhenSizesAreNotReady(float w, float h, float cw, float ch)
        {
            Assert.AreEqual(1f, FitInCanvas.ScaleToFit(new Vector2(w, h), new Vector2(cw, ch), 16f));
        }

        // ---- シーン ----

        [Test]
        public void TitleScene_CanvasesFollowTouchAndPanelsFit()
        {
            CheckScene(TitleScenePath, TitlePanels);
        }

        [Test]
        public void CampusScene_CanvasesFollowTouchAndPanelsFit()
        {
            CheckScene(CampusScenePath, CampusPanels);
        }

        private static void CheckScene(string path, IEnumerable<string> panels)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                List<GameObject> roots = scene.GetRootGameObjects().ToList();
                List<CanvasScaler> scalers = roots.SelectMany(r => r.GetComponentsInChildren<CanvasScaler>(true)).ToList();
                Assert.IsNotEmpty(scalers, path + " に Canvas が無い（KCD/シーンを組み直す）");

                foreach (CanvasScaler scaler in scalers)
                {
                    bool follows = scaler.GetComponent<TouchCanvasScale>() != null;
                    if (scaler.name == TouchCanvasName)
                    {
                        Assert.IsFalse(follows, path + ": " + scaler.name + " は最初から指の基準で作る");
                    }
                    else
                    {
                        Assert.IsTrue(follows, path + ": " + scaler.name + " に TouchCanvasScale が無い（KCD/シーンを組み直す）");
                    }
                }

                List<RectTransform> rects = roots.SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();
                foreach (string panel in panels)
                {
                    RectTransform rect = rects.FirstOrDefault(r => r.name == panel && r.Find("Backdrop") != null);
                    Assert.IsNotNull(rect, path + " に " + panel + " の枠が無い");
                    Assert.IsNotNull(rect.GetComponent<FitInCanvas>(), path + ": " + panel + " に FitInCanvas が無い（KCD/シーンを組み直す）");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
