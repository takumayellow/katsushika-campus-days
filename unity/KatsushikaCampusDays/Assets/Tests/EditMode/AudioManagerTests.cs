using NUnit.Framework;

namespace KCD.Tests
{
    public sealed class AudioManagerTests
    {
        [Test]
        public void NextBgmIsB_PicksTheSourceThatIsNotPlaying()
        {
            // _fade: 0 = A が鳴る, 1 = B が鳴る
            Assert.IsTrue(AudioManager.NextBgmIsB(0f), "A が鳴っているなら次は B");
            Assert.IsFalse(AudioManager.NextBgmIsB(1f), "B が鳴っているなら次は A");
        }

        [Test]
        public void NextBgmIsB_AlternatesAcrossSongChanges()
        {
            // PlayBgm と同じ更新: 次の側を選び, _fade をその側へ向ける
            float fade = 1f;
            bool first = AudioManager.NextBgmIsB(fade);
            fade = first ? 1f : 0f;
            bool second = AudioManager.NextBgmIsB(fade);
            fade = second ? 1f : 0f;

            Assert.AreNotEqual(first, second, "曲を替えるたびに A と B が入れ替わる");
            Assert.AreEqual(second ? 1f : 0f, fade, "_fade は最後に鳴らした側を指す");
        }
    }
}
