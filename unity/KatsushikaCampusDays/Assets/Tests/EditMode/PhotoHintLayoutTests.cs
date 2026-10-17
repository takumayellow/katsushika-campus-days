using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// フォトモードの操作案内の置き場所 (#187)。指で遊ぶ横長の画面では、下の真ん中に自分の人物が立ち、右上に「戻る」と
    /// 「案内」、右下に「撮影」、左にスティックの丸が出る。案内はそのどれにもかからない左上に置く。縦長の画面では
    /// 上のボタンの並びより上に置く。画面の大きさごとに、シーンの Canvas の設定から画面の px での四角を求めて比べる。
    /// </summary>
    public sealed class PhotoHintLayoutTests
    {
        private const string CampusScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>
        /// 横長の画面で案内の下端を止める高さ（画面の上からの割合）。844×390 の写真モードでは、人物は上から 183〜308 px
        /// （0.47〜0.79）に立つ。画角は高さで決まるので、横長の画面なら縦横比が変わっても同じ高さに立つ。
        /// </summary>
        private const float PlayerTop = 0.4f;

        /// <summary>写真モードの間に出ている丸いボタン（戻る・案内・撮影）。</summary>
        private static readonly string[] PhotoButtons = { "PhotoButton", "PhotoHintButton", "Jump" };

        [Test]
        public void ShapeFor_FollowsTheButtonsAndTheScreen()
        {
            Assert.AreEqual(PhotoHintLayout.Shape.Pointer, PhotoHintLayout.ShapeFor(false, false));
            Assert.AreEqual(PhotoHintLayout.Shape.Pointer, PhotoHintLayout.ShapeFor(false, true));
            Assert.AreEqual(PhotoHintLayout.Shape.LandscapeTouch, PhotoHintLayout.ShapeFor(true, false));
            Assert.AreEqual(PhotoHintLayout.Shape.Portrait, PhotoHintLayout.ShapeFor(true, true));
        }

        [Test]
        public void CampusScene_BuildsTheHintForThePointer()
        {
            WithScene(photo =>
            {
                PhotoHintLayout.Slot pointer = PhotoHintLayout.SlotFor(PhotoHintLayout.Shape.Pointer);
                Assert.AreEqual(pointer.Anchor, photo.Hint.anchorMin);
                Assert.AreEqual(pointer.Anchor, photo.Hint.anchorMax);
                Assert.AreEqual(pointer.Anchor, photo.Hint.pivot);
                Assert.AreEqual(pointer.Position, photo.Hint.anchoredPosition);
                Assert.AreEqual(pointer.Size, photo.Hint.sizeDelta);

                var fit = new SerializedObject(photo.Fit);
                Assert.IsTrue(fit.FindProperty("_reflow").boolValue, "縦長の画面で案内の幅を狭めない（KCD/シーンを組み直す）");
                Assert.IsFalse(fit.FindProperty("_portraitOnly").boolValue);
                Assert.AreEqual(PhotoHintLayout.SlotFor(PhotoHintLayout.Shape.Portrait).Size.y, photo.PortraitHeight, 0.01f,
                    "縦長の画面での案内の高さが PhotoHintLayout の表と違う");
            });
        }

        [TestCase(844f, 390f)]
        [TestCase(667f, 375f)]
        [TestCase(915f, 412f)]
        [TestCase(960f, 640f)]
        [TestCase(1024f, 768f)]
        public void LandscapeTouch_StaysClearOfTheButtonsAndThePlayer(float width, float height)
        {
            WithScene(photo => AssertLandscape(photo, new Vector2(width, height)));
        }

        [Test]
        public void LandscapeTouch_StaysClearFromSquareToNarrowScreens()
        {
            // 正方形に近い横長（折りたたみのスマホの内側の画面）から、縦横比 2.4 の細長いスマホまで。
            WithScene(photo =>
            {
                for (int i = 0; i <= 28; i++)
                {
                    AssertLandscape(photo, new Vector2(390f * (1f + 0.05f * i), 390f));
                }
            });
        }

        [TestCase(390f, 844f)]
        [TestCase(360f, 800f)]
        [TestCase(430f, 932f)]
        public void Portrait_StaysAboveTheTopButtons(float width, float height)
        {
            WithScene(photo =>
            {
                var screen = new Vector2(width, height);
                string at = " (" + width + "x" + height + ")";
                Rect hint = HintOnScreen(photo, PhotoHintLayout.Shape.Portrait, screen);
                CanvasScreen.AssertInside(screen, hint, "案内が画面からはみ出す" + at);
                foreach (RectTransform button in photo.Buttons.Append(photo.Stick))
                {
                    Rect place = CanvasScreen.OnScreen(photo.TouchScaler, button, screen, true);
                    CanvasScreen.AssertInside(screen, place, button.name + " が画面からはみ出す" + at);
                    Assert.IsFalse(hint.Overlaps(place), "案内が " + button.name + " にかかる" + at);
                }

                // 上の並び（戻る・案内）より上で止まる。y は画面の下から。
                foreach (RectTransform button in photo.Buttons.Where(b => b.name != "Jump"))
                {
                    Assert.Greater(hint.yMin, CanvasScreen.OnScreen(photo.TouchScaler, button, screen, true).yMax,
                        "案内が " + button.name + " の上端より下に来る" + at);
                }
            });
        }

        [TestCase(PhotoHintLayout.Shape.LandscapeTouch, 844f, 390f)]
        [TestCase(PhotoHintLayout.Shape.Portrait, 390f, 844f)]
        public void TouchText_FitsAtFullSizeWithoutWrapping(PhotoHintLayout.Shape shape, float width, float height)
        {
            WithScene(photo =>
            {
                Vector2 inner = HintSize(photo, shape, new Vector2(width, height)) + photo.Label.rectTransform.sizeDelta;
                photo.Label.enableAutoSizing = false;
                photo.Label.fontSize = photo.Label.fontSizeMax;
                foreach (bool english in new[] { false, true })
                {
                    string text = PhotoModeHints.Text(english, false, true);
                    float widest = photo.Label.GetPreferredValues(text).x;
                    Assert.LessOrEqual(widest, inner.x,
                        shape + ": 指の文の行が折り返す（いちばん長い行 " + widest + "、文字の入る幅 " + inner.x + "）");
                    float tall = photo.Label.GetPreferredValues(text, inner.x, 0f).y;
                    Assert.LessOrEqual(tall, inner.y,
                        shape + ": 指の文が縮む（要る高さ " + tall + "、文字の入る高さ " + inner.y + "）");
                }
            });
        }

        [Test]
        public void EveryText_FitsEveryBoxAtTheSmallestSize()
        {
            // 写真モードの途中で入力を変えると、枠と文が別々に切り替わることがある（指の文が PC の枠に入る）。
            // どの組み合わせでも、いちばん小さくすれば … で切らずに入る。
            var boxes = new Dictionary<PhotoHintLayout.Shape, Vector2>
            {
                { PhotoHintLayout.Shape.Pointer, new Vector2(1920f, 1080f) },
                { PhotoHintLayout.Shape.LandscapeTouch, new Vector2(844f, 390f) },
                { PhotoHintLayout.Shape.Portrait, new Vector2(390f, 844f) },
            };
            WithScene(photo =>
            {
                Assert.IsTrue(photo.Label.enableAutoSizing, "長い案内を縮めない（KCD/シーンを組み直す）");
                photo.Label.enableAutoSizing = false;
                photo.Label.fontSize = photo.Label.fontSizeMin;
                foreach (KeyValuePair<PhotoHintLayout.Shape, Vector2> box in boxes)
                {
                    Vector2 inner = HintSize(photo, box.Key, box.Value) + photo.Label.rectTransform.sizeDelta;
                    foreach (bool english in new[] { false, true })
                    {
                        foreach (bool gamepad in new[] { false, true })
                        {
                            foreach (bool touch in new[] { false, true })
                            {
                                string text = PhotoModeHints.Text(english, gamepad, touch);
                                float tall = photo.Label.GetPreferredValues(text, inner.x, 0f).y;
                                Assert.LessOrEqual(tall, inner.y, box.Key + " の枠に入らない（要る高さ " + tall +
                                    "、文字の入る高さ " + inner.y + "）: " + text);
                            }
                        }
                    }
                }
            });
        }

        private static void AssertLandscape(Photo photo, Vector2 screen)
        {
            string at = " (" + screen.x + "x" + screen.y + ")";
            Rect hint = HintOnScreen(photo, PhotoHintLayout.Shape.LandscapeTouch, screen);
            CanvasScreen.AssertInside(screen, hint, "案内が画面からはみ出す" + at);
            foreach (RectTransform button in photo.Buttons)
            {
                Rect place = CanvasScreen.OnScreen(photo.TouchScaler, button, screen, true);
                CanvasScreen.AssertInside(screen, place, button.name + " が画面からはみ出す" + at);
                Assert.IsFalse(hint.Overlaps(place), "案内が " + button.name + " にかかる" + at);
            }

            // y は画面の下から。スティックの丸（指を置く前の位置）と人物の頭より上で止まる。
            Assert.Greater(hint.yMin, CanvasScreen.OnScreen(photo.TouchScaler, photo.Stick, screen, true).yMax,
                "案内がスティックの丸にかかる" + at);
            Assert.GreaterOrEqual(hint.yMin, screen.y * (1f - PlayerTop), "案内が人物にかかる" + at);
        }

        /// <summary>案内の枠の、画面の px での四角（左下が原点）。指で遊んでいるときの Canvas の大きさで置く。</summary>
        private static Rect HintOnScreen(Photo photo, PhotoHintLayout.Shape shape, Vector2 screen)
        {
            float factor = CanvasScreen.Factor(photo.HudScaler, screen, true);
            Vector2 canvas = screen / factor;
            Vector2 size = HintSize(photo, shape, screen);
            PhotoHintLayout.Slot slot = PhotoHintLayout.SlotFor(shape);
            Rect inCanvas = CanvasScreen.Place(new Rect(Vector2.zero, canvas), slot.Anchor, slot.Anchor, slot.Anchor,
                slot.Position, size, FitInCanvas.ScaleToFit(size, canvas, FitInCanvas.Margin));
            return new Rect(inCanvas.position * factor, inCanvas.size * factor);
        }

        /// <summary>
        /// 案内の枠の大きさ（倍率をかける前）。FitInCanvas と同じく、幅を Canvas に合わせて狭め、縦長の画面では高さを
        /// 差し替える。
        /// </summary>
        private static Vector2 HintSize(Photo photo, PhotoHintLayout.Shape shape, Vector2 screen)
        {
            bool portrait = ScreenShape.IsPortraitSize(screen.x, screen.y);
            Vector2 canvas = screen / CanvasScreen.Factor(photo.HudScaler, screen, shape != PhotoHintLayout.Shape.Pointer);
            Vector2 size = PhotoHintLayout.SlotFor(shape).Size;
            float height = portrait && photo.PortraitHeight > 0f ? photo.PortraitHeight : size.y;
            return new Vector2(FitInCanvas.WidthToFit(size.x, canvas.x, FitInCanvas.Margin), height);
        }

        private static void WithScene(Action<Photo> body)
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<RectTransform> rects = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();

                RectTransform hint = rects.FirstOrDefault(r => r.name == "PhotoHint");
                Assert.IsNotNull(hint, "PhotoHint が無い");
                Assert.IsTrue(hint.parent.TryGetComponent(out CanvasScaler hudScaler), "PhotoHint が Canvas の直下にない");
                Assert.IsTrue(hint.TryGetComponent(out FitInCanvas fit), "PhotoHint に FitInCanvas が無い（KCD/シーンを組み直す）");
                Transform text = hint.Find("Label");
                Assert.IsNotNull(text, "PhotoHint に文字が無い");
                Assert.IsTrue(text.TryGetComponent(out TMP_Text label), "PhotoHint に文字が無い");

                RectTransform touch = rects.FirstOrDefault(r => r.name == "TouchCanvas");
                Assert.IsNotNull(touch, "TouchCanvas が無い");
                Assert.IsTrue(touch.TryGetComponent(out CanvasScaler touchScaler), "TouchCanvas に CanvasScaler が無い");
                RectTransform[] buttons = PhotoButtons
                    .Select(name => rects.FirstOrDefault(r => r.name == name && r.IsChildOf(touch)))
                    .ToArray();
                for (int i = 0; i < buttons.Length; i++)
                {
                    Assert.IsNotNull(buttons[i], PhotoButtons[i] + " が無い（KCD/シーンを組み直す）");
                }

                RectTransform stick = rects.FirstOrDefault(r =>
                    r.name == "Base" && r.parent != null && r.parent.name == "StickArea" && r.IsChildOf(touch));
                Assert.IsNotNull(stick, "スティックの丸が無い");

                float portraitHeight = new SerializedObject(fit).FindProperty("_portraitHeight").floatValue;
                body(new Photo(hint, fit, label, hudScaler, touchScaler, buttons, stick, portraitHeight));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Campus シーンの、写真モードで画面に出ているもの。</summary>
        private sealed class Photo
        {
            public readonly RectTransform Hint;
            public readonly FitInCanvas Fit;
            public readonly TMP_Text Label;
            public readonly CanvasScaler HudScaler;
            public readonly CanvasScaler TouchScaler;
            public readonly RectTransform[] Buttons;
            public readonly RectTransform Stick;
            public readonly float PortraitHeight;

            public Photo(RectTransform hint, FitInCanvas fit, TMP_Text label, CanvasScaler hudScaler, CanvasScaler touchScaler,
                RectTransform[] buttons, RectTransform stick, float portraitHeight)
            {
                Hint = hint;
                Fit = fit;
                Label = label;
                HudScaler = hudScaler;
                TouchScaler = touchScaler;
                Buttons = buttons;
                Stick = stick;
                PortraitHeight = portraitHeight;
            }
        }
    }
}
