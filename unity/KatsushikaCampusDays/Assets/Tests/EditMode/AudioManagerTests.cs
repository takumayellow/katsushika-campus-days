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
        public void IsChimeHour_OnlyAtNoonAndFive()
        {
            // 昼休み (12 時) と下校 (17 時) の切り替わりだけ時報を鳴らす。9 時は鳴らさない (#38)
            Assert.IsTrue(AudioManager.IsChimeHour(12));
            Assert.IsTrue(AudioManager.IsChimeHour(17));
            Assert.IsFalse(AudioManager.IsChimeHour(9));
            Assert.IsFalse(AudioManager.IsChimeHour(8));
            Assert.IsFalse(AudioManager.IsChimeHour(13));
            Assert.IsFalse(AudioManager.IsChimeHour(-1));
        }

        [Test]
        public void ChimeLabelKey_MatchesChimeHours()
        {
            // 鳴る時刻には必ずトーストの文言があり, 鳴らない時刻には無い (#38)
            for (int hour = 0; hour < 24; hour++)
            {
                bool chimes = AudioManager.IsChimeHour(hour);
                Assert.AreEqual(chimes, AudioManager.ChimeLabelKey(hour) != null, "hour " + hour);
            }
        }

        [Test]
        public void ShouldStopSilent_KeepsTheCurrentSongRunningWhileDuckedToZero()
        {
            // チャイムで音量 0 まで下げても現在の曲 (goal 1) は止めない。止めるのはフェードアウトし切った側だけ (#38)
            Assert.IsFalse(AudioManager.ShouldStopSilent(true, 0f, 1f), "ダック中の現在の曲は止めない");
            Assert.IsTrue(AudioManager.ShouldStopSilent(true, 0f, 0f), "フェードアウトし切った側は止める");
            Assert.IsFalse(AudioManager.ShouldStopSilent(true, 0.2f, 0f), "まだ聞こえる間は止めない");
            Assert.IsFalse(AudioManager.ShouldStopSilent(false, 0f, 0f), "止まっているものは触らない");
        }

        [Test]
        public void Chime_IsQuieterThanSeAndSilencesBgmWhileRinging()
        {
            // チャイムは SE 音量より小さく, 鳴っている間はほぼ全部で BGM を止める (#38)
            Assert.Less(AudioManager.ChimeScale, 0.5f);
            Assert.AreEqual(0f, AudioManager.ChimeDuckLevel);
            Assert.Less(AudioManager.ChimeDuckLevel, AudioManager.JingleDuckLevel);
            Assert.GreaterOrEqual(AudioManager.ChimeDuckFraction, 0.85f);
            Assert.LessOrEqual(AudioManager.ChimeDuckFraction, 1f);
        }
    }
}
