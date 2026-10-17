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
    /// 知らせ（トースト）の置き場所と文字 (#187)。指で遊ぶ横長の画面では、下の真ん中に自分の人物が立ち、右上にミニマップと
    /// 時計、左上にクエストの追跡、右下と上の真ん中に指のボタンが出る。知らせはそのどれにもかからない人物の頭の上に置く。
    /// 縦長の画面では「話す」の案内の上に置く。枠の高さは 60 で、長い知らせは 22 まで縮めて枠に収める。
    /// 画面の大きさごとに、シーンの Canvas の設定から画面の px での四角を求めて比べる。
    /// </summary>
    public sealed class ToastLayoutTests
    {
        private const string CampusScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>縮めてよい下限。横持ちのスマホ（844×390、1 単位 CSS 約 0.6 px）で 13 px 前後。</summary>
        private const float SmallestReadableSize = 22f;

        /// <summary>
        /// 知らせと「話す」の案内の間（Canvas の単位）。2 つは同時に出ることがあり、間が詰まると一つの塊に見える
        /// （横 844×390 で CSS 約 14 px）。
        /// </summary>
        private const float PromptGap = 24f;

        /// <summary>
        /// 横長の画面で知らせの下端を止める高さ（画面の上からの割合）。844×390 では人物の頭は上から 182 px（0.467）。
        /// 画角は高さで決まるので、横長の画面なら縦横比が変わっても同じ高さに立つ。
        /// </summary>
        private const float HeadTop = 0.46f;

        /// <summary>1 行で出る知らせ（開始直後の地名）。</summary>
        private const string OneLine = "東京理科大学 葛飾キャンパス";

        /// <summary>幅 1044 では 22 でも 2 行に折り返す長さ（指のときの操作の案内を 2 つつないだもの）。</summary>
        private const string TwoLines =
            "左側をなぞって移動　右側をなぞって見回す　2 本指で寄る・引く　人の近くで「話す」　会話は画面を叩いて進める";

        /// <summary>指で遊ぶ人に最初に出す知らせ（CampusDirector.ShowWelcome の日本語と英語）。</summary>
        private static readonly string[] TouchWelcome =
        {
            "東京理科大学 葛飾キャンパス",
            "Tokyo University of Science, Katsushika Campus",
            "左側をなぞって移動　右側をなぞって見回す　2 本指で寄る・引く",
            "Drag left to move   Drag right to look   Pinch to zoom",
            "人の近くで「話す」　会話は画面を叩いて進める",
            "Tap Talk near people   Tap the box to continue",
        };

        /// <summary>
        /// 知らせと一緒に出ている指のボタン。スティックの丸は指を置いている間だけ指の所に出るので、決まった場所では比べない。
        /// 写真モードの案内のボタンは、写真モードの間は知らせも HUD ごと隠れるので比べない。
        /// </summary>
        private static readonly string[] PlayButtons =
            { "PauseButton", "QuestLogButton", "PhotoButton", "Jump", "Interact", "Sprint" };

        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
        }

        [Test]
        public void ShapeFor_FollowsTheButtonsAndTheScreen()
        {
            Assert.AreEqual(ToastLayout.Shape.Pointer, ToastLayout.ShapeFor(false, false));
            Assert.AreEqual(ToastLayout.Shape.Pointer, ToastLayout.ShapeFor(false, true));
            Assert.AreEqual(ToastLayout.Shape.LandscapeTouch, ToastLayout.ShapeFor(true, false));
            Assert.AreEqual(ToastLayout.Shape.Portrait, ToastLayout.ShapeFor(true, true));
        }

        [Test]
        public void LandscapeWidth_StopsShortOfTheRightColumn()
        {
            Assert.AreEqual(926.86f, ToastLayout.LandscapeWidth(479.43f), 1e-3f, "右の列の Margin 手前で止まらない");
            Assert.AreEqual(1080f, ToastLayout.LandscapeWidth(10000f), "列が遠いと元の幅より広がる");
            Assert.AreEqual(FitInCanvas.WidthToFit(1080f, 960f, FitInCanvas.Margin), ToastLayout.LandscapeWidth(480f), 1e-3f,
                "列が無い（右端が Canvas の端）ときに、FitInCanvas と同じく端から Margin 空けない");
        }

        [TestCase(16f)]
        [TestCase(0f)]
        [TestCase(-5f)]
        public void LandscapeWidth_KeepsTheWidthWhenTheColumnReachesTheMiddle(float columnLeft)
        {
            // Canvas の大きさが決まる前は、右の列の左端が真ん中か、それより左に来る。そのときは狭めずに元の幅で置く。
            Assert.AreEqual(ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch).Size.x, ToastLayout.LandscapeWidth(columnLeft));
        }

        [Test]
        public void ColumnLeft_MeasuresThePlacedAndScaledColumn()
        {
            float scale = KeepPointerSize.Factor(true);
            RectTransform gameplay = Make(MakeCanvas(new Vector2(1412.2f, 652.6f)), "Gameplay", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 1f);
            RectTransform minimap = Make(gameplay, "Minimap", Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-40f, -40f) * scale, new Vector2(300f, 300f), scale);
            Assert.AreEqual(479.433f, ToastLayout.ColumnLeft(gameplay, new[] { minimap }), 1e-3f);
            Assert.AreEqual(479.433f, ToastLayout.ColumnLeft(gameplay, new[] { null, minimap }), 1e-3f);
            Assert.AreEqual(706.1f, ToastLayout.ColumnLeft(gameplay, new RectTransform[0]), 1e-3f);

            // 名前の枠が列より広いと、その左端で止まる。
            RectTransform caption = Make(minimap, "Caption", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(420f, 44f), 1f);
            Assert.AreEqual(439.433f, ToastLayout.ColumnLeft(gameplay, new[] { minimap, caption }), 1e-3f);
        }

        [Test]
        public void CampusScene_BuildsTheToastForThePointer()
        {
            WithScene(hud =>
            {
                ToastLayout.Slot pointer = ToastLayout.SlotFor(ToastLayout.Shape.Pointer);
                Assert.AreEqual(pointer.Anchor, hud.Toast.anchorMin);
                Assert.AreEqual(pointer.Anchor, hud.Toast.anchorMax);
                Assert.AreEqual(ToastLayout.Pivot, hud.Toast.pivot);
                Assert.AreEqual(pointer.Position, hud.Toast.anchoredPosition);
                Assert.AreEqual(pointer.Size, hud.Toast.sizeDelta);

                // 置き場所は ToastLayout だけが決める。TouchPosition も付いていると置き場所を取り合う。
                Assert.IsFalse(hud.Toast.TryGetComponent(out TouchPosition _), "Toast に TouchPosition が付いている（KCD/シーンを組み直す）");
                SerializedProperty column = new SerializedObject(hud.Layout).FindProperty("_rightColumn");
                UnityEngine.Object[] bound = Enumerable.Range(0, column.arraySize)
                    .Select(i => column.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                CollectionAssert.AreEqual(new UnityEngine.Object[] { hud.Minimap, hud.Caption, hud.Clock }, bound,
                    "知らせが避ける右の列が、ミニマップ・その下の名前・時計と違う（KCD/シーンを組み直す）");

                var fit = new SerializedObject(hud.Fit);
                Assert.IsTrue(fit.FindProperty("_reflow").boolValue, "縦長の画面で知らせの幅を狭めない（KCD/シーンを組み直す）");
                Assert.IsFalse(fit.FindProperty("_portraitOnly").boolValue, "横長の画面で知らせの幅を Canvas に合わせない");
                Assert.Greater(hud.PortraitHeight, pointer.Size.y, "縦長の画面で、折り返した知らせの行が入る高さにしない");
            });
        }

        [TestCase(844f, 390f)]
        [TestCase(667f, 375f)]
        [TestCase(915f, 412f)]
        [TestCase(960f, 640f)]
        [TestCase(1024f, 768f)]
        public void LandscapeTouch_StaysClearOfTheHudTheButtonsAndThePlayer(float width, float height)
        {
            WithScene(hud => AssertLandscape(hud, new Vector2(width, height)));
        }

        [Test]
        public void LandscapeTouch_StaysClearFromSquareToNarrowScreens()
        {
            // 正方形に近い横長（折りたたみのスマホの内側の画面）から、縦横比 2.7 の細長い画面まで。
            WithScene(hud =>
            {
                for (int i = 0; i <= 34; i++)
                {
                    AssertLandscape(hud, new Vector2(390f * (1f + 0.05f * i), 390f));
                }
            });
        }

        [TestCase(390f, 844f)]
        [TestCase(360f, 800f)]
        [TestCase(430f, 932f)]
        public void Portrait_StaysAboveThePromptAndClearOfTheButtons(float width, float height)
        {
            WithScene(hud =>
            {
                var screen = new Vector2(width, height);
                string at = " (" + width + "x" + height + ")";
                float factor = CanvasScreen.Factor(hud.Scaler, screen, true);
                Vector2 canvas = screen / factor;
                RectTransform gameplay = Copy(hud.Gameplay, MakeCanvas(canvas), Vector2.zero, 1f);
                Rect prompt = OnScreen(Copy(hud.Prompt, gameplay, hud.PromptPortrait, 1f), factor);

                ToastLayout.Slot slot = ToastLayout.SlotFor(ToastLayout.Shape.Portrait);
                Vector2 size = PortraitSize(hud, canvas);
                Rect toast = OnScreen(Make(gameplay, "Toast", slot.Anchor, slot.Anchor, ToastLayout.Pivot, slot.Position, size,
                    FitInCanvas.ScaleToFit(size, canvas, FitInCanvas.Margin)), factor);
                CanvasScreen.AssertInside(screen, toast, "知らせが画面からはみ出す" + at);
                Assert.GreaterOrEqual(toast.yMin - prompt.yMax, PromptGap * factor - 0.01f, "知らせが「話す」の案内に詰まる" + at);
                AssertClearOfButtons(hud, screen, toast, at);
            });
        }

        [Test]
        public void CampusScene_ToastShrinksLongTextInsideItsBox()
        {
            WithScene(hud =>
            {
                TMP_Text label = hud.Label;
                Assert.IsTrue(label.enableAutoSizing, "長い知らせを縮めない（KCD/シーンを組み直す）");
                Assert.GreaterOrEqual(label.fontSizeMin, SmallestReadableSize, "知らせを縮めすぎて読めない");
                Assert.Less(label.fontSizeMin, label.fontSizeMax);

                Vector2 inner = hud.Toast.sizeDelta + label.rectTransform.sizeDelta;
                label.enableAutoSizing = false;

                label.fontSize = label.fontSizeMax;
                float one = label.GetPreferredValues(OneLine, inner.x, 0f).y;
                Assert.LessOrEqual(one, inner.y,
                    "1 行の知らせが縮む（要る高さ " + one + "、文字の入る高さ " + inner.y + "）");

                label.fontSize = label.fontSizeMin;
                float single = label.GetPreferredValues(OneLine, inner.x, 0f).y;
                float wrapped = label.GetPreferredValues(TwoLines, inner.x, 0f).y;
                Assert.Greater(wrapped, single * 1.5f, "例の文が 2 行に折り返していない（幅 " + inner.x + "）");
                Assert.LessOrEqual(wrapped, hud.Toast.sizeDelta.y,
                    "2 行の知らせが枠からはみ出す（要る高さ " + wrapped + "、枠 " + hud.Toast.sizeDelta.y + "）");
            });
        }

        [Test]
        public void TouchWelcome_FitsTheBoxAtTheSmallestSize()
        {
            WithScene(hud =>
            {
                // 横の指では右の列の手前まで狭めるので、正方形に近い横長の画面ほど枠が狭い。
                var boxes = new List<Vector2>();
                for (int i = 0; i <= 34; i++)
                {
                    boxes.Add(LayOut(hud, new Vector2(390f * (1f + 0.05f * i), 390f)).Size);
                }

                var portrait = new Vector2(390f, 844f);
                boxes.Add(PortraitSize(hud, portrait / CanvasScreen.Factor(hud.Scaler, portrait, true)));

                TMP_Text label = hud.Label;
                label.enableAutoSizing = false;
                label.fontSize = label.fontSizeMin;
                foreach (Vector2 box in boxes)
                {
                    foreach (string text in TouchWelcome)
                    {
                        float tall = label.GetPreferredValues(text, box.x + label.rectTransform.sizeDelta.x, 0f).y;
                        Assert.LessOrEqual(tall, box.y,
                            "知らせが枠からはみ出す（枠 " + box + "、要る高さ " + tall + "）: " + text);
                    }
                }
            });
        }

        private void AssertLandscape(Hud hud, Vector2 screen)
        {
            string at = " (" + screen.x + "x" + screen.y + ")";
            Placed placed = LayOut(hud, screen);
            CanvasScreen.AssertInside(screen, placed.Toast, "知らせが画面からはみ出す" + at);
            foreach (KeyValuePair<string, Rect> part in placed.Parts)
            {
                Assert.IsFalse(placed.Toast.Overlaps(part.Value), "知らせが " + part.Key + " にかかる" + at);
            }

            // y は画面の下から。「話す」の案内の上に間を空け、人物の頭より上で止まる。
            Assert.GreaterOrEqual(placed.Toast.yMin - placed.Prompt.yMax, PromptGap * placed.Factor - 0.01f,
                "知らせが「話す」の案内に詰まる" + at);
            Assert.GreaterOrEqual(placed.Toast.yMin, screen.y * (1f - HeadTop), "知らせが人物にかかる" + at);
            AssertClearOfButtons(hud, screen, placed.Toast, at);
        }

        private static void AssertClearOfButtons(Hud hud, Vector2 screen, Rect toast, string at)
        {
            foreach (RectTransform button in hud.Buttons)
            {
                Rect place = CanvasScreen.OnScreen(hud.TouchScaler, button, screen, true);
                CanvasScreen.AssertInside(screen, place, button.name + " が画面からはみ出す" + at);
                Assert.IsFalse(toast.Overlaps(place), "知らせが " + button.name + " にかかる" + at);
            }
        }

        /// <summary>
        /// 横の指で screen の画面に出したときの HUD を、シーンの置き方から組み直す。ミニマップと時計は KeepPointerSize、
        /// クエストの追跡は KeepLeftOfCenter、「話す」の案内は TouchPosition と同じ倍率と位置にする。
        /// </summary>
        private Placed LayOut(Hud hud, Vector2 screen)
        {
            float factor = CanvasScreen.Factor(hud.Scaler, screen, true);
            Vector2 canvas = screen / factor;
            RectTransform gameplay = Copy(hud.Gameplay, MakeCanvas(canvas), Vector2.zero, 1f);

            float keep = KeepPointerSize.Factor(true, false);
            RectTransform minimap = Copy(hud.Minimap, gameplay, hud.Minimap.anchoredPosition, keep);
            RectTransform caption = Copy(hud.Caption, minimap, hud.Caption.anchoredPosition, 1f);
            RectTransform clock = Copy(hud.Clock, gameplay, hud.Clock.anchoredPosition, keep);
            float share = KeepLeftOfCenter.ScaleToShare(Mathf.Abs(hud.Tracker.anchoredPosition.x) + hud.Tracker.sizeDelta.x,
                canvas.x, KeepLeftOfCenter.Share);
            RectTransform tracker = Copy(hud.Tracker, gameplay, hud.Tracker.anchoredPosition, share);
            RectTransform prompt = Copy(hud.Prompt, gameplay, hud.PromptTouch, 1f);

            // ToastLayout.Sync と FitInCanvas と同じ順で、右の列の手前までの幅にしてから Canvas に収める。
            ToastLayout.Slot slot = ToastLayout.SlotFor(ToastLayout.Shape.LandscapeTouch);
            float width = ToastLayout.LandscapeWidth(ToastLayout.ColumnLeft(gameplay, new[] { minimap, caption, clock }));
            var size = new Vector2(FitInCanvas.WidthToFit(width, canvas.x, FitInCanvas.Margin), slot.Size.y);
            RectTransform toast = Make(gameplay, "Toast", slot.Anchor, slot.Anchor, ToastLayout.Pivot, slot.Position, size,
                FitInCanvas.ScaleToFit(size, canvas, FitInCanvas.Margin));

            var parts = new Dictionary<string, Rect>
            {
                { minimap.name, OnScreen(minimap, factor) },
                { caption.name, OnScreen(caption, factor) },
                { clock.name, OnScreen(clock, factor) },
                { tracker.name, OnScreen(tracker, factor) },
            };
            return new Placed(factor, size, OnScreen(toast, factor), OnScreen(prompt, factor), parts);
        }

        /// <summary>縦長の画面での知らせの枠の大きさ。FitInCanvas と同じく、幅を Canvas に合わせて狭め、高さを差し替える。</summary>
        private static Vector2 PortraitSize(Hud hud, Vector2 canvas)
        {
            float width = ToastLayout.SlotFor(ToastLayout.Shape.Portrait).Size.x;
            return new Vector2(FitInCanvas.WidthToFit(width, canvas.x, FitInCanvas.Margin), hud.PortraitHeight);
        }

        /// <summary>Canvas の代わりにする、左下が原点の size の四角。TearDown で消す。</summary>
        private RectTransform MakeCanvas(Vector2 size)
        {
            var go = new GameObject("CanvasForTest", typeof(RectTransform));
            _created.Add(go);
            var rect = (RectTransform)go.transform;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>parent の下に、RectTransform の値と倍率を入れた四角を作る。</summary>
        private static RectTransform Make(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = new Vector3(scale, scale, 1f);
            return rect;
        }

        /// <summary>
        /// from と同じ置き方の四角を、position に置いて scale 倍する。KeepPointerSize・KeepLeftOfCenter と同じく、
        /// 角からの距離も scale 倍する。
        /// </summary>
        private static RectTransform Copy(RectTransform from, RectTransform parent, Vector2 position, float scale)
        {
            return Make(parent, from.name, from.anchorMin, from.anchorMax, from.pivot, position * scale, from.sizeDelta, scale);
        }

        /// <summary>rect の画面の px での四角（左下が原点）。MakeCanvas の四角は原点にあるので、ワールドの座標が Canvas の単位になる。</summary>
        private static Rect OnScreen(RectTransform rect, float factor)
        {
            rect.GetWorldCorners(Corners);
            return Rect.MinMaxRect(Corners[0].x * factor, Corners[0].y * factor, Corners[2].x * factor, Corners[2].y * factor);
        }

        /// <summary>parent の直下の name の枠。</summary>
        private static RectTransform Part(RectTransform parent, string name)
        {
            var part = (RectTransform)parent.Find(name);
            Assert.IsNotNull(part, parent.name + " の下に " + name + " が無い（KCD/シーンを組み直す）");
            return part;
        }

        private static void WithScene(Action<Hud> body)
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                List<RectTransform> rects = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToList();
                body(new Hud(rects));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Campus シーンの、知らせと、横の指で一緒に画面に出ているもの。</summary>
        private sealed class Hud
        {
            public readonly RectTransform Toast;
            public readonly ToastLayout Layout;
            public readonly FitInCanvas Fit;
            public readonly TMP_Text Label;
            public readonly float PortraitHeight;
            public readonly CanvasScaler Scaler;
            public readonly RectTransform Gameplay;
            public readonly RectTransform Minimap;
            public readonly RectTransform Caption;
            public readonly RectTransform Clock;
            public readonly RectTransform Tracker;
            public readonly RectTransform Prompt;
            public readonly Vector2 PromptTouch;
            public readonly Vector2 PromptPortrait;
            public readonly CanvasScaler TouchScaler;
            public readonly RectTransform[] Buttons;

            public Hud(List<RectTransform> rects)
            {
                Toast = rects.FirstOrDefault(r => r.name == "Toast" && r.Find("Backdrop") != null);
                Assert.IsNotNull(Toast, "Toast の枠が無い");
                Assert.IsTrue(Toast.TryGetComponent(out Layout), "Toast に ToastLayout が無い（KCD/シーンを組み直す）");
                Assert.IsTrue(Toast.TryGetComponent(out Fit), "Toast に FitInCanvas が無い（KCD/シーンを組み直す）");
                Transform text = Toast.Find("Label");
                Assert.IsNotNull(text, "Toast に文字が無い");
                Assert.IsTrue(text.TryGetComponent(out Label), "Toast に文字が無い");
                PortraitHeight = new SerializedObject(Fit).FindProperty("_portraitHeight").floatValue;

                Gameplay = (RectTransform)Toast.parent;
                Assert.AreEqual("Gameplay", Gameplay.name, "Toast が Gameplay の下にない");
                Assert.IsTrue(Gameplay.parent.TryGetComponent(out Scaler), "Gameplay が Canvas の直下にない");
                Minimap = Part(Gameplay, "Minimap");
                Caption = Part(Minimap, "Caption");
                Clock = Part(Gameplay, "Clock");
                Tracker = Part(Gameplay, "QuestTracker");
                Prompt = Part(Gameplay, "Prompt");
                Assert.IsTrue(Prompt.TryGetComponent(out TouchPosition promptTouch), "Prompt に TouchPosition が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(promptTouch);
                PromptTouch = serialized.FindProperty("_touchPosition").vector2Value;
                PromptPortrait = serialized.FindProperty("_portraitTouchPosition").vector2Value;

                RectTransform touch = rects.FirstOrDefault(r => r.name == "TouchCanvas");
                Assert.IsNotNull(touch, "TouchCanvas が無い");
                Assert.IsTrue(touch.TryGetComponent(out TouchScaler), "TouchCanvas に CanvasScaler が無い");
                Buttons = PlayButtons.Select(name => rects.FirstOrDefault(r => r.name == name && r.IsChildOf(touch))).ToArray();
                for (int i = 0; i < Buttons.Length; i++)
                {
                    Assert.IsNotNull(Buttons[i], PlayButtons[i] + " が無い（KCD/シーンを組み直す）");
                }
            }
        }

        /// <summary>横の指で置いた知らせと周りの枠。Size は知らせの枠の大きさ（Canvas の単位）、ほかは画面の px での四角。</summary>
        private sealed class Placed
        {
            public readonly float Factor;
            public readonly Vector2 Size;
            public readonly Rect Toast;
            public readonly Rect Prompt;
            public readonly Dictionary<string, Rect> Parts;

            public Placed(float factor, Vector2 size, Rect toast, Rect prompt, Dictionary<string, Rect> parts)
            {
                Factor = factor;
                Size = size;
                Toast = toast;
                Prompt = prompt;
                Parts = parts;
            }
        }
    }
}
