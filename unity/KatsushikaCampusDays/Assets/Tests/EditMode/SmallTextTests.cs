using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 指か縦長の画面で小さくなりすぎる文字 (#187)。時計の時間帯・ミニマップの「N」と建物の名前、
    /// 走り方の文面（指では Shift を出さない）、クエストの追跡の 2 行目、クエストの記録とクレジットの案内。
    /// </summary>
    public sealed class SmallTextTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            foreach (GameObject made in _made)
            {
                Object.DestroyImmediate(made);
            }

            _made.Clear();
        }

        private RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _made.Add(go);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private TMP_Text MakeLabel(string name, RectTransform parent, float size)
        {
            RectTransform rect = MakeRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(18f, 12f);
            rect.offsetMax = new Vector2(-18f, -12f);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = size;
            return label;
        }

        private static void Call(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(target, null);
        }

        private static void Shape(InputKind kind, bool portrait)
        {
            InputMode.Force(kind);
            ScreenShape.Force(portrait);
        }

        // ---- 文字の大きさの決め方 ----

        [TestCase(false, false, 24f)]
        [TestCase(true, false, 28f)]
        [TestCase(false, true, 34f)]
        [TestCase(true, true, 34f)]
        public void HudTextScale_Size_FollowsTheShape(bool touch, bool portrait, float expected)
        {
            Assert.AreEqual(expected, HudTextScale.Size(24f, touch, portrait), 1e-4f);
        }

        [Test]
        public void HudTextScale_Size_NeverShrinksTheBuiltSize()
        {
            Assert.AreEqual(40f, HudTextScale.Size(40f, true, true), 1e-4f);
        }

        // ---- 時計 ----

        [TestCase(InputKind.Pointer, false, 24f, 44f)]
        [TestCase(InputKind.Touch, false, 28f, 46f)]
        [TestCase(InputKind.Pointer, true, 34f, 46f)]
        public void Clock_PhaseLabel_FollowsTheShapeAndFitsItsRow(InputKind kind, bool portrait, float size, float row)
        {
            RectTransform clock = MakeRect("Clock", null);
            RectTransform phaseRow = MakeRect("PhaseRow", clock);
            phaseRow.sizeDelta = new Vector2(300f, 44f);
            TMP_Text phase = MakeLabel("Phase", phaseRow, 24f);
            var view = clock.gameObject.AddComponent<ClockView>();
            view.Bind(MakeLabel("Time", clock, 44f), phase);

            Shape(kind, portrait);
            Call(view, "Update");

            Assert.AreEqual(size, phase.fontSize, 1e-4f);
            Assert.AreEqual(row, phaseRow.sizeDelta.y, 1e-4f);
            Assert.GreaterOrEqual(row, size * 1.25f, "時間帯の行が文字の高さに足りない");
            Assert.AreEqual(0f, phase.rectTransform.offsetMin.y, 1e-4f);
        }

        [Test]
        public void Clock_PhaseLabel_FollowsTurningTheScreen()
        {
            RectTransform clock = MakeRect("Clock", null);
            RectTransform phaseRow = MakeRect("PhaseRow", clock);
            TMP_Text phase = MakeLabel("Phase", phaseRow, 24f);
            var view = clock.gameObject.AddComponent<ClockView>();
            view.Bind(MakeLabel("Time", clock, 44f), phase);

            Shape(InputKind.Touch, true);
            Call(view, "Update");
            Assert.AreEqual(34f, phase.fontSize, 1e-4f);

            Shape(InputKind.Touch, false);
            Call(view, "Update");
            Assert.AreEqual(28f, phase.fontSize, 1e-4f, "横に倒しても縦の大きさのまま");

            Shape(InputKind.Pointer, false);
            Call(view, "Update");
            Assert.AreEqual(24f, phase.fontSize, 1e-4f, "キーボードに戻しても大きいまま");
        }

        // ---- ミニマップ ----

        private Minimap MakeMinimap(out TMP_Text north, out TMP_Text caption, out RectTransform panel)
        {
            RectTransform root = MakeRect("Minimap", null);
            root.sizeDelta = new Vector2(300f, 300f);
            RectTransform map = MakeRect("Map", root);
            RectTransform markers = MakeRect("Markers", root);
            RectTransform arrow = MakeRect("PlayerArrow", root);
            RectTransform northRect = MakeRect("North", root);
            northRect.sizeDelta = new Vector2(80f, 44f);
            north = MakeLabel("Label", northRect, 22f);
            panel = MakeRect("Caption", root);
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -6f);
            panel.sizeDelta = new Vector2(300f, 44f);
            caption = MakeLabel("Label", panel, 22f);
            var minimap = root.gameObject.AddComponent<Minimap>();
            minimap.Bind(map, markers, arrow, caption, null, null, panel.gameObject);
            return minimap;
        }

        [TestCase(InputKind.Pointer, false, 22f)]
        [TestCase(InputKind.Touch, false, 28f)]
        [TestCase(InputKind.Pointer, true, 34f)]
        public void Minimap_NorthAndCaption_FollowTheShape(InputKind kind, bool portrait, float size)
        {
            Minimap minimap = MakeMinimap(out TMP_Text north, out TMP_Text caption, out RectTransform panel);
            minimap.SetIndoor("葛飾コミュニティハウス 1F");

            Shape(kind, portrait);
            Call(minimap, "LateUpdate");

            Assert.AreEqual(size, north.fontSize, 1e-4f, "N の大きさ");
            Assert.AreEqual(44f, ((RectTransform)north.rectTransform.parent).sizeDelta.y, 1e-4f, "N の枠の高さ（時計に届かない）");
            Assert.AreEqual(0f, north.rectTransform.offsetMin.y, 1e-4f);
            if (kind == InputKind.Pointer && !portrait)
            {
                Assert.AreEqual(22f, caption.fontSize, 1e-4f);
                Assert.AreEqual(300f, panel.sizeDelta.x, 1e-4f);
                Assert.AreEqual(0.5f, panel.anchorMin.x, 1e-4f);
                return;
            }

            Assert.AreEqual(size, caption.fontSizeMax, 1e-4f, "建物と階の名前の大きさ");
            Assert.IsTrue(caption.enableAutoSizing, "長い名前は 1 行に収めるため縮められる");
            Assert.AreEqual(22f, caption.fontSizeMin, 1e-4f);
            Assert.AreEqual(44f, panel.sizeDelta.y, 1e-4f, "見出しの枠の高さ（下の時計に届かない）");
            Assert.GreaterOrEqual(panel.sizeDelta.x, 300f);
            Assert.LessOrEqual(panel.sizeDelta.x, 450f);
            Assert.AreEqual(1f, panel.anchorMin.x, 1e-4f, "広げた枠は右端をそろえて左へ伸ばす");
        }

        [Test]
        public void Minimap_Caption_ReturnsToTheBuiltBoxWhenTheShapeReturns()
        {
            Minimap minimap = MakeMinimap(out TMP_Text north, out TMP_Text caption, out RectTransform panel);
            minimap.SetIndoor("講義棟 1F");

            Shape(InputKind.Touch, true);
            Call(minimap, "LateUpdate");
            Shape(InputKind.Pointer, false);
            Call(minimap, "LateUpdate");

            Assert.AreEqual(22f, caption.fontSize, 1e-4f);
            Assert.AreEqual(22f, north.fontSize, 1e-4f);
            Assert.IsFalse(caption.enableAutoSizing);
            Assert.AreEqual(new Vector2(300f, 44f), panel.sizeDelta);
            Assert.AreEqual(new Vector2(0.5f, 0f), panel.anchorMin);
            Assert.AreEqual(new Vector2(0f, -6f), panel.anchoredPosition);
        }

        // ---- 走り方の文面 ----

        private const string TouchLineJson =
            "{\"id\":\"prof\",\"topics\":[{\"id\":\"t\",\"lines\":[" +
            "{\"text\":\"Shift で走れ\",\"text_en\":\"Hold Shift\",\"text_touch\":\"ボタンで走れ\",\"text_touch_en\":\"Tap Run\"}," +
            "{\"text\":\"普通\",\"text_en\":\"Plain\"}]}]}";

        private const string TouchQuestJson =
            "{\"id\":\"q\",\"summary\":\"Shift で走れ\",\"summary_en\":\"Sprint with Shift\"," +
            "\"summary_touch\":\"ボタンで走れ\",\"summary_touch_en\":\"Tap Run\",\"steps\":[]}";

        private static void InLocale(string locale, System.Action body)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale(locale);
                    body();
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        [TestCase("ja", InputKind.Pointer, "Shift で走れ")]
        [TestCase("ja", InputKind.Touch, "ボタンで走れ")]
        [TestCase("en", InputKind.Pointer, "Hold Shift")]
        [TestCase("en", InputKind.Touch, "Tap Run")]
        public void DialogueLine_UsesTheTouchTextOnlyForFingers(string locale, InputKind kind, string expected)
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(TouchLineJson) as Dictionary<string, object>);
            DialogueLine line = data.Topics[0].Lines[0];
            DialogueLine plain = data.Topics[0].Lines[1];
            InLocale(locale, () =>
            {
                InputMode.Force(kind);
                Assert.AreEqual(expected, line.DisplayText);
                Assert.AreEqual(locale == "en" ? "Plain" : "普通", plain.DisplayText, "指用の文が無い行は指でも同じ");
            });
        }

        [TestCase("ja", InputKind.Pointer, "Shift で走れ")]
        [TestCase("ja", InputKind.Touch, "ボタンで走れ")]
        [TestCase("en", InputKind.Pointer, "Sprint with Shift")]
        [TestCase("en", InputKind.Touch, "Tap Run")]
        public void QuestSummary_UsesTheTouchTextOnlyForFingers(string locale, InputKind kind, string expected)
        {
            QuestData quest = QuestData.FromJson(MiniJson.Deserialize(TouchQuestJson) as Dictionary<string, object>);
            InLocale(locale, () =>
            {
                InputMode.Force(kind);
                Assert.AreEqual(expected, quest.DisplaySummary);
            });
        }

        [Test]
        public void ShippedGymTexts_TouchVersionsNameTheRunButtonNotShift()
        {
            var prof = MiniJson.Deserialize(Resources.Load<TextAsset>("KCD/Dialogue/prof").text) as Dictionary<string, object>;
            DialogueData data = DialogueData.FromJson(prof);
            DialogueTopic topic = data.Topics.Find(t => t.Id == "t_gym_active");
            Assert.IsNotNull(topic, "t_gym_active が無い");
            bool found = false;
            foreach (DialogueLine line in topic.Lines)
            {
                if (line.Text.Contains("Shift"))
                {
                    found = true;
                    foreach (string touch in new[] { line.TextTouch, line.TextTouchEn })
                    {
                        Assert.IsNotEmpty(touch, "Shift を教える行に指用の文が無い");
                        StringAssert.DoesNotContain("Shift", touch);
                    }

                    StringAssert.Contains("走る", line.TextTouch);
                    StringAssert.Contains("Run", line.TextTouchEn);
                }
            }

            Assert.IsTrue(found, "Shift の行が見つからない（テストの前提が変わった）");

            QuestData gym = QuestData.FromJson(
                MiniJson.Deserialize(Resources.Load<TextAsset>("KCD/Quests/q_gym").text) as Dictionary<string, object>);
            Assert.IsNotEmpty(gym.SummaryTouch);
            Assert.IsNotEmpty(gym.SummaryTouchEn);
            StringAssert.DoesNotContain("Shift", gym.SummaryTouch);
            StringAssert.DoesNotContain("Shift", gym.SummaryTouchEn);
            StringAssert.Contains("走る", gym.SummaryTouch);
            StringAssert.Contains("Run", gym.SummaryTouchEn);
        }

        // ---- クエストの追跡の 2 行目 ----

        [Test]
        public void QuestTracker_SubLine_IsShrunkOnlyInLandscape()
        {
            string landscape = QuestTrackerView.SubLine("#FFB3B3", "残り 9 秒", false);
            string portrait = QuestTrackerView.SubLine("#FFB3B3", "残り 9 秒", true);
            StringAssert.Contains("<size=80%>", landscape);
            StringAssert.DoesNotContain("<size", portrait, "縦の 2 行目は本文と同じ大きさ");
            StringAssert.Contains("残り 9 秒", portrait);
            Assert.IsTrue(portrait.StartsWith("\n"), "2 行目は改行で始まる");
        }

        // ---- クエストの記録 ----

        [Test]
        public void QuestLog_CloseHint_IsNotShrunk()
        {
            string hint = QuestLogView.CloseHint();
            StringAssert.DoesNotContain("<size", hint);
            StringAssert.Contains(L.Pick("タップで閉じる", "Tap to close"), hint);
            Assert.IsTrue(hint.StartsWith("\n"));
        }

        // ---- クレジット ----

        [Test]
        public void Credits_BackHint_IsNotShrunkForFingersOrPortrait()
        {
            string back = L.Pick("タップで戻る", "Tap to go back");
            Assert.IsFalse(CreditsView.BuildText(true).Contains("<size=75%>"), "指の戻る案内");
            Assert.IsFalse(CreditsView.BuildText(false, true).Contains("<size=75%>"), "縦の戻る案内");
            StringAssert.Contains(back, CreditsView.BuildText(true));
            StringAssert.Contains("<size=75%>", CreditsView.BuildText(), "キーボード・マウスの横長は今のまま");
        }

        [Test]
        public void Credits_Notes_AreLargerOnlyInPortrait()
        {
            string landscape = CreditsView.BuildBody();
            string portrait = CreditsView.BuildBody(true);
            StringAssert.Contains("<size=80%>", landscape);
            StringAssert.DoesNotContain("<size=90%>", landscape);
            StringAssert.Contains("<size=90%>", portrait);
            StringAssert.DoesNotContain("<size=80%>", portrait);
            Assert.AreEqual(landscape.Replace("<size=80%>", "<size=90%>"), portrait, "補足の大きさ以外は同じ文面");
        }
    }
}
