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
    /// クエストログの文面と枠 (#187)。縦持ちのスマホでは幅 1000 の枠を Canvas の幅 800 に縮めて本文が 11 px ほどになり、
    /// 達成したクエストまで目標を書くので、終わりのほうでは本文が枠からはみ出していた。
    /// 枠は Campus.unity のもの（幅・余白・フォント）をそのまま使って測る。
    /// </summary>
    public sealed class QuestLogLayoutTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>縦持ちの Canvas の幅（TouchCanvasScale.TouchPortraitWidth と同じ基準）。</summary>
        private const float PortraitCanvasWidth = 800f;

        /// <summary>いちばん低い縦持ちの Canvas の高さ（iPhone SE、375×667）。</summary>
        private const float ShortestPortraitCanvasHeight = 1423f;

        private const float FontSize = 28f;

        // ---- 文面 ----

        [Test]
        public void Compose_ListsActiveQuestsFirstAndCompletedOnesByTitle()
        {
            QuestSystem quests = Loaded();
            QuestData[] withSteps = quests.All.Where(q => q.Steps.Count > 0).Take(2).ToArray();
            Assert.AreEqual(2, withSteps.Length, "目標のあるクエストが 2 つ無い");
            QuestData done = withSteps[0];
            QuestData active = withSteps[1];
            Restore(quests, q => q == done ? QuestProgress.StateCompleted
                : q == active ? QuestProgress.StateActive : QuestProgress.StateNotStarted);

            string log = Log(quests);
            int activeAt = log.IndexOf(active.DisplayTitle, StringComparison.Ordinal);
            int headingAt = log.IndexOf(L.Format("ui.questlog.completed_heading", 1), StringComparison.Ordinal);
            Assert.GreaterOrEqual(activeAt, 0, "受注中のクエストが無い");
            Assert.Greater(headingAt, activeAt, "達成したクエストが受注中のものより前にある");
            Assert.Greater(log.IndexOf(done.DisplayTitle, headingAt, StringComparison.Ordinal), headingAt,
                "達成したクエストの題名が見出しの後ろに無い");
            StringAssert.Contains("・" + active.Steps[0].DisplayText, log);
            StringAssert.DoesNotContain("・" + done.Steps[0].DisplayText, log, "達成したクエストの目標まで書いている");
        }

        [Test]
        public void Compose_JoinsCompletedTitlesIntoOneParagraph()
        {
            QuestSystem quests = Loaded();
            Restore(quests, q => QuestProgress.StateCompleted);

            string log = Log(quests);
            StringAssert.Contains(L.Format("ui.questlog.completed_heading", quests.All.Count), log);
            int lines = log.Split('\n').Count(line => line.Length > 0);
            Assert.AreEqual(2, lines, "見出しと題名の段落の 2 行になっていない: " + log);
        }

        // ---- 枠 ----

        [Test]
        public void Portrait_ReflowsInsteadOfShrinking()
        {
            WithLog((frame, body) =>
            {
                var fit = new SerializedObject(frame.GetComponent<FitInCanvas>());
                Assert.IsTrue(fit.FindProperty("_reflow").boolValue, "縦持ちで枠ごと縮めている");
                float height = fit.FindProperty("_portraitHeight").floatValue;
                Assert.Greater(height, frame.sizeDelta.y, "縦持ちで行を足す高さが無い");
                Assert.LessOrEqual(height, ShortestPortraitCanvasHeight - FitInCanvas.Margin * 2f,
                    "iPhone SE の縦に収まらない");
                Assert.IsTrue(body.enableAutoSizing, "入らないときに本文を小さくしない");
                Assert.AreEqual(FontSize, body.fontSizeMax, 1e-3f);
            });
        }

        [TestCase("ja", true)]
        [TestCase("ja", false)]
        [TestCase("en", true)]
        [TestCase("en", false)]
        public void AllQuestsCompleted_FitAtFullSize(string locale, bool portrait)
        {
            In(locale, () => WithLog((frame, body) =>
            {
                QuestSystem quests = Loaded();
                Restore(quests, q => QuestProgress.StateCompleted);
                var builder = new StringBuilder();
                // 集めた数の行はいちばん長くなる形で入れる（どの数も 2 桁まで）。
                QuestLogView.ComposeProgress(builder, 99, 99, 99, 99, 99, 99);
                QuestLogView.Compose(builder, quests);
                builder.Append("\n<size=70%>").Append(L.Pick("タップで閉じる", "Tap to close")).Append("</size>");

                Vector2 size = frame.sizeDelta;
                if (portrait)
                {
                    float height = new SerializedObject(frame.GetComponent<FitInCanvas>())
                        .FindProperty("_portraitHeight").floatValue;
                    size = new Vector2(FitInCanvas.WidthToFit(size.x, PortraitCanvasWidth, FitInCanvas.Margin), height);
                }

                // 本文は枠いっぱいから余白（UIKit.Label の左右 18・上下 12）を引いた大きさ。sizeDelta は負の余白の合計。
                Vector2 inner = size + body.rectTransform.sizeDelta;
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(builder.ToString(), inner.x, 0f).y;
                Assert.LessOrEqual(needed, inner.y, (portrait ? "縦" : "横") + "で全部を達成すると本文が枠に入らない");
            }));
        }

        private static void WithLog(Action<RectTransform, TMP_Text> body)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                QuestLogView view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<QuestLogView>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "QuestLogView が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(view);
                var root = serialized.FindProperty("_root").objectReferenceValue as GameObject;
                var label = serialized.FindProperty("_bodyLabel").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                Assert.IsNotNull(label, "本文が差し込まれていない");
                body((RectTransform)root.transform, label);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static QuestSystem Loaded()
        {
            var quests = new QuestSystem();
            quests.LoadFromResources();
            Assert.Greater(quests.All.Count, 1, "クエストを読めていない");
            return quests;
        }

        private static void Restore(QuestSystem quests, Func<QuestData, int> state)
        {
            var progress = new QuestProgress();
            foreach (QuestData quest in quests.All)
            {
                int value = state(quest);
                progress.QuestIds.Add(quest.Id);
                progress.States.Add(value);
                progress.StepsDone.Add(value == QuestProgress.StateCompleted ? quest.Steps.Count : 0);
            }

            quests.Restore(progress);
        }

        private static string Log(QuestSystem quests)
        {
            var builder = new StringBuilder();
            QuestLogView.Compose(builder, quests);
            return builder.ToString();
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
