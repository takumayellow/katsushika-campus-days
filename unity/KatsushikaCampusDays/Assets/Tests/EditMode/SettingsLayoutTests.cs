using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 縦持ちのスマホでの設定パネル (#187)。幅 960 の枠を Canvas の幅 800 に縮めたうえで本文も縮めていたので、
    /// 文字が 11 px、行の間隔が 18 px ほどになり、◀ ▶ を叩くと隣の行に当たっていた。
    /// 縦持ちでは枠を縮めずに幅を合わせて高さを足し、名前と値を 2 行に分けて書く。
    /// </summary>
    public sealed class SettingsLayoutTests
    {
        /// <summary>縦持ちの Canvas の幅（TouchCanvasScale.TouchPortraitWidth と同じ基準）。</summary>
        private const float PortraitCanvasWidth = 800f;

        /// <summary>いちばん低い縦持ちの Canvas の高さ（iPhone SE、375×667）。</summary>
        private const float ShortestPortraitCanvasHeight = 1423f;

        private const float FontSize = 30f;

        // ---- 文面 ----

        [Test]
        public void AppendRow_StackedPutsTheValueOnTheNextLine()
        {
            var builder = new StringBuilder();
            SettingsView.AppendRow(builder, 1, false, "効果音の音量", "■■□", true, true, true);

            Assert.AreEqual("<link=\"dec:1\">   効果音</link><link=\"inc:1\">の音量</link>\n      <link=\"dec:1\">◀</link> ■■□ <link=\"inc:1\">▶</link>\n",
                builder.ToString());
        }

        [Test]
        public void AppendRow_StackedNameWithoutArrowsStaysAChoice()
        {
            var builder = new StringBuilder();
            SettingsView.AppendRow(builder, 8, false, "閉じる", string.Empty, false, true, true);

            Assert.AreEqual("<link=\"choice:8\">   閉じる</link>\n", builder.ToString());
        }

        [Test]
        public void Hint_IsAtLeast85PercentOfTheBody()
        {
            foreach (bool stacked in new[] { false, true })
            {
                string text = SettingsView.Compose(LongestEntries(), 0, true, stacked);
                StringAssert.Contains("<size=85%>", text);
                StringAssert.DoesNotContain("<size=70%>", text);
            }

            StringAssert.Contains("<size=85%>", SettingsView.Compose(LongestEntries(), 0, false, false));
            StringAssert.Contains("<size=85%>", SettingsView.Compose(LongestEntries(), 0, true, false, true));
        }

        // ---- 横持ちの指（2 列） ----

        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        public void UseTwoColumns_OnlyForTouchInLandscape(bool touch, bool portrait, bool expected)
        {
            Assert.AreEqual(expected, SettingsView.UseTwoColumns(touch, portrait));
        }

        [Test]
        public void TwoColumns_PairsTwoLinesPerItemAtTheTapHeight()
        {
            string text = SettingsView.Compose(LongestEntries(), 0, true, false, true);

            StringAssert.StartsWith(SettingsView.TwoColumnLineHeight, text);
            // 5 段 x 2 行。末尾の改行で空の要素が 1 つ増える。
            Assert.AreEqual(11, text.Split('\n').Length, text);
            Assert.AreEqual(8, CountOf(text, SettingsView.RightColumnPos), "右の列が 3 項目 + 閉じる + 案内になっていない");
        }

        [Test]
        public void TwoColumns_EveryRowCanBeTappedAndNamesChangeTheValue()
        {
            string text = SettingsView.Compose(LongestEntries(), 3, true, false, true);

            for (int row = 0; row < 8; row++)
            {
                StringAssert.Contains("<link=\"dec:" + row + "\">", text, "行 " + row + " に ◀ が無い");
                StringAssert.Contains("<link=\"inc:" + row + "\">", text, "行 " + row + " に ▶ が無い");
                StringAssert.DoesNotContain("<link=\"choice:" + row + "\">", text, "行 " + row + " の名前が値を変えない");
            }

            StringAssert.Contains("<link=\"choice:8\">", text);
            // 名前の前半と後半、値の ◀ と ▶ で 1 項目 4 つ。8 項目と「閉じる」で 33。
            Assert.AreEqual(33, CountOf(text, "<link="), text);
        }

        [Test]
        public void TwoColumns_LeftHoldsFiveItemsAndRightTheRestWithTheHint()
        {
            string[] lines = SettingsView.Compose(LongestEntries(), 0, true, false, true).Split('\n');
            string[] sep = { SettingsView.RightColumnPos };
            for (int slot = 0; slot < 3; slot++)
            {
                StringAssert.Contains("dec:" + (5 + slot) + "\"", lines[slot * 2].Split(sep, StringSplitOptions.None)[1]);
            }

            StringAssert.Contains("choice:8", lines[6].Split(sep, StringSplitOptions.None)[1]);
            StringAssert.Contains("size=85%", lines[8].Split(sep, StringSplitOptions.None)[1]);
        }

        [TestCase("ja", true)]
        [TestCase("en", true)]
        [TestCase("ja", false)]
        [TestCase("en", false)]
        public void TwoColumns_FitTheFrameAtTheNarrowestPhone(string locale, bool fullscreenSupported)
        {
            In(locale, () => WithSettings("Assets/Scenes/Campus.unity", (frame, body) =>
            {
                // 667x375 は Canvas が 1280x720。枠は Canvas の幅 -32 まで縮む。
                var size = new Vector2(FitInCanvas.WidthToFit(SettingsView.TwoColumnFrameWidth, 1280f, FitInCanvas.Margin),
                    SettingsView.NormalFrameSize.y);
                var bodyBox = (RectTransform)body.rectTransform.parent;
                Vector2 inner = size + bodyBox.sizeDelta + body.rectTransform.sizeDelta;
                string text = SettingsView.Compose(LongestEntries(fullscreenSupported), 0, true, false, true);

                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "横で行が折り返す");
                Assert.LessOrEqual(needed, inner.y, "横で本文が枠に入らない");
                // 10 行のうち下の 9 行ぶんの間隔が 1.45em（1 項目 87 単位）。最後の行は文字の高さだけ。
                Assert.GreaterOrEqual(unwrapped, 9f * FontSize * 1.45f, "1 項目が 87 単位にならない");

                string[] sep = { SettingsView.RightColumnPos };
                foreach (string line in text.Split('\n'))
                {
                    foreach (string cell in line.Split(sep, StringSplitOptions.None))
                    {
                        Assert.LessOrEqual(body.GetPreferredValues(cell, 100000f, 0f).x, inner.x / 2f, "列に入らない: " + cell);
                    }
                }
            }));
        }

        // ---- 全画面を使えない端末 ----

        [Test]
        public void FullscreenEntry_UnsupportedExplainsInsteadOfOffering()
        {
            SettingsView.Entry entry = SettingsView.FullscreenEntry(false, false);
            Assert.IsFalse(entry.Arrows, "押しても変わらない ◀ ▶ を出している");
            StringAssert.Contains(L.Pick("ホーム画面", "Home Screen"), entry.Value);

            SettingsView.Entry usable = SettingsView.FullscreenEntry(true, true);
            Assert.IsTrue(usable.Arrows);
            StringAssert.DoesNotContain(L.Pick("ホーム画面", "Home Screen"), usable.Value);
        }

        [Test]
        public void FullscreenEntry_UnsupportedRowIsOnlyChosenNotChanged()
        {
            string text = SettingsView.Compose(LongestEntries(false), 0, true, true);
            StringAssert.DoesNotContain("dec:7", text);
            StringAssert.DoesNotContain("inc:7", text);
            StringAssert.Contains("<link=\"choice:7\">", text);
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        [Test]
        public void Compose_StackedWidensTheLinesAndFlatDoesNot()
        {
            SettingsView.Entry[] entries = LongestEntries();
            string stacked = SettingsView.Compose(entries, 0, true, true);
            string flat = SettingsView.Compose(entries, 0, true, false);

            StringAssert.StartsWith(SettingsView.StackedLineHeight, stacked);
            StringAssert.DoesNotContain("<line-height", flat);
            // 8 項目は 2 行ずつ。「閉じる」の前後の空行と、「閉じる」と案内を足して 20 行。1 行に書くと 12 行。
            Assert.AreEqual(20, stacked.Split('\n').Length, stacked);
            Assert.AreEqual(12, flat.Split('\n').Length, flat);
        }

        // ---- 枠 ----

        [TestCase("Assets/Scenes/Campus.unity")]
        [TestCase("Assets/Scenes/Title.unity")]
        public void Portrait_ReflowsInsteadOfShrinking(string scenePath)
        {
            WithSettings(scenePath, (frame, body) =>
            {
                var fit = new SerializedObject(frame.GetComponent<FitInCanvas>());
                Assert.IsTrue(fit.FindProperty("_reflow").boolValue, "縦持ちで枠ごと縮めている");
                float height = fit.FindProperty("_portraitHeight").floatValue;
                Assert.Greater(height, frame.sizeDelta.y, "縦持ちで行を足す高さが無い");
                Assert.LessOrEqual(height, ShortestPortraitCanvasHeight - FitInCanvas.Margin * 2f,
                    "iPhone SE の縦に収まらない");

                var bodyBox = (RectTransform)body.rectTransform.parent;
                Assert.AreEqual(Vector2.zero, bodyBox.anchorMin, "本文が枠に合わせて伸びない");
                Assert.AreEqual(Vector2.one, bodyBox.anchorMax, "本文が枠に合わせて伸びない");
            });
        }

        [TestCase("ja", true)]
        [TestCase("en", true)]
        [TestCase("ja", false)]
        [TestCase("en", false)]
        public void Portrait_StackedRowsFitAtFullSizeWithoutWrapping(string locale, bool fullscreenSupported)
        {
            In(locale, () => WithSettings("Assets/Scenes/Campus.unity", (frame, body) =>
            {
                float height = new SerializedObject(frame.GetComponent<FitInCanvas>())
                    .FindProperty("_portraitHeight").floatValue;
                var size = new Vector2(FitInCanvas.WidthToFit(frame.sizeDelta.x, PortraitCanvasWidth, FitInCanvas.Margin),
                    height);

                // 本文は枠から本文の箱の余白と UIKit.Label の余白を引いた大きさ。どちらも sizeDelta が負の余白の合計。
                var bodyBox = (RectTransform)body.rectTransform.parent;
                Vector2 inner = size + bodyBox.sizeDelta + body.rectTransform.sizeDelta;
                string text = SettingsView.Compose(LongestEntries(fullscreenSupported), 0, true, true);

                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "縦で行が折り返す");
                Assert.LessOrEqual(needed, inner.y, "縦で本文が枠に入らない");
            }));
        }

        /// <summary>いちばん長くなる値で並べた 8 項目。</summary>
        private static SettingsView.Entry[] LongestEntries(bool fullscreenSupported = true)
        {
            string volume = SettingsView.Bar(1f);
            return new[]
            {
                new SettingsView.Entry(L.Get("ui.settings.bgm_volume", "音楽の音量"), volume, true),
                new SettingsView.Entry(L.Get("ui.settings.se_volume", "効果音の音量"), volume, true),
                new SettingsView.Entry(L.Pick("環境音の音量", "Ambient Volume"), volume, true),
                new SettingsView.Entry(L.Get("ui.settings.language", "言語"), "English", true),
                new SettingsView.Entry(L.Get("ui.settings.quality", "画質"), L.Get("ui.settings.quality.medium", "中"), true),
                new SettingsView.Entry(L.Get("ui.settings.look_sensitivity", "カメラの感度"),
                    SettingsView.SensitivityBar(CameraPrefs.MaxLevel), true),
                new SettingsView.Entry(L.Get("ui.settings.invert_y", "カメラ上下を反転"), L.Pick("オフ", "Off"), true),
                SettingsView.FullscreenEntry(fullscreenSupported, false),
            };
        }

        private static void WithSettings(string scenePath, Action<RectTransform, TMP_Text> body)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                SettingsView view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<SettingsView>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "SettingsView が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(view);
                var root = serialized.FindProperty("_root").objectReferenceValue as GameObject;
                var label = serialized.FindProperty("_body").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                Assert.IsNotNull(label, "本文が差し込まれていない");
                body((RectTransform)root.transform, label);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>言語を切り替えて body を走らせ、言語と保存済みの設定を元に戻す（LocalizedTextTests と同じ）。</summary>
        private static void In(string locale, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
                Assert.AreEqual(locale, L.Locale);
                body();
            }
            finally
            {
                L.SetLocale(before);
                if (hadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, savedPref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }
    }
}
