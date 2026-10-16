using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときの操作 (#187) の決め方。入力の種類の切り替え、なぞり・つまみの量、走るの切り替え、
    /// スティックの倒し具合、操作 UI の出し入れ、文字のリンクの叩き分け、タイトル・キャラ選択・リザルトの叩いた先。
    /// </summary>
    public sealed class TouchControlsTests
    {
        private const float Tolerance = 0.0001f;

        [TearDown]
        public void TearDown()
        {
            InputMode.Force(null);
            TouchInput.Clear();
        }

        [Test]
        public void InputMode_Next_TouchingSwitchesToTouch()
        {
            Assert.AreEqual(InputKind.Touch, InputMode.Next(InputKind.Pointer, true, false, false, 0f));
            Assert.AreEqual(InputKind.Touch, InputMode.Next(InputKind.Pointer, true, true, true, 0f),
                "キーやマウスと同じフレームでも、触れていれば指");
        }

        [Test]
        public void InputMode_Next_KeyReturnsToPointer()
        {
            Assert.AreEqual(InputKind.Pointer, InputMode.Next(InputKind.Touch, false, true, false, 0f));
        }

        [Test]
        public void InputMode_Next_MouseRightAfterTouchIsIgnored()
        {
            // ブラウザが指のあとに出す互換のマウスで戻らない。
            Assert.AreEqual(InputKind.Touch,
                InputMode.Next(InputKind.Touch, false, false, true, InputMode.MouseAfterTouchGrace * 0.5f));
            Assert.AreEqual(InputKind.Pointer,
                InputMode.Next(InputKind.Touch, false, false, true, InputMode.MouseAfterTouchGrace + 0.1f));
        }

        [Test]
        public void InputMode_Next_NothingKeepsTheCurrentKind()
        {
            Assert.AreEqual(InputKind.Touch, InputMode.Next(InputKind.Touch, false, false, false, 10f));
            Assert.AreEqual(InputKind.Pointer, InputMode.Next(InputKind.Pointer, false, false, true, 10f));
        }

        [TestCase(true, false, true, InputKind.Touch)]
        [TestCase(false, true, false, InputKind.Touch)]
        [TestCase(false, true, true, InputKind.Pointer)]
        [TestCase(false, false, true, InputKind.Pointer)]
        [TestCase(false, false, false, InputKind.Pointer)]
        public void InputMode_Initial_PicksTouchOnPhonesAndMouselessTouchscreens(
            bool mobile, bool touchscreen, bool mouse, InputKind expected)
        {
            Assert.AreEqual(expected, InputMode.Initial(mobile, touchscreen, mouse));
        }

        [Test]
        public void InputMode_Force_RaisesChangedOnlyWhenTheKindChanges()
        {
            InputMode.Force(InputKind.Pointer);
            var seen = new List<InputKind>();
            void OnChanged(InputKind kind) => seen.Add(kind);
            InputMode.Changed += OnChanged;
            try
            {
                InputMode.Force(InputKind.Touch);
                InputMode.Force(InputKind.Touch);
                Assert.IsTrue(InputMode.IsTouch);
                InputMode.Force(InputKind.Pointer);
            }
            finally
            {
                InputMode.Changed -= OnChanged;
            }

            CollectionAssert.AreEqual(new[] { InputKind.Touch, InputKind.Pointer }, seen);
        }

        [Test]
        public void TouchInput_DragToLook_ScreenHeightGivesTheFullTurn()
        {
            Vector2 look = TouchInput.DragToLook(new Vector2(0f, 720f), 720f);
            Assert.AreEqual(TouchInput.LookPerScreenHeight, look.y, Tolerance);
            Assert.AreEqual(0f, look.x, Tolerance);
            Assert.AreEqual(Vector2.zero, TouchInput.DragToLook(new Vector2(10f, 10f), 0f));
        }

        [Test]
        public void TouchInput_DragToLook_IsTheSameOnAnyResolution()
        {
            // 同じ割合をなぞれば、画面の画素数によらず同じだけ回る。
            Vector2 small = TouchInput.DragToLook(new Vector2(39f, 0f), 390f);
            Vector2 large = TouchInput.DragToLook(new Vector2(108f, 0f), 1080f);
            Assert.AreEqual(small.x, large.x, Tolerance);
        }

        [Test]
        public void TouchInput_PinchToZoom_SpreadIsPositiveAndSymmetric()
        {
            float spread = TouchInput.PinchToZoom(100f, 200f);
            float pinch = TouchInput.PinchToZoom(200f, 100f);
            Assert.Greater(spread, 0f);
            Assert.AreEqual(-spread, pinch, Tolerance);
            Assert.AreEqual(0f, TouchInput.PinchToZoom(0f, 100f));
            Assert.AreEqual(0f, TouchInput.PinchToZoom(100f, -1f));
        }

        // 最後に読んだフレームは Clear() では戻らない。テストごとにフレーム番号を大きく離して、前のテストの読みと続かないようにする。
        [Test]
        public void TouchInput_LookAt_DragLandsInTheNextReadFrameOnce()
        {
            TouchInput.Clear();
            Assert.AreEqual(Vector2.zero, TouchInput.LookAt(100000));
            TouchInput.AddDrag(new Vector2(0f, 72f), 720f);
            TouchInput.AddPinch(100f, 200f);

            Vector2 look = TouchInput.LookAt(100001);
            Assert.AreEqual(TouchInput.LookPerScreenHeight * 0.1f, look.y, Tolerance);
            Assert.AreEqual(TouchInput.PinchToZoom(100f, 200f), TouchInput.ZoomAt(100001), Tolerance);
            Assert.AreEqual(look, TouchInput.LookAt(100001), "同じフレームに 2 回読むと量が変わる");
            Assert.AreEqual(Vector2.zero, TouchInput.LookAt(100002), "1 回のなぞりが 2 フレームに数えられた");
        }

        [Test]
        public void TouchInput_LookAt_DragAfterTheReadMovesToTheNextFrame()
        {
            TouchInput.Clear();
            TouchInput.LookAt(200000);
            TouchInput.AddDrag(new Vector2(72f, 0f), 720f);
            Assert.AreEqual(Vector2.zero, TouchInput.LookAt(200000), "読んだ後に来たなぞりが同じフレームに混ざった");
            Assert.AreEqual(TouchInput.LookPerScreenHeight * 0.1f, TouchInput.LookAt(200001).x, Tolerance,
                "読んだ後に来たなぞりが次のフレームに回らずに消えた");
        }

        [Test]
        public void TouchInput_LookAt_DropsWhatPiledUpWhileNobodyRead()
        {
            // 会話中などで視点を読まない間のなぞりを、読み始めた瞬間にまとめて回さない。
            TouchInput.Clear();
            TouchInput.LookAt(300000);
            TouchInput.AddDrag(new Vector2(0f, 720f), 720f);
            TouchInput.AddPinch(100f, 400f);
            Assert.AreEqual(Vector2.zero, TouchInput.LookAt(300005));
            Assert.AreEqual(0f, TouchInput.ZoomAt(300005));

            TouchInput.AddDrag(new Vector2(0f, 72f), 720f);
            Assert.AreEqual(TouchInput.LookPerScreenHeight * 0.1f, TouchInput.LookAt(300006).y, Tolerance,
                "読み始めた次のフレームのなぞりが届かない");
        }

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        [TestCase(true, false, true, true)]
        [TestCase(true, true, true, true)]
        [TestCase(true, true, false, false)]
        public void TouchInput_NextSprint_StopsWhenTheStickIsReleased(
            bool on, bool wasMoving, bool isMoving, bool expected)
        {
            Assert.AreEqual(expected, TouchInput.NextSprint(on, wasMoving, isMoving));
        }

        [Test]
        public void TouchStick_StickValue_ScalesAndClamps()
        {
            Vector2 half = TouchStick.StickValue(new Vector2(45f, 0f), 90f);
            Assert.AreEqual(0.5f, half.x, Tolerance);

            Vector2 far = TouchStick.StickValue(new Vector2(300f, 400f), 90f);
            Assert.AreEqual(1f, far.magnitude, Tolerance);
            Assert.AreEqual(0.6f, far.x, Tolerance);

            Assert.AreEqual(Vector2.zero, TouchStick.StickValue(new Vector2(10f, 0f), 0f));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void TouchHud_ShowsPlayControls_OnlyWhileWalkingWithFingers(bool touch, bool blocked, bool expected)
        {
            Assert.AreEqual(expected, TouchHud.ShowsPlayControls(touch, blocked));
        }

        [TestCase(false, false, false, false, TouchHud.Layout.Hidden)]
        [TestCase(false, false, true, false, TouchHud.Layout.Hidden)]
        [TestCase(true, false, false, false, TouchHud.Layout.Play)]
        [TestCase(true, true, false, false, TouchHud.Layout.Hidden)]
        [TestCase(true, true, true, false, TouchHud.Layout.Photo)]
        [TestCase(true, true, true, true, TouchHud.Layout.Hidden)]
        [TestCase(true, false, false, true, TouchHud.Layout.Hidden)]
        public void TouchHud_LayoutFor_KeepsTheCameraButtonsInPhotoMode(bool touch, bool blocked, bool photoMode,
            bool capturing, TouchHud.Layout expected)
        {
            // 写真モードは操作を止める（blocked）が、動かす・向ける・撮る・戻るのボタンは出す。撮る瞬間は写らないよう隠す。
            Assert.AreEqual(expected, TouchHud.LayoutFor(touch, blocked, photoMode, capturing));
        }

        [Test]
        public void KCDInput_PhotoZoomPerPinch_ZoomsAboutAsMuchAsTheFingersSpread()
        {
            // 2 本指の間隔を e 倍に広げたときに狭まる画角（度）。小さいと端から端まで何度もつまみ直す。
            float degrees = TouchInput.ZoomPerPinchLog * KCDInput.PhotoZoomPerPinch * PhotoFreeCamera.ZoomStepDegrees;
            Assert.That(degrees, Is.InRange(25f, 45f));

            // 広角の端から望遠の端まで、指の間隔の倍率。片手の 2 本指で届く範囲に収める。
            float spread = Mathf.Exp((PhotoFreeCamera.MaxFieldOfView - PhotoFreeCamera.MinFieldOfView) / degrees);
            Assert.That(spread, Is.InRange(3f, 8f));
        }

        [Test]
        public void TmpLinkTap_Pick_InsideWinsOverTheNeighboursMargin()
        {
            // 「◀　名前　▶」の並び。名前の端を叩いても、広げた矢印の範囲に入っているだけでは矢印にならない。
            var rects = new List<Rect>
            {
                new Rect(100f, 100f, 20f, 30f),
                new Rect(140f, 100f, 200f, 30f),
                new Rect(360f, 100f, 20f, 30f),
            };
            Assert.AreEqual(1, TmpLinkTap.Pick(rects, new Vector2(145f, 115f), 40f));
            Assert.AreEqual(1, TmpLinkTap.Pick(rects, new Vector2(335f, 115f), 40f));
        }

        [Test]
        public void TmpLinkTap_Pick_NearbyTapHitsTheClosest()
        {
            var rects = new List<Rect>
            {
                new Rect(100f, 100f, 20f, 30f),
                new Rect(140f, 100f, 200f, 30f),
            };
            Assert.AreEqual(0, TmpLinkTap.Pick(rects, new Vector2(90f, 80f), 40f), "矢印の左下の少し外");
            Assert.AreEqual(0, TmpLinkTap.Pick(rects, new Vector2(125f, 115f), 40f), "矢印寄りの隙間");
            Assert.AreEqual(-1, TmpLinkTap.Pick(rects, new Vector2(40f, 115f), 40f), "どれからも遠い");
            Assert.AreEqual(-1, TmpLinkTap.Pick(new List<Rect>(), Vector2.zero, 40f));
        }

        [TestCase("choice:0", true, 0)]
        [TestCase("choice:1", true, 1)]
        [TestCase("choice:", false, -1)]
        [TestCase("choice:-1", false, -1)]
        [TestCase("choice:x", false, -1)]
        [TestCase("pick", false, -1)]
        [TestCase(null, false, -1)]
        public void TmpLinkTap_TryParseChoice(string id, bool ok, int index)
        {
            Assert.AreEqual(ok, TmpLinkTap.TryParseChoice(id, out int parsed));
            if (ok)
            {
                Assert.AreEqual(index, parsed);
            }
        }

        [Test]
        public void TitleMenu_TapChoice_WithoutSaveAnyTapStarts()
        {
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.TapChoice(false, null));
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.TapChoice(false, "choice:0"));
        }

        [Test]
        public void TitleMenu_TapChoice_WithSaveOnlyTheChoicesCount()
        {
            Assert.AreEqual(TitleChoice.Continue, TitleMenu.TapChoice(true, "choice:0"));
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.TapChoice(true, "choice:1"));
            Assert.IsNull(TitleMenu.TapChoice(true, null), "選択肢の外を叩いても、つづきからの進行を消さない");
            Assert.IsNull(TitleMenu.TapChoice(true, "choice:2"));
        }

        [Test]
        public void TitleMenu_ChoiceLine_LinksWrapEachChoice()
        {
            string plain = TitleMenu.ChoiceLine(0, "つづきから", "はじめから");
            string linked = TitleMenu.ChoiceLine(0, "つづきから", "はじめから", true);
            StringAssert.DoesNotContain("<link", plain);
            StringAssert.Contains("<link=\"choice:0\"><color=#FFD98A>▶ つづきから</color></link>", linked);
            StringAssert.Contains("<link=\"choice:1\">　はじめから</link>", linked);
        }

        [Test]
        public void ResultScreen_ChoiceLine_LinksWrapEachChoice()
        {
            string[] labels = { "もう一日歩く", "タイトルへ" };
            string plain = ResultScreen.ChoiceLine(0, labels, false);
            string linked = ResultScreen.ChoiceLine(1, labels, true);
            StringAssert.DoesNotContain("<link", plain);
            StringAssert.Contains("<color=#FFD98A>▶ もう一日歩く</color>", plain);
            StringAssert.Contains("<link=\"choice:0\">   もう一日歩く</link>", linked);
            StringAssert.Contains("<link=\"choice:1\"><color=#FFD98A>▶ タイトルへ</color></link>", linked);
        }

        [TestCase("choice:0", 0)]
        [TestCase("choice:1", 1)]
        [TestCase("choice:2", -1)]
        [TestCase("pick", -1)]
        [TestCase(null, -1)]
        public void ResultScreen_TapIndex_OnlyTheChoicesCount(string id, int expected)
        {
            Assert.AreEqual(expected, ResultScreen.TapIndex(id));
        }

        [TestCase(CharacterSelect.PrevLink, -1)]
        [TestCase(CharacterSelect.NextLink, 1)]
        [TestCase(CharacterSelect.PickLink, 0)]
        [TestCase(null, 0)]
        public void CharacterSelect_TapStep(string id, int expected)
        {
            Assert.AreEqual(expected, CharacterSelect.TapStep(id));
        }
    }
}
