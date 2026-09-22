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

        [Test]
        public void IsChimeHour_OnlyAtNineNoonAndFive()
        {
            // 9・12・17 時の切り替わりだけ時報を鳴らす (#38)
            Assert.IsTrue(AudioManager.IsChimeHour(9));
            Assert.IsTrue(AudioManager.IsChimeHour(12));
            Assert.IsTrue(AudioManager.IsChimeHour(17));
            Assert.IsFalse(AudioManager.IsChimeHour(8));
            Assert.IsFalse(AudioManager.IsChimeHour(13));
            Assert.IsFalse(AudioManager.IsChimeHour(-1));
        }

        [Test]
        public void Chime_IsQuieterThanSeAndDucksMostOfItsLength()
        {
            // チャイムは SE 音量より小さく, 鳴っている間の大半で BGM を下げる (#38)
            Assert.Less(AudioManager.ChimeScale, 0.5f);
            Assert.Greater(AudioManager.ChimeDuckFraction, 0.5f);
            Assert.LessOrEqual(AudioManager.ChimeDuckFraction, 1f);
        }
    }
}
