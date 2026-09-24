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

        [Test]
        public void ShouldChime_RingsOnlyRightAfterCrossingNoonOrFive()
        {
            // 毎フレーム少しずつ進んで 12 時・17 時をまたいだフレームだけ鳴る (#38)
            Assert.IsTrue(AudioManager.ShouldChime(11.99f, 12.01f), "12 時をまたいだ直後");
            Assert.IsTrue(AudioManager.ShouldChime(16.999f, 17f), "ちょうど 17 時になったフレーム");
            Assert.IsFalse(AudioManager.ShouldChime(12.01f, 12.02f), "またいだ次のフレームでは鳴らさない (1 回だけ)");
            Assert.IsFalse(AudioManager.ShouldChime(8.99f, 9.01f), "9 時は鳴らさない");
            Assert.IsFalse(AudioManager.ShouldChime(12.99f, 13.01f), "13 時は鳴らさない");
        }

        [Test]
        public void ShouldChime_OnlyRecordsFirstFrameBackwardAndStaleJumps()
        {
            // 開始直後・時刻の巻き戻し・ロードなどで飛んだときは鳴らさず, 時刻を覚えるだけ (#38)
            Assert.IsFalse(AudioManager.ShouldChime(-1f, 12.01f), "シーンに入った最初のフレーム");
            Assert.IsFalse(AudioManager.ShouldChime(12.3f, 11.95f), "時刻が戻った (ロードの次のフレーム)");
            Assert.IsFalse(AudioManager.ShouldChime(10.5f, 12.03f), "ロードで 12 時過ぎへ飛んだ");
            Assert.IsFalse(AudioManager.ShouldChime(20f, 8.5f), "翌日の 8:30 に戻った");
            Assert.IsFalse(AudioManager.ShouldChime(11.9f, 12f + AudioManager.ChimeWindowHours), "またいでから時間が経っている");
        }

        [Test]
        public void ChimeWindow_CoversTheLongestFrame()
        {
            // 1 日 = 実時間 720 秒 (DayNightCycle)。Unity の deltaTime の上限 0.333 秒のフレームでも窓に収まる
            float perFrame = 0.3333f * 24f / 720f;
            Assert.Less(perFrame, AudioManager.ChimeWindowHours);
            Assert.IsTrue(AudioManager.ShouldChime(12f - perFrame * 0.5f, 12f + perFrame * 0.5f));
        }

        [Test]
        public void StepDuck_SilencesBgmBeforeTheFirstBell()
        {
            // チャイムは ChimeLeadSeconds 遅れて鳴る。60 fps ならその前に BGM の倍率が 0 まで下がり切る (#38)
            Assert.Greater(AudioManager.ChimeLeadSeconds, AudioManager.DuckAttackSeconds);
            const float dt = 1f / 60f;
            float duck = 1f;
            for (float t = dt; t <= AudioManager.ChimeLeadSeconds; t += dt)
            {
                duck = AudioManager.StepDuck(duck, AudioManager.ChimeDuckLevel, dt);
            }

            Assert.AreEqual(0f, duck, "最初の鐘の前に無音");
        }

        [Test]
        public void StepDuck_BringsBgmBackGradually()
        {
            // 戻りはクロスフェードと同じくらいゆっくり。1 フレームで跳ね上がらない (#38)
            Assert.Greater(AudioManager.DuckReleaseSeconds, AudioManager.DuckAttackSeconds);
            const float dt = 1f / 60f;
            Assert.Less(AudioManager.StepDuck(0f, 1f, dt), 0.05f, "1 フレームでは少ししか戻らない");

            float duck = 0f;
            float seconds = 0f;
            while (duck < 1f && seconds < 10f)
            {
                duck = AudioManager.StepDuck(duck, 1f, dt);
                seconds += dt;
            }

            Assert.AreEqual(1f, duck, "最後は元の音量に戻る");
            Assert.GreaterOrEqual(seconds, 1f, "1 秒以上かけて戻る");
        }
    }
}
