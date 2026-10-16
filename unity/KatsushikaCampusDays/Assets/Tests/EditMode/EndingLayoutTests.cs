using System;
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
    /// 縦持ちのスマホでのクレジットと 2 つのエンディング (#187)。幅 1000〜1240 の枠を Canvas の幅 800 に縮めると
    /// 文字が 6〜12 px になるので、縦長では縮めずに幅を合わせて折り返す。折り返した本文が枠に入るかを、
    /// シーンに置いた枠と文字の大きさで確かめる（クレジットは Title.unity、エンディングは Campus.unity）。
    /// </summary>
    public sealed class EndingLayoutTests
    {
        /// <summary>縦持ちの Canvas の幅（TouchCanvasScale.PortraitWidth）。</summary>
        private const float PortraitCanvasWidth = 800f;

        /// <summary>スマホでいちばん低い縦持ちの Canvas の高さ（iPhone SE、375×667）。縦横比が 4:3 に近いタブレットでは枠ごと縮む。</summary>
        private const float ShortestPortraitCanvasHeight = 1423f;

        private const string CampusScene = "Assets/Scenes/Campus.unity";
        private const string TitleScene = "Assets/Scenes/Title.unity";

        [Test]
        public void WrapsBody_KeepsTheCreditsOnOneLineOnlyInLandscape()
        {
            Assert.IsTrue(FormalEnding.WrapsBody(0, false));
            Assert.IsTrue(FormalEnding.WrapsBody(1, false));
            Assert.IsFalse(FormalEnding.WrapsBody(FormalEnding.PageCount - 1, false));
            for (int page = 0; page < FormalEnding.PageCount; page++)
            {
                Assert.IsTrue(FormalEnding.WrapsBody(page, true), "縦で " + page + " ページ目を折り返さない");
            }
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void Credits_FitInPortrait(string locale)
        {
            In(locale, () => With<CreditsView>(TitleScene, (frame, body) =>
                AssertFits(frame, body, CreditsView.BuildText(true), body.fontSize, "クレジット")));
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void FormalEndingCredits_FitInPortraitWithoutShrinkingMuch(string locale)
        {
            // 本文は 30 から縮める。縦でクレジットのページが 26 のまま入れば、クレジット画面と同じ大きさで読める。
            In(locale, () => With<FormalEnding>(CampusScene, (frame, body) =>
                AssertFits(frame, body, CreditsView.BuildBody(), 26f, "エンディングのクレジット")));
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void DormEnding_FitsInPortrait(string locale)
        {
            In(locale, () => With<DormEnding>(CampusScene, (frame, body) =>
                AssertFits(frame, body, DormEnding.BodyText(23.5f, L.IsEnglish), body.fontSize, "裏エンド")));
        }

        /// <summary>縦長の画面で、text を fontSize で折り返したときに本文の枠に入るか。枠は縮めない大きさか。</summary>
        private static void AssertFits(RectTransform frame, TMP_Text body, string text, float fontSize, string what)
        {
            var fit = new SerializedObject(frame.GetComponent<FitInCanvas>());
            Assert.IsTrue(fit.FindProperty("_reflow").boolValue, what + " の枠を縦で縮めている");
            float portraitHeight = fit.FindProperty("_portraitHeight").floatValue;
            var size = new Vector2(FitInCanvas.WidthToFit(frame.sizeDelta.x, PortraitCanvasWidth, FitInCanvas.Margin),
                portraitHeight > 0f ? portraitHeight : frame.sizeDelta.y);
            Assert.AreEqual(1f,
                FitInCanvas.ScaleToFit(size, new Vector2(PortraitCanvasWidth, ShortestPortraitCanvasHeight),
                    FitInCanvas.Margin), 1e-4f, what + " の枠が iPhone SE の縦で縮む");

            var box = (RectTransform)body.rectTransform.parent;
            Assert.AreEqual(Vector2.zero, box.anchorMin, what + " の本文が枠に合わせて伸びない");
            Assert.AreEqual(Vector2.one, box.anchorMax, what + " の本文が枠に合わせて伸びない");
            Vector2 inner = size + box.sizeDelta + body.rectTransform.sizeDelta;

            body.enableAutoSizing = false;
            body.fontSize = fontSize;
            body.textWrappingMode = TextWrappingModes.Normal;
            float needed = body.GetPreferredValues(text, inner.x, 0f).y;
            Assert.LessOrEqual(needed, inner.y, what + " が縦で入らない（要る高さ " + needed + "、枠 " + inner.y + "）");
        }

        private static void With<T>(string path, Action<RectTransform, TMP_Text> body) where T : MonoBehaviour
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                T view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault();
                Assert.IsNotNull(view, typeof(T).Name + " が無い（KCD/シーンを組み直す）");
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
