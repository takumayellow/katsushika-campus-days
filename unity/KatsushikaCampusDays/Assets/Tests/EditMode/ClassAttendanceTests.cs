using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の教室の、授業の前後の出入りと始めの小テスト (#424)。
    /// 授業の前は学生が 1 人ずつ席に着き、後は 1 人ずつ出ていく。前後の時間は休み時間の中に収まる。
    /// 時間割で小テストのコマは、始めの 15 分だけ板に問題を出す。部屋に入ったときの話題が会話のデータにそろっているかも確かめる。
    /// </summary>
    public sealed class ClassAttendanceTests
    {
        private const string DataPath = "Assets/Data/Classes/timetable.json";
        private const string DialogueFolder = "Assets/Data/Dialogue";
        private const char Newline = (char)10;

        /// <summary>席の数。教室の席より多めに取り、ばらし方の偏りを見る。</summary>
        private const int SeatCount = 16;

        private ClassTimetable _table;

        [OneTimeSetUp]
        public void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            Assert.IsNotNull(asset, DataPath + " が無い");
            _table = ClassTimetable.Parse(asset.text);
            Assert.IsNotNull(_table, DataPath + " が読めない");
        }

        private static float Hour(ClassTimetable.Period period, float minutesFromStart)
        {
            return period.StartHour + minutesFromStart / 60f;
        }

        [Test]
        public void 授業の前は始まりに近づくほど席が埋まり_後は1人ずつ減っていく()
        {
            // 104 の月曜 1 限（微分積分）。1 日目は月曜。
            ClassTimetable.Period first = _table.Periods[0];
            int previous = -1;
            for (float before = ClassAttendance.ArriveMinutes - 0.05f; before > 0.05f; before -= 0.25f)
            {
                ClassAttendance.Moment moment = ClassAttendance.At(_table, "104", 1, Hour(first, -before));
                Assert.AreEqual(ClassAttendance.Phase.Before, moment.Phase, "始まりの " + before + " 分前");
                Assert.AreEqual(0, moment.Session.BoardLines, "授業の前は板を消してある");
                int taken = ClassAttendance.TakenCount(moment, SeatCount);
                Assert.GreaterOrEqual(taken, previous, "始まりの " + before + " 分前に席が減った");
                previous = taken;
            }

            Assert.Less(ClassAttendance.TakenCount(ClassAttendance.At(_table, "104", 1, Hour(first, -7.9f)), SeatCount),
                SeatCount / 2, "8 分前はまだ半分も来ていない");
            Assert.AreEqual(SeatCount, ClassAttendance.TakenCount(ClassAttendance.At(_table, "104", 1, Hour(first, -0.9f)), SeatCount),
                "始まりの 1 分前には全員いる");

            float end = first.EndHour;
            previous = int.MaxValue;
            for (float after = 0f; after < ClassAttendance.LeaveMinutes; after += 0.25f)
            {
                ClassAttendance.Moment moment = ClassAttendance.At(_table, "104", 1, end + after / 60f);
                Assert.AreEqual(ClassAttendance.Phase.After, moment.Phase, "終わりの " + after + " 分後");
                Assert.AreEqual(moment.Session.Subject.Lines.Count, moment.Session.BoardLines, "授業の後は板書が残っている");
                int taken = ClassAttendance.TakenCount(moment, SeatCount);
                Assert.LessOrEqual(taken, previous, "終わりの " + after + " 分後に席が増えた");
                previous = taken;
            }

            Assert.AreEqual(SeatCount, ClassAttendance.TakenCount(ClassAttendance.At(_table, "104", 1, end + 0.4f / 60f), SeatCount),
                "終わった直後はまだ全員いる");
            Assert.AreEqual(ClassAttendance.Phase.None,
                ClassAttendance.At(_table, "104", 1, end + (ClassAttendance.LeaveMinutes + 0.1f) / 60f).Phase,
                "終わって 5 分を過ぎたら誰もいない");
        }

        [Test]
        public void 授業中は全員が席にいて_SessionAtと同じ授業になる()
        {
            foreach (ClassTimetable.Room room in _table.Rooms)
            {
                for (int day = 1; day <= ClassTimetable.WeekLength; day++)
                {
                    for (float hour = 8.5f; hour < 17.5f; hour += 1f / 12f)
                    {
                        ClassTimetable.Session expected = _table.SessionAt(room.Id, day, hour);
                        ClassAttendance.Moment moment = ClassAttendance.At(_table, room.Id, day, hour);
                        string where = room.Id + " の " + day + " 日目 " + hour.ToString("F2") + " 時";
                        Assert.AreEqual(expected.IsInClass, moment.Phase == ClassAttendance.Phase.During, where);
                        if (!expected.IsInClass)
                        {
                            continue;
                        }

                        Assert.AreEqual(expected.SubjectId, moment.Session.SubjectId, where);
                        Assert.AreEqual(expected.BoardLines, moment.Session.BoardLines, where);
                        Assert.AreEqual(SeatCount, ClassAttendance.TakenCount(moment, SeatCount), where);
                    }
                }
            }
        }

        [Test]
        public void 授業の前後の時間は最も短い休み時間より短く_昼休みと夜は誰もいない()
        {
            float shortest = float.MaxValue;
            for (int i = 1; i < _table.Periods.Count; i++)
            {
                shortest = Mathf.Min(shortest, (_table.Periods[i].StartHour - _table.Periods[i - 1].EndHour) * 60f);
            }

            Assert.Less(ClassAttendance.ArriveMinutes + ClassAttendance.LeaveMinutes, shortest,
                "前の授業の後と次の授業の前が重なる");

            foreach (ClassTimetable.Room room in _table.Rooms)
            {
                for (int day = 1; day <= ClassTimetable.WeekLength; day++)
                {
                    foreach (float hour in new[] { 8f, 12.5f, 17.5f, 20f })
                    {
                        Assert.AreEqual(ClassAttendance.Phase.None, ClassAttendance.At(_table, room.Id, day, hour).Phase,
                            room.Id + " の " + day + " 日目 " + hour + " 時に人がいる");
                    }
                }
            }
        }

        [Test]
        public void 席ごとの着く時刻と出る時刻は範囲の中で_隣の席とずれる()
        {
            for (int seat = 0; seat < SeatCount; seat++)
            {
                float lead = ClassAttendance.ArriveLead(seat);
                float lag = ClassAttendance.LeaveLag(seat);
                Assert.That(lead, Is.InRange(1f, ClassAttendance.ArriveMinutes), "席 " + seat + " の着く時刻");
                Assert.That(lag, Is.InRange(0.5f, ClassAttendance.LeaveMinutes), "席 " + seat + " の出る時刻");
                if (seat > 0)
                {
                    Assert.Greater(Mathf.Abs(lead - ClassAttendance.ArriveLead(seat - 1)), 0.5f, "席 " + seat + " が隣と同時に着く");
                }
            }
        }

        [Test]
        public void 小テストのコマは時間割の科目を指し_その科目に問題があり_月曜には無い()
        {
            Assert.IsNotEmpty(_table.Quizzes, "小テストのコマが無い");
            foreach (ClassTimetable.QuizSlot slot in _table.Quizzes)
            {
                string where = slot.RoomId + " の " + slot.Weekday + " 曜 " + slot.Period + " 限";
                Assert.IsTrue(_table.TryGetRoom(slot.RoomId, out ClassTimetable.Room room), where + " の教室が無い");
                Assert.IsTrue(_table.TryGetSubject(room.SubjectIdAt(slot.Weekday, slot.Period), out ClassTimetable.Subject subject),
                    where + " に授業が無い");
                Assert.GreaterOrEqual(subject.Quiz.Count, 2, where + " の " + subject.Id + " に問題が無い");
                foreach (ClassTimetable.BoardLine line in subject.Quiz)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(line.Ja) || string.IsNullOrEmpty(line.En), subject.Id + " の問題に空の行");
                }

                Assert.IsTrue(_table.HasQuiz(slot.RoomId, slot.Weekday, slot.Period), where);
                // PlayMode のテストは 1 日目（月曜）の授業中の板書の行数と講師の動きを撮るので、月曜には小テストを置かない。
                Assert.AreNotEqual(0, slot.Weekday, where + ": 月曜に小テストがある");
            }
        }

        [Test]
        public void 小テストは始めの15分だけで_同じ時刻のほかの教室ではしない()
        {
            // 202 の火曜 1 限（論理回路）。火曜は 2 日目。
            ClassTimetable.Period first = _table.Periods[0];
            ClassAttendance.Moment quiz = ClassAttendance.At(_table, "202", 2, Hour(first, 14f));
            Assert.AreEqual(ClassAttendance.Phase.During, quiz.Phase);
            Assert.IsTrue(quiz.IsQuiz, "始まって 14 分は小テスト中");

            ClassAttendance.Moment later = ClassAttendance.At(_table, "202", 2, Hour(first, ClassTimetable.QuizMinutes + 1f));
            Assert.AreEqual(ClassAttendance.Phase.During, later.Phase);
            Assert.IsFalse(later.IsQuiz, "15 分を過ぎたら普段の授業");

            Assert.IsFalse(ClassAttendance.At(_table, "104", 2, Hour(first, 5f)).IsQuiz, "104 の火曜 1 限は小テストのコマではない");
            Assert.IsFalse(ClassAttendance.At(_table, "202", 2, Hour(first, -3f)).IsQuiz, "授業の前は小テストではない");
            Assert.IsFalse(ClassAttendance.At(_table, "202", 1, Hour(_table.Periods[1], 5f)).IsQuiz, "月曜 2 限は小テストのコマではない");

            string text = Classroom.BoardText(quiz.Session.Subject.Quiz, quiz.Session.Subject.Quiz.Count, false);
            Assert.AreEqual(quiz.Session.Subject.Quiz.Count, text.Split(Newline).Length, "小テストの板書は問題を全部出す");
            StringAssert.Contains(quiz.Session.Subject.Quiz[0].Ja, text);
        }

        [Test]
        public void 入ったときの話題は授業中と小テストと前後で変わる()
        {
            ClassTimetable.Period first = _table.Periods[0];
            ClassAttendance.Moment quiz = ClassAttendance.At(_table, "202", 2, Hour(first, 5f));
            AssertTalk(quiz, false, Classroom.EntryDialogueId, Classroom.QuizTopic);
            AssertTalk(quiz, true, Classroom.EntryDialogueId, Classroom.QuizRecallTopic);

            ClassAttendance.Moment lesson = ClassAttendance.At(_table, "202", 2, Hour(first, 40f));
            AssertTalk(lesson, true, Classroom.EntryDialogueId, "t_info");

            ClassAttendance.Moment before = ClassAttendance.At(_table, "104", 1, Hour(first, -3f));
            AssertTalk(before, false, Classroom.BreakDialogueId, "t_before_math");

            ClassAttendance.Moment after = ClassAttendance.At(_table, "104", 1, first.EndHour + 1f / 60f);
            AssertTalk(after, false, Classroom.BreakDialogueId, "t_after_math");

            Assert.IsFalse(Classroom.TryEntryTalk(default, true, out _, out _), "授業の無い時間は話しかけない");
            Assert.AreEqual("t_before_info", ClassAttendance.BreakTopicFor(ClassAttendance.Phase.Before, "chemistry"),
                "話題の無い分野は情報の話題");
        }

        private static void AssertTalk(ClassAttendance.Moment moment, bool popQuizDone, string dialogueId, string topicId)
        {
            Assert.IsTrue(Classroom.TryEntryTalk(moment, popQuizDone, out string dialogue, out string topic), moment.Phase.ToString());
            Assert.AreEqual(dialogueId, dialogue, moment.Phase.ToString());
            Assert.AreEqual(topicId, topic, moment.Phase.ToString());
        }

        [Test]
        public void 時間割のどの授業の前中後に入っても_話題が会話のデータにある()
        {
            var topics = new Dictionary<string, HashSet<string>>();
            foreach (string id in new[] { Classroom.EntryDialogueId, Classroom.BreakDialogueId })
            {
                string path = Path.Combine(DialogueFolder, id + ".json");
                Assert.IsTrue(File.Exists(path), path + " が無い");
                DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>);
                Assert.IsNotNull(data, path + " が読めない");
                var ids = new HashSet<string>();
                foreach (DialogueTopic topic in data.Topics)
                {
                    ids.Add(topic.Id);
                    foreach (DialogueLine line in topic.Lines)
                    {
                        Assert.IsFalse(string.IsNullOrEmpty(line.TextEn), id + " の " + topic.Id + " に英語の無い行");
                    }
                }

                topics[id] = ids;
            }

            var phases = new[] { ClassAttendance.Phase.Before, ClassAttendance.Phase.During, ClassAttendance.Phase.After };
            foreach (ClassTimetable.Subject subject in _table.Subjects)
            {
                var session = new ClassTimetable.Session(subject, _table.Periods[0], 0, 1);
                foreach (ClassAttendance.Phase phase in phases)
                {
                    foreach (bool quiz in new[] { false, true })
                    {
                        foreach (bool done in new[] { false, true })
                        {
                            var moment = new ClassAttendance.Moment(phase, session, 1f, quiz && phase == ClassAttendance.Phase.During);
                            Assert.IsTrue(Classroom.TryEntryTalk(moment, done, out string dialogue, out string topic));
                            Assert.IsTrue(topics[dialogue].Contains(topic), dialogue + " に " + topic + " が無い（" + subject.Id + "）");
                        }
                    }
                }
            }
        }

        [Test]
        public void 休み時間の会話の話者名が辞書にある()
        {
            foreach (string lang in new[] { "ja", "en" })
            {
                string text = File.ReadAllText("Assets/Data/Localization/" + lang + ".json");
                StringAssert.Contains("\"" + DialogueData.SpeakerKeyPrefix + Classroom.BreakDialogueId + "\"", text, lang);
            }
        }
    }
}
