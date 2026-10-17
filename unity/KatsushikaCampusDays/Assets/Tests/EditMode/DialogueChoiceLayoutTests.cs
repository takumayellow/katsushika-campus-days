using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときの会話の選択肢 (#187)。選択肢どうしの間を TMP の段落の間で空け（折り返した 2 行目は詰める）、
    /// 最後に「やめる」を置く。叩ける高さと、いちばん長い選択肢（prof・sora）が縦の本文の欄に入ること、
    /// 横では本文の上端から枠の下端までに入ることを、シーンに置いた本文の欄と実際の会話データで確かめる。
    /// </summary>
    public sealed class DialogueChoiceLayoutTests
    {
        private const float PortraitCanvasWidth = 800f;

        /// <summary>縦持ちの Canvas（幅 800）を CSS の幅 390 px で描いたときの倍率。</summary>
        private const float PortraitScale390 = 390f / PortraitCanvasWidth;

        private const string CampusScene = "Assets/Scenes/Campus.unity";

        private static readonly string NewLine = ((char)10).ToString();

        private const string ThreeChoices =
            "{\"id\":\"npc\",\"topics\":[{\"id\":\"t\",\"lines\":[\"a\"],\"choices\":[" +
            "{\"text\":\"短い\",\"text_en\":\"Short\"}," +
            "{\"text\":\"もう一つ\",\"text_en\":\"Another\"}," +
            "{\"text\":\"みっつめ\",\"text_en\":\"Third\"}]}]}";

        private static List<DialogueChoice> Choices()
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(ThreeChoices) as Dictionary<string, object>);
            return data.Topics[0].Choices;
        }

        [TestCase("ja", "やめる")]
        [TestCase("en", "Cancel")]
        public void Compose_Touch_EndsWithTheCloseLinkAndHasNoFixedLineHeight(string locale, string label)
        {
            In(locale, () =>
            {
                string text = DialogueView.ComposeChoices(Choices(), 0, true);
                StringAssert.DoesNotContain("<line-height", text);
                StringAssert.Contains("<link=\"choice:-1\">   " + label + "</link>", text);
                StringAssert.Contains("<link=\"choice:2\">", text);
                Assert.Less(text.IndexOf("choice:2", StringComparison.Ordinal),
                    text.IndexOf("choice:-1", StringComparison.Ordinal), "「やめる」は選択肢の後");
            });
        }

        [Test]
        public void Compose_Keys_HaveNoCloseLink()
        {
            string text = DialogueView.ComposeChoices(Choices(), 0, false);
            StringAssert.DoesNotContain("choice:-1", text);
            StringAssert.DoesNotContain("<size=" + DialogueView.TouchChoiceSizePercent, text);
        }

        [TestCase("choice:0", 3, 0)]
        [TestCase("choice:2", 3, 2)]
        [TestCase("choice:3", 3, DialogueView.NoTap)]
        [TestCase("choice:-1", 3, DialogueSystem.CloseChoice)]
        [TestCase("inc:1", 3, DialogueView.NoTap)]
        [TestCase("", 3, DialogueView.NoTap)]
        [TestCase(null, 3, DialogueView.NoTap)]
        public void TappedChoice_MapsLinksToChoicesAndTheCloseLink(string link, int count, int expected)
        {
            Assert.AreEqual(expected, DialogueView.TappedChoice(link, count));
        }

        [Test]
        public void ApplyChoiceSpacing_OnlyForFingers()
        {
            WithBody((frame, body) =>
            {
                body.fontSize = 32f;
                DialogueView.ApplyChoiceSpacing(body, true, true);
                Assert.AreEqual(DialogueView.PortraitChoiceGap * 100f / 32f, body.paragraphSpacing, 1e-3f);
                DialogueView.ApplyChoiceSpacing(body, true, false);
                Assert.AreEqual(DialogueView.LandscapeChoiceGap * 100f / 32f, body.paragraphSpacing, 1e-3f);
                DialogueView.ApplyChoiceSpacing(body, false, true);
                Assert.AreEqual(0f, body.paragraphSpacing);
            });
        }

        [Test]
        public void Portrait_GapGoesBetweenChoicesNotBetweenWrappedLines()
        {
            In("en", () => WithBody((frame, body) =>
            {
                Prepare(body, true);
                float width = BodyWidth(frame, body, true);
                float lineHeight = body.GetPreferredValues(Size("A"), width, 0f).y;
                string longText = string.Join(" ", Enumerable.Repeat("wrapped", 30));
                float wrapped = body.GetPreferredValues(Size(longText), width, 0f).y;
                float lines = Mathf.Round(wrapped / lineHeight);
                float twoRows = body.GetPreferredValues(Size("A" + NewLine + "B"), width, 0f).y;
                float band = twoRows - lineHeight;

                Assert.Greater(lines, 2f, "折り返しの前提");
                Assert.AreEqual(lineHeight * lines, wrapped, lineHeight * 0.1f, "折り返した行に選択肢の間が付いている");
                Assert.AreEqual(lineHeight + DialogueView.PortraitChoiceGap, band, 1f, "選択肢の帯");
                Assert.GreaterOrEqual(band * PortraitScale390, 44f, "縦 390 で指の高さ 44 px に届かない（帯 " + band + "）");
            }));
        }

        [Test]
        public void Landscape_ChoiceBandIsNotNarrowerThanBefore()
        {
            WithBody((frame, body) =>
            {
                Prepare(body, false);
                float width = BodyWidth(frame, body, false);
                float one = body.GetPreferredValues(Size("A"), width, 0f).y;
                float two = body.GetPreferredValues(Size("A" + NewLine + "B"), width, 0f).y;
                // 以前は行の間隔が 64 単位（2em）だった。
                Assert.GreaterOrEqual(two - one, 64f, "横の叩く高さが以前より低い");
            });
        }

        [TestCase("ja", true)]
        [TestCase("en", true)]
        [TestCase("ja", false)]
        [TestCase("en", false)]
        public void EveryChoiceTopic_FitsTheBodyWithTheCloseRow(string locale, bool portrait)
        {
            In(locale, () => WithBody((frame, body) =>
            {
                Prepare(body, portrait);
                float width = BodyWidth(frame, body, portrait);
                float limit = portrait ? BodyHeight(frame, body) : LandscapeHeight(frame, body);
                int checkedTopics = 0;
                foreach (string file in Directory.GetFiles("Assets/Data/Dialogue", "*.json"))
                {
                    DialogueData data = DialogueData.FromJson(
                        MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                    foreach (DialogueTopic topic in data.Topics.Where(t => t.HasChoices))
                    {
                        checkedTopics++;
                        string text = DialogueView.ComposeChoices(topic.Choices, 0, true);
                        float needed = body.GetPreferredValues(text, width, 0f).y;
                        Assert.LessOrEqual(needed, limit,
                            data.Id + "/" + topic.Id + "（" + locale + (portrait ? "・縦" : "・横") + "）が本文の欄に入らない");
                    }
                }

                Assert.GreaterOrEqual(checkedTopics, 4, "選択肢の話題が少ない");
            }));
        }

        private static string Size(string text)
        {
            return "<size=" + DialogueView.TouchChoiceSizePercent + "%>" + text + "</size>";
        }

        private static void Prepare(TMP_Text body, bool portrait)
        {
            body.enableAutoSizing = false;
            body.fontSize = 32f;
            body.textWrappingMode = TextWrappingModes.Normal;
            DialogueView.ApplyChoiceSpacing(body, true, portrait);
        }

        /// <summary>本文の欄の幅。縦は枠を幅 800 の Canvas に合わせて縮めた幅から余白を引く。</summary>
        private static float BodyWidth(RectTransform frame, TMP_Text body, bool portrait)
        {
            float frameWidth = portrait
                ? FitInCanvas.WidthToFit(frame.sizeDelta.x, PortraitCanvasWidth, FitInCanvas.Margin)
                : frame.sizeDelta.x;
            var box = (RectTransform)body.rectTransform.parent;
            return frameWidth + box.sizeDelta.x + body.rectTransform.sizeDelta.x;
        }

        private static float BodyHeight(RectTransform frame, TMP_Text body)
        {
            var fit = new SerializedObject(frame.GetComponent<FitInCanvas>());
            float portraitHeight = fit.FindProperty("_portraitHeight").floatValue;
            var box = (RectTransform)body.rectTransform.parent;
            return (portraitHeight > 0f ? portraitHeight : frame.sizeDelta.y) + box.sizeDelta.y + body.rectTransform.sizeDelta.y;
        }

        /// <summary>横持ちで選択肢を並べられる高さ。本文の上端から枠の下端まで（枠の高さから、名札の欄と本文の上の余白を引く）。</summary>
        private static float LandscapeHeight(RectTransform frame, TMP_Text body)
        {
            var box = (RectTransform)body.rectTransform.parent;
            return frame.sizeDelta.y + box.offsetMax.y + body.rectTransform.offsetMax.y;
        }

        private static void WithBody(Action<RectTransform, TMP_Text> run)
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScene, OpenSceneMode.Additive);
            try
            {
                DialogueView view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<DialogueView>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "DialogueView が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(view);
                var root = serialized.FindProperty("_root").objectReferenceValue as GameObject;
                var label = serialized.FindProperty("_bodyLabel").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                Assert.IsNotNull(label, "本文が差し込まれていない");
                run((RectTransform)root.transform, label);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>言語を切り替えて run を走らせ、言語と保存済みの設定を元に戻す（ElevatorLayoutTests と同じ）。</summary>
        private static void In(string locale, Action run)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
                run();
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
