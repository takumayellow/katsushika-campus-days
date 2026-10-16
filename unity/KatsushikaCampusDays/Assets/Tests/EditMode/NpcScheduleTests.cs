using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>NPC の時間帯ごとの居場所 (#9)。時刻から時間帯を選ぶ規則、クエストが済むまでとどまる規則、歩くか一瞬で移るかの判定。</summary>
    public sealed class NpcScheduleTests
    {
        private static readonly float[] Hours = { DayRestart.DayStartHour, 13f };

        [Test]
        public void SlotAtPicksTheLastSlotThatHasStarted()
        {
            Assert.AreEqual(0, NpcSchedule.SlotAt(Hours, DayRestart.DayStartHour));
            Assert.AreEqual(0, NpcSchedule.SlotAt(Hours, 12.99f));
            Assert.AreEqual(1, NpcSchedule.SlotAt(Hours, 13f));
            Assert.AreEqual(1, NpcSchedule.SlotAt(Hours, 23.5f));
        }

        [Test]
        public void SlotAtBeforeTheFirstSlotContinuesTheLastOne()
        {
            Assert.AreEqual(1, NpcSchedule.SlotAt(Hours, 2f), "夜中は前の日の午後の続き");
        }

        [Test]
        public void SlotAtWithoutSlotsIsNone()
        {
            Assert.AreEqual(-1, NpcSchedule.SlotAt(new float[0], 10f));
            Assert.AreEqual(-1, NpcSchedule.SlotAt(null, 10f));
        }

        [Test]
        public void StaysAtTheFirstSpotUntilTheHoldQuestIsCompleted()
        {
            Assert.AreEqual(0, NpcSchedule.CurrentSlot(Hours, 15f, "q_gym", _ => false), "体育館まで 60 秒が済むまでは講義棟の前");
            Assert.AreEqual(0, NpcSchedule.CurrentSlot(Hours, 15f, "q_gym", null), "クエストがまだ読めていない");
            Assert.AreEqual(1, NpcSchedule.CurrentSlot(Hours, 15f, "q_gym", id => id == "q_gym"));
            Assert.AreEqual(0, NpcSchedule.CurrentSlot(Hours, 10f, "q_gym", id => id == "q_gym"));
        }

        [Test]
        public void WithoutAHoldQuestFollowsTheClock()
        {
            Assert.AreEqual(1, NpcSchedule.CurrentSlot(Hours, 15f, string.Empty, _ => false));
            Assert.AreEqual(1, NpcSchedule.CurrentSlot(Hours, 15f, null, null));
            Assert.AreEqual(-1, NpcSchedule.CurrentSlot(new float[0], 15f, "q_gym", _ => false));
        }

        [Test]
        public void WarpsOnTheFirstPlacementAndWhenThePlayerIsFar()
        {
            Vector3 npc = Vector3.zero;

            Assert.IsTrue(NpcSchedule.ShouldWarp(true, true, new Vector3(1f, 0f, 0f), npc), "シーンに入った直後");
            Assert.IsTrue(NpcSchedule.ShouldWarp(false, false, Vector3.zero, npc), "プレイヤーがいない");
            Assert.IsFalse(NpcSchedule.ShouldWarp(false, true, new Vector3(30f, 0f, 0f), npc), "見えるところでは歩く");
            Assert.IsTrue(NpcSchedule.ShouldWarp(false, true, new Vector3(4000f, 0f, 0f), npc), "屋内にいる");
        }

        [Test]
        public void HeightDoesNotCountTowardTheDistance()
        {
            var player = new Vector3(10f, NpcSchedule.WalkVisibleDistance * 2f, 0f);

            Assert.IsFalse(NpcSchedule.ShouldWarp(false, true, player, Vector3.zero));
        }
    }
}
