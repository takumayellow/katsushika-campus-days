using System.Text;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// クエストログの先頭に出す、集めた数の行 (#10)。項目の並び、全部の数での頭打ち、全部の数が 0 の項目を出さないこと。
    /// </summary>
    public sealed class QuestLogProgressTests
    {
        private PlayerPrefsKeyScope _localePref;
        private string _savedLocale;

        [SetUp]
        public void SetUp()
        {
            _localePref = new PlayerPrefsKeyScope(L.PrefKey);
            _savedLocale = L.Locale;
            L.SetLocale("ja");
            Assert.AreEqual("ja", L.Locale, "Resources/KCD/Localization/ja.json を読めていない");
        }

        [TearDown]
        public void TearDown()
        {
            L.SetLocale(_savedLocale);
            _localePref.Dispose();
        }

        private static string Compose(int hidden, int hiddenTotal, int photos, int photoTotal, int titles, int titleTotal)
        {
            var builder = new StringBuilder();
            QuestLogView.ComposeProgress(builder, hidden, hiddenTotal, photos, photoTotal, titles, titleTotal);
            return builder.ToString();
        }

        [Test]
        public void ShowsAllThreeCountsOnOneLine()
        {
            string text = Compose(3, 20, 1, 6, 2, 14);

            StringAssert.Contains("隠しアイテム 3 / 20　写真 1 / 6　称号 2 / 14", text);
            StringAssert.StartsWith("<size=85%>", text);
            StringAssert.EndsWith("</size>\n\n", text);
        }

        [Test]
        public void ClampsCountsToTheTotal()
        {
            string text = Compose(25, 20, -1, 6, 99, 14);

            StringAssert.Contains("隠しアイテム 20 / 20", text);
            StringAssert.Contains("写真 0 / 6", text);
            StringAssert.Contains("称号 14 / 14", text);
        }

        [Test]
        public void SkipsItemsWithNoTotal()
        {
            string text = Compose(0, 0, 2, 6, 0, 0);

            StringAssert.Contains(">写真 2 / 6<", text);
            StringAssert.DoesNotContain("隠しアイテム", text);
            StringAssert.DoesNotContain("称号", text);
        }

        [Test]
        public void WritesNothingWhenEverythingIsEmpty()
        {
            Assert.AreEqual(string.Empty, Compose(0, 0, 0, 0, 0, 0));
        }

        [Test]
        public void UsesTheEnglishLabels()
        {
            L.SetLocale("en");

            StringAssert.Contains("Hidden items 3 / 20   Photos 1 / 6   Achievements 2 / 14", Compose(3, 20, 1, 6, 2, 14));
        }
    }
}
