using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 時計の横に出す時間帯の呼び名（ClockView.PhaseName）。区切りの時刻ちょうどは後ろの帯に入る。
    /// 文言は辞書で変わるので、どのキーを選んだかを L.Get(キー, 既定文) と比べて確かめる。
    /// </summary>
    public sealed class ClockViewTests
    {
        [TestCase(0f, "ui.hud.time_midnight", "深夜")]
        [TestCase(4.99f, "ui.hud.time_midnight", "深夜")]
        [TestCase(5f, "ui.hud.time_morning", "朝")]
        [TestCase(8.99f, "ui.hud.time_morning", "朝")]
        [TestCase(9f, "ui.hud.time_forenoon", "午前")]
        [TestCase(11.99f, "ui.hud.time_forenoon", "午前")]
        [TestCase(12f, "ui.hud.time_afternoon", "昼下がり")]
        [TestCase(14.99f, "ui.hud.time_afternoon", "昼下がり")]
        [TestCase(15f, "ui.hud.time_late_afternoon", "夕方まえ")]
        [TestCase(16.99f, "ui.hud.time_late_afternoon", "夕方まえ")]
        [TestCase(17f, "ui.hud.time_dusk", "夕暮れ")]
        [TestCase(18.99f, "ui.hud.time_dusk", "夕暮れ")]
        [TestCase(19f, "ui.hud.time_night", "夜")]
        [TestCase(23.99f, "ui.hud.time_night", "夜")]
        public void PhaseName_PicksTheBandForTheHour(float hours, string key, string fallback)
        {
            Assert.AreEqual(L.Get(key, fallback), ClockView.PhaseName(hours), hours + " 時の呼び名が違う");
        }

        [Test]
        public void PhaseName_DayStartsInTheMorning()
        {
            Assert.AreEqual(L.Get("ui.hud.time_morning", "朝"), ClockView.PhaseName(DayRestart.DayStartHour),
                "一日の始まり（" + DayRestart.DayStartHour + " 時）が朝になっていない");
        }

        [Test]
        public void PhaseName_EveryBandHasItsOwnName()
        {
            float[] samples = { 2f, 7f, 10f, 13f, 16f, 18f, 21f };
            var names = new HashSet<string>();
            foreach (float hours in samples)
            {
                names.Add(ClockView.PhaseName(hours));
            }

            Assert.AreEqual(samples.Length, names.Count, "同じ呼び名の時間帯がある（辞書のキーの取り違え）");
        }
    }
}
