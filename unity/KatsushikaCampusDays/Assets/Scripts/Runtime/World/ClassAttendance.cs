namespace KCD
{
    /// <summary>
    /// 時間割の教室の、授業の前後を含めた人の出入り (#424)。
    /// 授業の始まる <see cref="ArriveMinutes"/> 分前から学生が 1 人ずつ席に着き、終わってから
    /// <see cref="LeaveMinutes"/> 分のうちに 1 人ずつ出ていく。授業の始めの小テストの時間もここで決める。
    /// 時刻と日だけから決まる純関数なので、EditMode のテストからそのまま確かめられる。
    /// </summary>
    public static class ClassAttendance
    {
        /// <summary>最初の学生が席に着く、授業の始まりからの分（ゲームの分）。</summary>
        public const float ArriveMinutes = 8f;

        /// <summary>最後の学生が出ていく、授業の終わりからの分。前後を足しても最も短い休み時間（15 分）より短い。</summary>
        public const float LeaveMinutes = 5f;

        /// <summary>席ごとの着く時刻と出る時刻をばらす、席の番号にかける数（黄金比の端数）。</summary>
        private const float Spread = 0.618034f;

        /// <summary>授業との位置関係。</summary>
        public enum Phase
        {
            /// <summary>前後の時間にも入らない（人はいない）。</summary>
            None,

            /// <summary>授業の前。学生が集まってくる。</summary>
            Before,

            /// <summary>授業中。</summary>
            During,

            /// <summary>授業の後。学生が出ていく。</summary>
            After,
        }

        /// <summary>ある時刻の、教室の 1 つの授業との関係。</summary>
        public readonly struct Moment
        {
            public Moment(Phase phase, ClassTimetable.Session session, float minutes, bool quiz)
            {
                Phase = phase;
                Session = session;
                Minutes = minutes;
                IsQuiz = quiz;
            }

            public Phase Phase { get; }

            /// <summary>
            /// 関係する授業。授業中は板書の行数も入る。前は板を消してあるので 0 行、後は全部の行が残っている。
            /// </summary>
            public ClassTimetable.Session Session { get; }

            /// <summary>前なら始まりまでの分、授業中なら始まってからの分、後なら終わってからの分。</summary>
            public float Minutes { get; }

            /// <summary>授業の始めの小テストの時間か。</summary>
            public bool IsQuiz { get; }

            public bool HasClass => Phase != Phase.None;
        }

        /// <summary>教室・日・時刻から、その時の授業との関係。</summary>
        public static Moment At(ClassTimetable timetable, string roomId, int dayNumber, float hour)
        {
            if (timetable == null || !timetable.TryGetRoom(roomId, out ClassTimetable.Room room))
            {
                return default;
            }

            int weekday = ClassTimetable.WeekdayOf(dayNumber);
            foreach (ClassTimetable.Period period in timetable.Periods)
            {
                if (!timetable.TryGetSubject(room.SubjectIdAt(weekday, period.Number), out ClassTimetable.Subject subject))
                {
                    continue;
                }

                float sinceStart = (hour - period.StartHour) * 60f;
                float sinceEnd = (hour - period.EndHour) * 60f;
                if (period.Contains(hour))
                {
                    var during = new ClassTimetable.Session(subject, period, weekday,
                        ClassTimetable.BoardLinesAt(period.Progress(hour), subject.Lines.Count));
                    bool quiz = sinceStart < ClassTimetable.QuizMinutes
                                && timetable.HasQuiz(roomId, weekday, period.Number);
                    return new Moment(Phase.During, during, sinceStart, quiz);
                }

                if (sinceStart < 0f && -sinceStart <= ArriveMinutes)
                {
                    return new Moment(Phase.Before, new ClassTimetable.Session(subject, period, weekday, 0), -sinceStart, false);
                }

                if (sinceEnd >= 0f && sinceEnd < LeaveMinutes)
                {
                    var after = new ClassTimetable.Session(subject, period, weekday, subject.Lines.Count);
                    return new Moment(Phase.After, after, sinceEnd, false);
                }
            }

            return default;
        }

        /// <summary>席の番号から 0〜1 の端数。隣どうしの席が続けて着かないようにばらす。</summary>
        private static float Fraction(int seat)
        {
            float x = (seat + 1) * Spread;
            return x - (int)x;
        }

        /// <summary>その席の学生が着く、授業の始まりの何分前か（1 分〜<see cref="ArriveMinutes"/> 分）。</summary>
        public static float ArriveLead(int seat)
        {
            return 1f + Fraction(seat) * (ArriveMinutes - 1f);
        }

        /// <summary>その席の学生が出ていく、授業の終わりの何分後か（0.5 分〜<see cref="LeaveMinutes"/> 分）。</summary>
        public static float LeaveLag(int seat)
        {
            return 0.5f + Fraction(seat * 3 + 1) * (LeaveMinutes - 0.5f);
        }

        /// <summary>その時、その席に学生がいるか。授業中は全員いる。</summary>
        public static bool SeatTaken(Moment moment, int seat)
        {
            switch (moment.Phase)
            {
                case Phase.During:
                    return true;
                case Phase.Before:
                    return moment.Minutes <= ArriveLead(seat);
                case Phase.After:
                    return moment.Minutes < LeaveLag(seat);
                default:
                    return false;
            }
        }

        /// <summary>席の数のうち、いま学生がいる数。</summary>
        public static int TakenCount(Moment moment, int seatCount)
        {
            int count = 0;
            for (int i = 0; i < seatCount; i++)
            {
                if (SeatTaken(moment, i))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>休み時間に部屋へ入ったときの話題（Dialogue/class_break.json）。前は t_before_分野、後は t_after_分野。</summary>
        public static string BreakTopicFor(Phase phase, string field)
        {
            string fieldPart = Classroom.EntryTopicFor(field).Substring(2);
            return (phase == Phase.Before ? "t_before_" : "t_after_") + fieldPart;
        }
    }
}
