using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 開発者用の機能を出してよいかの判定。エディタ・Development Build・URL の ?dev=1 のときだけ true で、
    /// 公開版の通常の URL（クエリ無し・別の値・別の名前）では false。
    /// </summary>
    public sealed class DevModeTests
    {
        private const string PublicUrl = "https://takumayellow.github.io/katsushika-campus-days/";

        [Test]
        public void IsEnabled_PublicBuildWithPlainUrl_IsOff()
        {
            Assert.IsFalse(DevMode.IsEnabled(false, false, PublicUrl));
            Assert.IsFalse(DevMode.IsEnabled(false, false, string.Empty));
            Assert.IsFalse(DevMode.IsEnabled(false, false, null));
        }

        [Test]
        public void IsEnabled_EditorOrDevelopmentBuild_IsOn()
        {
            Assert.IsTrue(DevMode.IsEnabled(true, false, PublicUrl));
            Assert.IsTrue(DevMode.IsEnabled(false, true, PublicUrl));
        }

        [Test]
        public void IsEnabled_DevQueryOnPublicBuild_IsOn()
        {
            Assert.IsTrue(DevMode.IsEnabled(false, false, PublicUrl + "?dev=1"));
        }

        [TestCase("http://127.0.0.1:8765/?dev=1", true)]
        [TestCase("http://127.0.0.1:8765/index.html?dev=1", true)]
        [TestCase("http://127.0.0.1:8765/?lang=en&dev=1", true)]
        [TestCase("http://127.0.0.1:8765/?dev=1&lang=en", true)]
        [TestCase("http://127.0.0.1:8765/?dev=1#top", true)]
        [TestCase("http://127.0.0.1:8765/", false)]
        [TestCase("http://127.0.0.1:8765/?dev=0", false)]
        [TestCase("http://127.0.0.1:8765/?dev=10", false)]
        [TestCase("http://127.0.0.1:8765/?dev=", false)]
        [TestCase("http://127.0.0.1:8765/?dev", false)]
        [TestCase("http://127.0.0.1:8765/?xdev=1", false)]
        [TestCase("http://127.0.0.1:8765/?DEV=1", false)]
        [TestCase("http://127.0.0.1:8765/?lang=dev=1", false)]
        [TestCase("http://127.0.0.1:8765/#?dev=1", false)]
        [TestCase("http://127.0.0.1:8765/?x=1#dev=1", false)]
        [TestCase("http://127.0.0.1:8765/dev=1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void HasDevQuery_ReadsOnlyTheQueryString(string url, bool expected)
        {
            Assert.AreEqual(expected, DevMode.HasDevQuery(url), url);
        }
    }
}
