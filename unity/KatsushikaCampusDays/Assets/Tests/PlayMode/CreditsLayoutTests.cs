using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルのクレジットの本文が、日本語でも英語でも枠からはみ出さず、タグが文字のまま出ない (#74)。
    /// 開いている間はタイトルの題名と案内が隠れる。文面は CreditsTextTests が見る。
    /// </summary>
    public sealed class CreditsLayoutTests : SceneTestBase
    {
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Credits_BodyFitsItsBoxAndHidesTheTitle_InJapaneseAndEnglish()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            CreditsView credits = Object.FindAnyObjectByType<CreditsView>();
            Assert.IsNotNull(credits, "タイトルに CreditsView が無い");
            GameObject titleRoot = GameObject.Find("TitleRoot");
            Assert.IsNotNull(titleRoot, "タイトルに TitleRoot（題名と案内）が無い");

            string before = L.Locale;
            try
            {
                foreach (string locale in new[] { "ja", "en" })
                {
                    L.SetLocale(locale);
                    Assert.AreEqual(locale, L.Locale, locale + " の辞書を読めていない");

                    credits.Open();
                    yield return null;
                    Assert.IsTrue(credits.IsOpen, "Open() でクレジットが開かない");
                    Assert.IsFalse(titleRoot.activeSelf, "クレジットを開いても題名と案内がパネルに重なって出ている");

                    TMP_Text body = FindBody(credits);
                    Assert.IsNotNull(body, locale + ": クレジットの本文のラベルが見つからない");
                    AssertFits(body, locale);
                    AssertNoLiteralTags(body, locale);

                    credits.Close();
                    yield return null;
                    Assert.IsFalse(credits.IsOpen, "Close() でクレジットが閉じない");
                    Assert.IsTrue(titleRoot.activeSelf, "クレジットを閉じても題名と案内が戻らない");
                }
            }
            finally
            {
                L.SetLocale(before);
            }

            AssertNoErrors("クレジット");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Settings_ShowsNoLiteralTagsAndHidesTheTitle()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            SettingsView settings = Object.FindAnyObjectByType<SettingsView>();
            Assert.IsNotNull(settings, "タイトルに SettingsView が無い");
            GameObject titleRoot = GameObject.Find("TitleRoot");
            Assert.IsNotNull(titleRoot, "タイトルに TitleRoot（題名と案内）が無い");

            settings.Open(null);
            yield return null;
            Assert.IsFalse(titleRoot.activeSelf, "設定を開いても題名と案内がパネルに重なって出ている");
            GameObject panel = GameObject.Find("Settings");
            Assert.IsNotNull(panel, "設定のパネル（Settings）が開いていない");
            TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(false);
            Assert.IsNotEmpty(labels, "設定のパネルに文字のラベルが無い");
            foreach (TMP_Text label in labels)
            {
                label.ForceMeshUpdate();
                AssertNoLiteralTags(label, "設定の " + label.transform.parent.name);
            }

            settings.Close();
            yield return null;
            Assert.IsTrue(titleRoot.activeSelf, "設定を閉じても題名と案内が戻らない");
            AssertNoErrors("設定");
        }

        /// <summary>開いたクレジットの中で、BuildText() の文面を出しているラベル。</summary>
        private static TMP_Text FindBody(CreditsView credits)
        {
            string text = CreditsView.BuildText();
            foreach (TMP_Text label in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (label.isActiveAndEnabled && label.text == text)
                {
                    return label;
                }
            }

            return null;
        }

        /// <summary>TextMeshPro が知らないタグは画面に文字のまま出る。組んだあとの文字に山かっこが残っていないか。</summary>
        private static void AssertNoLiteralTags(TMP_Text body, string locale)
        {
            string parsed = body.GetParsedText();
            int at = parsed.IndexOfAny(new[] { '<', '>' });
            if (at >= 0)
            {
                int from = Mathf.Max(0, at - 20);
                Assert.Fail(locale + ": タグが文字のまま出ている: \"" + parsed.Substring(from, Mathf.Min(40, parsed.Length - from)) + "\"");
            }
        }

        /// <summary>余白を除いた枠の幅で組んだときの高さが、枠の高さに収まるか。</summary>
        private static void AssertFits(TMP_Text body, string locale)
        {
            body.ForceMeshUpdate();
            Rect rect = body.rectTransform.rect;
            Vector4 margin = body.margin;
            float width = rect.width - margin.x - margin.z;
            float height = rect.height - margin.y - margin.w;
            Vector2 preferred = body.GetPreferredValues(body.text, width, 0f);

            Debug.Log(string.Format("[KCD] クレジット（{0}）: 枠 {1:F0}x{2:F0}、本文 {3:F0}x{4:F0}、{5} 行",
                locale, width, height, preferred.x, preferred.y, body.textInfo.lineCount));
            Assert.LessOrEqual(preferred.y, height,
                locale + ": 本文の高さ " + preferred.y.ToString("F0") + " px が枠の " + height.ToString("F0") + " px を超える");
            Assert.LessOrEqual(preferred.x, width + 0.5f,
                locale + ": 本文の幅 " + preferred.x.ToString("F0") + " px が枠の " + width.ToString("F0") + " px を超える");
        }
    }
}
