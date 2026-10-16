using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
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

        /// <summary>指のときの専用 Canvas。最初から 1280×720 基準で作っているので、横長の画面では入力で切り替えない。</summary>
        private const string TouchCanvasName = "TouchCanvas";

        /// <summary>縦持ちのスマホ（CSS px）。</summary>
        private const float PhoneWidth = 390f;
        private const float PhoneHeight = 844f;

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

        [Test]
        public void PortraitReference_FollowsTheWidth()
        {
            Assert.AreEqual(new Vector2(800f, 1600f), TouchCanvasScale.PortraitReference(TouchCanvasScale.PortraitWidth));
            Assert.AreEqual(new Vector2(720f, 1440f), TouchCanvasScale.PortraitReference(TouchCanvasScale.TouchPortraitWidth));
        }

        [Test]
        public void PortraitWidth_KeepsTheDialogueTextReadable()
        {
            // 縦持ちのスマホで、会話の 32 の文字が 15 px 以上になる。
            Assert.GreaterOrEqual(32f * PhoneWidth / TouchCanvasScale.PortraitWidth, 15f);
        }

        [Test]
        public void TouchPortraitWidth_KeepsTheButtonsAsLargeAsInLandscape()
        {
            // 横持ちは短い辺 390 を TouchReference.y で、縦持ちは同じ 390 を TouchPortraitWidth で描く。
            Assert.AreEqual(TouchCanvasScale.TouchReference.y, TouchCanvasScale.TouchPortraitWidth,
                "縦と横でスマホの短い辺を描く幅が違うと、ボタンの大きさが変わる");
        }

        // ---- ScreenShape ----

        [TestCase(390, 844, true)]
        [TestCase(844, 390, false)]
        [TestCase(1920, 1080, false)]
        [TestCase(800, 800, false)]
        public void IsPortraitSize_IsTrueOnlyWhenTaller(int width, int height, bool expected)
        {
            Assert.AreEqual(expected, ScreenShape.IsPortraitSize(width, height));
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

        [Test]
        public void WidthToFit_NarrowsAPanelToTheCanvas()
        {
            // 縦持ちのスマホでの会話の枠。幅 1520 を 800 - 32 にする。
            Assert.AreEqual(768f, FitInCanvas.WidthToFit(1520f, 800f, 16f), 1e-5f);
        }

        [Test]
        public void WidthToFit_LeavesANarrowPanelAlone()
        {
            Assert.AreEqual(600f, FitInCanvas.WidthToFit(600f, 1412f, 16f), 1e-5f);
        }

        [TestCase(0f)]
        [TestCase(20f)]
        public void WidthToFit_LeavesTheWidthAloneWhenTheCanvasIsNotReady(float canvasWidth)
        {
            Assert.AreEqual(1520f, FitInCanvas.WidthToFit(1520f, canvasWidth, 16f), 1e-5f);
        }

        // ---- KeepPointerSize ----

        [Test]
        public void KeepPointerSize_Factor_CancelsTheTouchEnlargement()
        {
            Assert.AreEqual(1f, KeepPointerSize.Factor(false));
            Assert.AreEqual(1f, KeepPointerSize.Factor(true) * 1.5f, 1e-6f);
        }

        [Test]
        public void KeepPointerSize_Factor_ShrinksOnAPortraitScreen()
        {
            Assert.AreEqual(1f, KeepPointerSize.Factor(false, false));
            Assert.AreEqual(KeepPointerSize.Factor(true), KeepPointerSize.Factor(false, true));
            Assert.AreEqual(KeepPointerSize.Factor(true), KeepPointerSize.Factor(true, true));
        }

        // ---- KeepLeftOfCenter ----

        [Test]
        public void ScaleToShare_LeavesTheTrackerAloneOnAWideScreen()
        {
            // 横持ちのスマホ（Canvas 幅 1412）。左端から 40 + 520 = 560 まで届いても半分に届かない。
            Assert.AreEqual(1f, KeepLeftOfCenter.ScaleToShare(560f, 1412f, KeepLeftOfCenter.Share));
        }

        [Test]
        public void ScaleToShare_ShrinksTheTrackerOnAFourByThreeScreen()
        {
            // 4:3 のタブレット（Canvas 幅 1108）。真ん中のポーズのボタンの手前で止める。
            float scale = KeepLeftOfCenter.ScaleToShare(560f, 1108f, KeepLeftOfCenter.Share);
            Assert.AreEqual(1108f * KeepLeftOfCenter.Share / 560f, scale, 1e-5f);
            Assert.Less(scale, 1f);
        }

        [TestCase(0f, 1108f)]
        [TestCase(560f, 0f)]
        public void ScaleToShare_LeavesTheScaleAloneWhenSizesAreNotReady(float extent, float width)
        {
            Assert.AreEqual(1f, KeepLeftOfCenter.ScaleToShare(extent, width, KeepLeftOfCenter.Share));
        }

        // ---- TouchPosition ----

        [Test]
        public void TouchPosition_PositionFor_RaisesOnlyAboveTheButtons()
        {
            var pointer = new Vector2(0f, 96f);
            var touch = new Vector2(0f, 262f);
            Assert.AreEqual(pointer, TouchPosition.PositionFor(false, pointer, touch));
            Assert.AreEqual(touch, TouchPosition.PositionFor(true, pointer, touch));
        }

        [Test]
        public void TouchPosition_PositionFor_UsesThePortraitPlaceOnlyAboveTheButtons()
        {
            var pointer = new Vector2(0f, 96f);
            var touch = new Vector2(0f, 262f);
            var portrait = new Vector2(0f, 452f);
            Assert.AreEqual(pointer, TouchPosition.PositionFor(false, true, pointer, touch, portrait));
            Assert.AreEqual(touch, TouchPosition.PositionFor(true, false, pointer, touch, portrait));
            Assert.AreEqual(portrait, TouchPosition.PositionFor(true, true, pointer, touch, portrait));
        }

        // ---- PortraitFieldOfView ----

        [Test]
        public void VerticalFor_LeavesALandscapeScreenAlone()
        {
            Assert.AreEqual(55f, PortraitFieldOfView.VerticalFor(55f, 16f / 9f, 50f, 80f));
            Assert.AreEqual(55f, PortraitFieldOfView.VerticalFor(55f, 1f, 50f, 80f));
        }

        [Test]
        public void VerticalFor_WidensATallScreenUpToTheLimit()
        {
            float aspect = PhoneWidth / PhoneHeight;
            float vertical = PortraitFieldOfView.VerticalFor(55f, aspect, 50f, 80f);
            Assert.AreEqual(80f, vertical, 1e-4f, "縦持ちのスマホでは上限まで広げる");

            // 上限の手前なら、横の画角がちょうど minHorizontal になる。
            float wide = PortraitFieldOfView.VerticalFor(42f, 0.9f, 50f, 80f);
            float horizontal = 2f * Mathf.Atan(Mathf.Tan(wide * 0.5f * Mathf.Deg2Rad) * 0.9f) * Mathf.Rad2Deg;
            Assert.AreEqual(50f, horizontal, 1e-3f);
        }

        [Test]
        public void VerticalFor_NeverNarrowsTheBaseView()
        {
            Assert.AreEqual(70f, PortraitFieldOfView.VerticalFor(70f, 0.95f, 50f, 80f));
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

        [Test]
        public void CampusScene_HudCornersStayOutOfTheButtons()
        {
            // 知らせと「話す」の案内は同時に出ることがある。間が詰まると一つの塊に見える（横 844×390 で CSS 14 px 以上）。
            const float minToastGap = 24f;
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<RectTransform> rects = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();
                RectTransform Find(string name) => rects.FirstOrDefault(r => r.name == name);

                Assert.IsNotNull(Find("Minimap")?.GetComponent<KeepPointerSize>(), "Minimap に KeepPointerSize が無い（KCD/シーンを組み直す）");
                Assert.IsNotNull(Find("Clock")?.GetComponent<KeepPointerSize>(), "Clock に KeepPointerSize が無い（KCD/シーンを組み直す）");
                Assert.IsNotNull(Find("QuestTracker")?.GetComponent<KeepLeftOfCenter>(), "QuestTracker に KeepLeftOfCenter が無い（KCD/シーンを組み直す）");
                TouchPosition toast = Find("Toast")?.GetComponent<TouchPosition>();
                Assert.IsNotNull(toast, "Toast に TouchPosition が無い（KCD/シーンを組み直す）");
                RectTransform prompt = Find("Prompt");
                Assert.IsNotNull(prompt, "Prompt が無い");
                float touchBottom = new SerializedObject(toast).FindProperty("_touchPosition").vector2Value.y;
                Assert.GreaterOrEqual(touchBottom - (prompt.anchoredPosition.y + prompt.sizeDelta.y), minToastGap,
                    "指のときの知らせが「話す」の案内にくっつく（場所が入っていない）");

                TouchPosition promptTouch = prompt.GetComponent<TouchPosition>();
                Assert.IsNotNull(promptTouch, "Prompt に TouchPosition が無い（KCD/シーンを組み直す）");
                float portraitPrompt = new SerializedObject(promptTouch).FindProperty("_portraitTouchPosition").vector2Value.y;
                float portraitToast = new SerializedObject(toast).FindProperty("_portraitTouchPosition").vector2Value.y;
                Assert.Greater(portraitPrompt, prompt.anchoredPosition.y, "縦長の画面で「話す」の案内がボタンの上へ逃げない");
                Assert.GreaterOrEqual(portraitToast - (portraitPrompt + prompt.sizeDelta.y), minToastGap,
                    "縦長の画面で知らせが「話す」の案内にくっつく");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void CampusScene_PhotoModeKeepsOnlyTheCameraButtons()
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<RectTransform> rects = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();
                RectTransform extras = rects.FirstOrDefault(r => r.name == "Extras" && r.parent != null && r.parent.name == "Play");
                Assert.IsNotNull(extras, "Play/Extras が無い（KCD/シーンを組み直す）");
                Assert.IsNotNull(extras.GetComponent<CanvasGroup>());

                // 写真モードで隠すボタン。
                foreach (string button in new[] { "QuestLogButton", "Interact", "Sprint" })
                {
                    RectTransform rect = rects.FirstOrDefault(r => r.name == button);
                    Assert.IsNotNull(rect, button + " が無い");
                    Assert.IsTrue(rect.IsChildOf(extras), button + " が写真モードでも出る");
                }

                // 写真モードで使うボタン。ジャンプが撮影、写真が戻る。
                foreach (string button in new[] { "Jump", "PhotoButton", "StickArea", "LookArea" })
                {
                    RectTransform rect = rects.FirstOrDefault(r => r.name == button && r.IsChildOf(extras.parent));
                    Assert.IsNotNull(rect, button + " が Play に無い");
                    Assert.IsFalse(rect.IsChildOf(extras), button + " が写真モードで隠れる");
                }

                // 写真モードの間だけ出すボタン。操作の案内を出し入れする（L3 と同じ口）。
                RectTransform photoExtras = rects.FirstOrDefault(r => r.name == "PhotoExtras" && r.parent == extras.parent);
                Assert.IsNotNull(photoExtras, "Play/PhotoExtras が無い（KCD/シーンを組み直す）");
                Assert.IsTrue(photoExtras.TryGetComponent(out CanvasGroup photoExtrasGroup), "PhotoExtras に CanvasGroup が無い");
                RectTransform hintButton = rects.FirstOrDefault(r => r.name == "PhotoHintButton");
                Assert.IsNotNull(hintButton, "PhotoHintButton が無い");
                Assert.IsTrue(hintButton.IsChildOf(photoExtras), "PhotoHintButton が写真モードの外でも出る");
                Assert.IsTrue(hintButton.TryGetComponent(out OnScreenButton hintPress), "PhotoHintButton を押しても何も入らない");
                Assert.AreEqual("<Gamepad>/leftStickPress", hintPress.controlPath, "PhotoHintButton が案内の出し入れにつながっていない");
                Transform hintText = hintButton.Find("Label");
                Assert.IsNotNull(hintText, "PhotoHintButton に文字が無い");
                Assert.IsTrue(hintText.TryGetComponent(out TMP_Text hintLabel), "PhotoHintButton に文字が無い");

                TouchHud hud = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TouchHud>(true)).FirstOrDefault();
                Assert.IsNotNull(hud, "TouchHud が無い");
                var bound = new SerializedObject(hud);
                Assert.AreEqual(photoExtrasGroup, bound.FindProperty("_photoExtras").objectReferenceValue,
                    "TouchHud が写真モードの間だけのボタンを出し入れしない（KCD/シーンを組み直す）");
                Assert.AreEqual(hintLabel, bound.FindProperty("_hintLabel").objectReferenceValue,
                    "TouchHud が案内のボタンに文字を入れない（KCD/シーンを組み直す）");

                // 案内の枠の置き場所は PhotoHintLayout だけが決める。TouchPosition も付いていると置き場所を取り合う。
                RectTransform hint = rects.FirstOrDefault(r => r.name == "PhotoHint");
                Assert.IsNotNull(hint, "PhotoHint が無い");
                Assert.IsTrue(hint.TryGetComponent(out PhotoHintLayout _),
                    "PhotoHint に PhotoHintLayout が無い（指のとき撮影のボタンや人物に重なる）");
                Assert.IsFalse(hint.TryGetComponent(out TouchPosition _), "PhotoHint の置き場所を TouchPosition と取り合う");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void CampusScene_IsReadyForAPortraitScreen()
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<GameObject> roots = scene.GetRootGameObjects().ToList();
                List<RectTransform> rects = roots.SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();

                foreach (string button in new[] { "PauseButton", "QuestLogButton", "PhotoButton", "PhotoHintButton" })
                {
                    RectTransform rect = rects.FirstOrDefault(r => r.name == button);
                    Assert.IsNotNull(rect, button + " が無い");
                    Assert.IsTrue(rect.TryGetComponent(out PortraitPosition _),
                        button + " に PortraitPosition が無い（縦長の画面でクエストの枠に重なる）");
                }

                foreach (string panel in new[] { "Dialogue", "Toast" })
                {
                    FitInCanvas fit = rects.FirstOrDefault(r => r.name == panel && r.Find("Backdrop") != null)
                        ?.GetComponent<FitInCanvas>();
                    Assert.IsNotNull(fit, panel + " に FitInCanvas が無い");
                    Assert.IsTrue(new SerializedObject(fit).FindProperty("_reflow").boolValue,
                        panel + " が縦長の画面で折り返さない（縮めると文字が読めない）");
                }

                Assert.IsNotEmpty(roots.SelectMany(r => r.GetComponentsInChildren<PortraitFieldOfView>(true)),
                    "カメラに PortraitFieldOfView が無い（縦長の画面で左右が見えない）");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void CampusScene_TopButtonsLeaveAGapBetweenThem()
        {
            // 上端に並ぶポーズ・クエストログ・写真の丸と、写真モードの間だけ出る案内の丸の間が狭いと、隣を押し間違える
            // （縦横とも CSS 14 px 以上）。
            const float minGap = 26f;
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<RectTransform> rects = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();
                RectTransform[] buttons = new[] { "PauseButton", "QuestLogButton", "PhotoButton" }
                    .Select(name => rects.First(r => r.name == name)).ToArray();

                for (int i = 1; i < buttons.Length; i++)
                {
                    RectTransform left = buttons[i - 1];
                    RectTransform right = buttons[i];
                    float edges = (left.sizeDelta.x + right.sizeDelta.x) * 0.5f;

                    float landscape = right.anchoredPosition.x - left.anchoredPosition.x - edges;
                    Assert.GreaterOrEqual(landscape, minGap, left.name + " と " + right.name + " が横長の画面で近すぎる");

                    float portrait = PortraitPlace(right).x - PortraitPlace(left).x - edges;
                    Assert.GreaterOrEqual(portrait, minGap, left.name + " と " + right.name + " が縦長の画面で近すぎる");
                }

                // 案内の丸は、横長の画面では写真の真下、縦長の画面では写真の右隣。
                RectTransform photo = buttons[buttons.Length - 1];
                RectTransform hint = rects.First(r => r.name == "PhotoHintButton");
                Assert.AreEqual(photo.anchorMin, hint.anchorMin, "案内の丸が写真の丸と別の所を基準に置かれている");
                Assert.AreEqual(photo.anchoredPosition.x, hint.anchoredPosition.x, 0.01f, "横長の画面で案内の丸が写真の真下にない");
                Assert.GreaterOrEqual(photo.anchoredPosition.y - hint.anchoredPosition.y - (photo.sizeDelta.y + hint.sizeDelta.y) * 0.5f,
                    minGap, "写真と案内の丸が横長の画面で近すぎる");

                Vector2 photoPortrait = PortraitPlace(photo);
                Vector2 hintPortrait = PortraitPlace(hint);
                Assert.AreEqual(photoPortrait.y, hintPortrait.y, 0.01f, "縦長の画面で案内の丸が写真の右隣にない");
                Assert.GreaterOrEqual(hintPortrait.x - photoPortrait.x - (photo.sizeDelta.x + hint.sizeDelta.x) * 0.5f,
                    minGap, "写真と案内の丸が縦長の画面で近すぎる");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Vector2 PortraitPlace(RectTransform button)
        {
            return new SerializedObject(button.GetComponent<PortraitPosition>())
                .FindProperty("_portraitPosition").vector2Value;
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
                    TouchCanvasScale scale = scaler.GetComponent<TouchCanvasScale>();
                    Assert.IsNotNull(scale, path + ": " + scaler.name + " に TouchCanvasScale が無い（KCD/シーンを組み直す）");
                    bool follows = new SerializedObject(scale).FindProperty("_followTouch").boolValue;
                    Assert.AreEqual(scaler.name != TouchCanvasName, follows,
                        path + ": " + scaler.name + (follows ? " は最初から指の基準で作る" : " が指の操作に合わせて大きくならない"));
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
