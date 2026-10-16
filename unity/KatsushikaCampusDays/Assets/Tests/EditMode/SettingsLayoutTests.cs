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

            Assert.AreEqual("<link=\"choice:1\">   効果音の音量</link>\n      <link=\"dec:1\">◀</link> ■■□ <link=\"inc:1\">▶</link>\n",
                builder.ToString());
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

        [TestCase("ja")]
        [TestCase("en")]
        public void Portrait_StackedRowsFitAtFullSizeWithoutWrapping(string locale)
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
                string text = SettingsView.Compose(LongestEntries(), 0, true, true);

                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "縦で行が折り返す");
                Assert.LessOrEqual(needed, inner.y, "縦で本文が枠に入らない");
            }));
        }

        /// <summary>いちばん長くなる値で並べた 8 項目。</summary>
        private static SettingsView.Entry[] LongestEntries()
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
                new SettingsView.Entry(L.Get("ui.settings.fullscreen", "フルスクリーン"), L.Pick("オフ", "Off"), true),
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
