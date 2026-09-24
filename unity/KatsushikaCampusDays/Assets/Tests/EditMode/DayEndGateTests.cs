using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 一日の終わりのリザルトを出す判定 (#62)。DayEndEvaluator.Update は毎フレーム
    /// LatchDayEnd で掛け金を更新してから ShouldEndDay を見る。ここではその 2 つを同じ順で呼んで、
    /// 会話などの封鎖中に 24 時をまたいだときと、裏エンドからタイトルへ戻る途中を確かめる。
    /// </summary>
    public sealed class DayEndGateTests
    {
        private const float Start = DayRestart.DayStartHour;

        /// <summary>Ending/result.json の day_end_hour。</summary>
        private const float End = 20f;

        /// <summary>DayEndEvaluator.Update と同じ順で 1 フレームぶん判定する。</summary>
        private static bool Frame(ref bool latched, float hours, bool blocked, bool dormEnding = false)
        {
            latched = DayEndEvaluator.LatchDayEnd(latched, hours, Start, End);
            return DayEndEvaluator.ShouldEndDay(latched, blocked, dormEnding);
        }

        /// <summary>DayNightCycle と同じく 24 で巻き戻しながら、from から minutes 分だけ 1 分ずつ進める。</summary>
        private static bool RunBlocked(ref bool latched, float from, int minutes)
        {
            bool ended = false;
            for (int i = 0; i <= minutes; i++)
            {
                float hours = Mathf.Repeat(from + i / 60f, 24f);
                ended |= Frame(ref latched, hours, true);
            }

            return ended;
        }

        [Test]
        public void EndsAtTheDayEndHour_WhenNothingBlocks()
        {
            bool latched = false;

            Assert.IsFalse(Frame(ref latched, 19.99f, false), "20 時前に終わった");
            Assert.IsTrue(Frame(ref latched, 20f, false), "20 時になっても終わらない");
        }

        [Test]
        public void WaitsForTheBlockToLift()
        {
            bool latched = false;

            // 20 時を過ぎたのが会話の最中なら、会話が終わった最初のフレームで出す。
            Assert.IsFalse(Frame(ref latched, 20.2f, true), "会話の上にリザルトが出た");
            Assert.IsTrue(Frame(ref latched, 20.3f, false), "会話が終わってもリザルトが出ない");
        }

        [Test]
        public void CrossingMidnightWhileBlocked_StillEndsTheDay()
        {
            // 19:30 から会話が 5 時間続き、封鎖中に 24 時をまたいで時計が 0:30 に巻き戻った。
            bool latched = false;
            Assert.IsFalse(RunBlocked(ref latched, 19.5f, 5 * 60), "封鎖中にリザルトが出た");

            // 前の判定は「今が 20 以上 24 未満」だけで、0:30 では終わらず翌日の 20 時まで続いていた。
            const float afterMidnight = 0.5f;
            Assert.IsFalse(afterMidnight >= End && afterMidnight < 24f);

            Assert.IsTrue(Frame(ref latched, afterMidnight, false),
                "封鎖中に 24 時をまたぐと、その日の終わりが来ない");
        }

        [Test]
        public void TheMorningClearsTheLatch()
        {
            // 翌朝への巻き戻し（BeginNextDay）や、朝のセーブのロードで時計が朝になったら、前の夜を持ち越さない。
            bool latched = true;

            Assert.IsFalse(Frame(ref latched, Start, false), "朝に戻したのにリザルトが出た");
            Assert.IsFalse(latched);
            Assert.IsFalse(Frame(ref latched, 12f, false));
        }

        [Test]
        public void TheLatchHoldsThroughTheNightUntilTheMorning()
        {
            Assert.IsTrue(DayEndEvaluator.LatchDayEnd(true, 0f, Start, End));
            Assert.IsTrue(DayEndEvaluator.LatchDayEnd(true, 3f, Start, End));
            Assert.IsTrue(DayEndEvaluator.LatchDayEnd(true, Start - 0.01f, Start, End));
            Assert.IsFalse(DayEndEvaluator.LatchDayEnd(true, Start, Start, End));
            Assert.IsFalse(DayEndEvaluator.LatchDayEnd(true, End - 0.01f, Start, End));
            Assert.IsTrue(DayEndEvaluator.LatchDayEnd(false, End, Start, End));
            Assert.IsTrue(DayEndEvaluator.LatchDayEnd(false, 23.99f, Start, End));
        }

        [Test]
        public void StartingBeforeDawn_DoesNotEndTheDay()
        {
            // スモーク（-kcd-time 5）のように夜明け前から始めた日は、20 時を過ぎていないので終わらせない。
            bool latched = false;
            for (float hours = 5f; hours < Start; hours += 0.25f)
            {
                Assert.IsFalse(Frame(ref latched, hours, false), hours + " 時にリザルトが出た");
            }
        }

        [Test]
        public void TheDormEnding_HoldsTheResultBack_EvenAfterTheBlocksAreCleared()
        {
            // 裏エンドを閉じると ReturnToTitle が封鎖をまとめて外す。シーンが切り替わるまでの
            // フレームは封鎖が無いので、20 時を過ぎていると本編のリザルトが割り込んでいた。
            bool latched = false;

            Assert.IsFalse(Frame(ref latched, 21f, false, true), "裏エンドの暗転の裏でリザルトが出た");
            Assert.IsTrue(latched, "裏エンドの間も 20 時を過ぎたことは覚えておく");
            Assert.IsFalse(DayEndEvaluator.ShouldEndDay(true, true, false));
            Assert.IsTrue(DayEndEvaluator.ShouldEndDay(true, false, false));
            Assert.IsFalse(DayEndEvaluator.ShouldEndDay(false, false, false));
        }

        [Test]
        public void WalkedHours_CountsTheNightAfterMidnight()
        {
            Assert.AreEqual(0f, DayEndEvaluator.WalkedHours(Start, Start), 0.001f);
            Assert.AreEqual(11.5f, DayEndEvaluator.WalkedHours(End, Start), 0.001f);

            // 0:30 に終わった日は 8:30 から 16 時間。引き算だけでは 0 時間になっていた。
            Assert.AreEqual(16f, DayEndEvaluator.WalkedHours(0.5f, Start), 0.001f);
            Assert.Greater(DayEndEvaluator.WalkedHours(0.5f, Start), DayEndEvaluator.WalkedHours(23.5f, Start));
        }
    }
}
