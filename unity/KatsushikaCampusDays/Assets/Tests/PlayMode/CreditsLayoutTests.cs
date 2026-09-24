using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルのクレジットの本文が、日本語でも英語でも枠からはみ出さない (#74)。
    /// 文面は CreditsTextTests が見るので、ここでは実際のシーンに置いたラベルの大きさだけを見る。
    /// </summary>
    public sealed class CreditsLayoutTests : SceneTestBase
    {
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Credits_BodyFitsItsBox_InJapaneseAndEnglish()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            CreditsView credits = Object.FindAnyObjectByType<CreditsView>();
            Assert.IsNotNull(credits, "タイトルに CreditsView が無い");

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

                    TMP_Text body = FindBody(credits);
                    Assert.IsNotNull(body, locale + ": クレジットの本文のラベルが見つからない");
                    AssertFits(body, locale);

                    credits.Close();
                    Assert.IsFalse(credits.IsOpen, "Close() でクレジットが閉じない");
                }
            }
            finally
            {
                L.SetLocale(before);
            }

            AssertNoErrors("クレジット");
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
