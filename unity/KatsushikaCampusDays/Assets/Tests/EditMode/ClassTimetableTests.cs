using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の教室の時間割と板書 (#439)。timetable.json を読み、時刻から授業の有無・科目・板書の行数が決まるかを確かめる。
    /// </summary>
    public sealed class ClassTimetableTests
    {
        private const string DataPath = "Assets/Data/Classes/timetable.json";
        private static readonly string[] Fields = { "math", "physics", "info" };
        private const char Newline = (char)10;

        private ClassTimetable _table;

        [OneTimeSetUp]
        public void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            Assert.IsNotNull(asset, DataPath + " が無い");
            _table = ClassTimetable.Parse(asset.text);
            Assert.IsNotNull(_table, DataPath + " が読めない");
        }

        [Test]
        public void 時限は1限から5限で_昼と夕方のチャイムで終わる()
        {
            Assert.AreEqual(5, _table.Periods.Count);
            for (int i = 0; i < _table.Periods.Count; i++)
            {
                Assert.AreEqual(i + 1, _table.Periods[i].Number);
                Assert.Less(_table.Periods[i].StartHour, _table.Periods[i].EndHour);
                if (i > 0)
                {
                    Assert.Less(_table.Periods[i - 1].EndHour, _table.Periods[i].StartHour, "休み時間が無い");
                }
            }

            Assert.AreEqual(12f, _table.Periods[1].EndHour, 1e-4f, "2 限は 12 時のチャイムで終わる");
            Assert.AreEqual(17f, _table.Periods[4].EndHour, 1e-4f, "5 限は 17 時のチャイムで終わる");
        }

        [Test]
        public void 教室は104と202と304で_101は使わない()
        {
            var ids = new List<string>();
            foreach (ClassTimetable.Room room in _table.Rooms)
            {
                ids.Add(room.Id);
            }

            CollectionAssert.AreEquivalent(new[] { "104", "202", "304" }, ids);
        }

        [Test]
        public void 一週間のどのコマも_科目は空か時間割にある科目で_同じ時限の教室で分野が重ならない()
        {
            var counts = new Dictionary<string, int>();
            for (int weekday = 0; weekday < ClassTimetable.WeekLength; weekday++)
            {
                for (int period = 1; period <= 5; period++)
                {
                    var fields = new HashSet<string>();
                    foreach (ClassTimetable.Room room in _table.Rooms)
                    {
                        string id = room.SubjectIdAt(weekday, period);
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        Assert.IsTrue(_table.TryGetSubject(id, out ClassTimetable.Subject subject),
                            room.Id + " の " + weekday + " 曜 " + period + " 限の科目 " + id + " が無い");
                        Assert.IsTrue(fields.Add(subject.Field),
                            weekday + " 曜 " + period + " 限に " + subject.Field + " が 2 室ある");
                        counts[subject.Field] = counts.TryGetValue(subject.Field, out int n) ? n + 1 : 1;
                    }
                }
            }

            // 数学・物理・情報が同じくらい（どの分野も最も多い分野の 3/4 以上）。
            int max = 0;
            foreach (string field in Fields)
            {
                Assert.IsTrue(counts.ContainsKey(field), field + " の授業が無い");
                max = Mathf.Max(max, counts[field]);
            }

            foreach (string field in Fields)
            {
                Assert.GreaterOrEqual(counts[field] * 4, max * 3, field + " の授業が少ない");
            }
        }

        [Test]
        public void どの分野にも講師がいて_どの科目も板書が5行ある()
        {
            foreach (string field in Fields)
            {
                Assert.IsNotNull(_table.LecturerFor(field), field + " の講師が無い");
            }

            foreach (ClassTimetable.Subject subject in _table.Subjects)
            {
                CollectionAssert.Contains(Fields, subject.Field, subject.Id);
                Assert.AreEqual(5, subject.Lines.Count, subject.Id + " の板書の行数");
                foreach (ClassTimetable.BoardLine line in subject.Lines)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(line.Ja), subject.Id);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(line.En), subject.Id);
                }
            }
        }

        [Test]
        public void 月曜1限の104は微分積分で_板書は時間とともに1行ずつ増える()
        {
            ClassTimetable.Session start = _table.SessionAt("104", 1, 9f);
            Assert.IsTrue(start.IsInClass);
            Assert.AreEqual("calculus", start.SubjectId);
            Assert.AreEqual(1, start.PeriodNumber);
            Assert.AreEqual(0, start.Weekday);
            Assert.AreEqual(1, start.BoardLines, "始まりは題目の 1 行だけ");

            // 1 限は 80 分。半分の 40 分で 1 + floor(0.5 × 5) = 3 行。
            Assert.AreEqual(3, _table.SessionAt("104", 1, 9f + 40f / 60f).BoardLines);
            Assert.AreEqual(5, _table.SessionAt("104", 1, 10f + 19f / 60f).BoardLines, "終わる前に全部の行が出る");

            int previous = 0;
            for (float hour = 9f; hour < 10f + 20f / 60f; hour += 1f / 60f)
            {
                int lines = _table.SessionAt("104", 1, hour).BoardLines;
                Assert.GreaterOrEqual(lines, previous, "板書の行が減った: " + hour);
                Assert.LessOrEqual(lines - previous, 1, "2 行以上いっぺんに増えた: " + hour);
                previous = lines;
            }
        }

        [Test]
        public void 休み時間と昼休みと放課後は授業が無い()
        {
            foreach (float hour in new[] { 8.5f, 10.5f, 12f, 12.5f, 14.2f, 15.7f, 17f, 20f })
            {
                Assert.IsFalse(_table.SessionAt("104", 1, hour).IsInClass, hour + " 時に授業がある");
            }

            Assert.IsFalse(_table.SessionAt("104", 1, 13.5f).IsInClass, "月曜 3 限の 104 は空き");
            Assert.IsFalse(_table.SessionAt("999", 1, 9.5f).IsInClass, "時間割に無い教室");
        }

        [Test]
        public void 曜日は1日目が月曜で_5日ごとに繰り返す()
        {
            Assert.AreEqual(0, ClassTimetable.WeekdayOf(1));
            Assert.AreEqual(4, ClassTimetable.WeekdayOf(5));
            Assert.AreEqual(0, ClassTimetable.WeekdayOf(6));
            Assert.AreEqual(_table.SessionAt("202", 2, 10.75f).SubjectId, _table.SessionAt("202", 7, 10.75f).SubjectId);
        }

        [Test]
        public void 板書の文は出ている行だけを並べ_英語では英語の行になる()
        {
            Assert.IsTrue(_table.TryGetSubject("calculus", out ClassTimetable.Subject subject));
            string[] two = Classroom.BoardText(subject, 2, false).Split(Newline);
            Assert.AreEqual(2, two.Length, "出ている 2 行だけ");
            StringAssert.Contains(subject.Lines[0].Ja, two[0]);
            Assert.AreEqual(subject.Lines[1].Ja, two[1]);
            StringAssert.Contains(subject.Lines[0].En, Classroom.BoardText(subject, 1, true));
            Assert.AreEqual(string.Empty, Classroom.BoardText(subject, 0, false));
        }

        [Test]
        public void 入室の会話は分野ごとの話題で_無い分野は情報の話題()
        {
            Assert.AreEqual("t_math", Classroom.EntryTopicFor("math"));
            Assert.AreEqual("t_physics", Classroom.EntryTopicFor("physics"));
            Assert.AreEqual("t_info", Classroom.EntryTopicFor(string.Empty));
        }

        [Test]
        public void 時刻の文字列を時間に直す()
        {
            Assert.IsTrue(ClassTimetable.TryParseClock("09:40", out float hour));
            Assert.AreEqual(9f + 40f / 60f, hour, 1e-4f);
            Assert.IsFalse(ClassTimetable.TryParseClock("9", out _));
            Assert.IsFalse(ClassTimetable.TryParseClock(string.Empty, out _));
        }
    }
}
