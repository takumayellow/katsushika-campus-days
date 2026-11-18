using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>時間帯（DayPart）と、それで言い分ける台詞・寮のエンドの文 (#41)。</summary>
    public sealed class DayPartTests
    {
        private const string TimedLineJson =
            "{\"id\":\"npc\",\"topics\":[{\"id\":\"t\",\"lines\":[" +
            "{\"text\":\"昼\",\"text_en\":\"Day\",\"text_morning\":\"朝\",\"text_morning_en\":\"Morning\"," +
            "\"text_after_school\":\"放課後\",\"text_after_school_en\":\"After\",\"text_night\":\"夜\",\"text_night_en\":\"Night\"}," +
            "{\"text\":\"いつも\",\"text_en\":\"Always\"}]}]}";

        [TearDown]
        public void TearDown()
        {
            InputMode.Force(null);
        }

        [TestCase(8.5f, DayPartKind.Morning)]
        [TestCase(8.99f, DayPartKind.Morning)]
        [TestCase(9f, DayPartKind.Class)]
        [TestCase(12.5f, DayPartKind.Class)]
        [TestCase(16.99f, DayPartKind.Class)]
        [TestCase(17f, DayPartKind.AfterSchool)]
        [TestCase(18.49f, DayPartKind.AfterSchool)]
        [TestCase(18.5f, DayPartKind.Night)]
        [TestCase(23.9f, DayPartKind.Night)]
        public void Of_SplitsTheDayAtFirstPeriodLastPeriodAndSunset(float hours, DayPartKind expected)
        {
            Assert.AreEqual(expected, DayPart.Of(hours));
        }

        [Test]
        public void Of_TreatsNaNAsMorning()
        {
            Assert.AreEqual(DayPartKind.Morning, DayPart.Of(float.NaN));
        }

        [Test]
        public void Boundaries_MatchTheTimetableAndSunset()
        {
            // timetable.json の 1 限は 09:00 から、5 限は 17:00 まで。日の入りは DayNightCycle の既定。
            string path = Path.Combine(Application.dataPath, "Data", "Classes", "timetable.json");
            string json = File.ReadAllText(path);
            StringAssert.Contains("\"start\": \"09:00\"", json, "1 限の始まりが変わった");
            StringAssert.Contains("\"end\": \"17:00\"", json, "5 限の終わりが変わった");
            Assert.AreEqual(DayNightCycle.DefaultSunsetHour, DayPart.NightStartHour);
        }

        [TestCase("ja", DayPartKind.Morning, "朝")]
        [TestCase("ja", DayPartKind.Class, "昼")]
        [TestCase("ja", DayPartKind.AfterSchool, "放課後")]
        [TestCase("ja", DayPartKind.Night, "夜")]
        [TestCase("en", DayPartKind.Morning, "Morning")]
        [TestCase("en", DayPartKind.Class, "Day")]
        [TestCase("en", DayPartKind.AfterSchool, "After")]
        [TestCase("en", DayPartKind.Night, "Night")]
        public void DialogueLine_PicksTheTextForTheTimeOfDay(string locale, DayPartKind part, string expected)
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(TimedLineJson) as Dictionary<string, object>);
            DialogueLine timed = data.Topics[0].Lines[0];
            DialogueLine plain = data.Topics[0].Lines[1];
            InLocale(locale, () =>
            {
                InputMode.Force(InputKind.Pointer);
                Assert.AreEqual(expected, timed.DisplayTextAt(part));
                Assert.AreEqual(locale == "en" ? "Always" : "いつも", plain.DisplayTextAt(part),
                    "時間帯の文が無い行はいつも同じ");
            });
        }

        [Test]
        public void DormHeadGreeting_ChangesWithTheTimeOfDay()
        {
            string path = Path.Combine(Application.dataPath, "Data", "Dialogue", "dorm_head.json");
            DialogueData data = DialogueData.FromJson(
                MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>);
            DialogueLine greeting = data.Topics[0].Lines[0];
            InLocale("ja", () =>
            {
                InputMode.Force(InputKind.Pointer);
                var seen = new HashSet<string>();
                foreach (DayPartKind part in new[]
                         { DayPartKind.Morning, DayPartKind.Class, DayPartKind.AfterSchool, DayPartKind.Night })
                {
                    Assert.IsTrue(seen.Add(greeting.DisplayTextAt(part)), "寮長のあいさつが時間帯で変わらない: " + part);
                }

                StringAssert.Contains("始まっている", greeting.DisplayTextAt(DayPartKind.Class));
            });

            // 英語にも全部の時間帯の文がある。
            Assert.IsNotEmpty(greeting.TextMorningEn);
            Assert.IsNotEmpty(greeting.TextAfterSchoolEn);
            Assert.IsNotEmpty(greeting.TextNightEn);
        }

        [Test]
        public void DormEndingLead_DiffersByTimeOfDay()
        {
            foreach (bool english in new[] { false, true })
            {
                var seen = new HashSet<string>();
                foreach (float hours in new[] { 8.6f, 14f, 17.5f, 19f })
                {
                    string body = DormEnding.BodyText(hours, english);
                    string lead = DormEnding.LeadText(DayPart.Of(hours), english);
                    Assert.IsTrue(body.StartsWith(lead), "本文が時間帯の一文で始まらない: " + hours);
                    Assert.IsTrue(seen.Add(lead), "時間帯で文が変わらない: " + hours + " english=" + english);
                }
            }

            StringAssert.Contains("サボった", DormEnding.BodyText(14f, false), "授業中は従来どおりサボった話");
        }

        private static void InLocale(string locale, System.Action body)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale(locale);
                    body();
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }
    }
}
