using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 講義棟の教室の時間割（Assets/Data/Classes/timetable.json、DataBundler が Resources/KCD/Classes へ複製, #439）。
    /// 1〜5 限の時刻、科目と板書、分野ごとの講師の見た目、教室ごとの 1 週ぶん（月〜金 × 1〜5 限）の割り当てを持つ。
    /// 時刻と日から「授業をしているか・どの科目か・板書が何行まで進んだか」を返す純関数なので、
    /// EditMode のテストからそのまま確かめられる。
    /// </summary>
    public sealed class ClassTimetable
    {
        public const string ResourcePath = "KCD/Classes/timetable";

        /// <summary>1 週の授業のある日数（月〜金）。ゲームの日数をこの数で回して曜日にする。</summary>
        public const int WeekLength = 5;

        /// <summary>1 つの時限。開始以上・終了未満を授業中とする。</summary>
        public sealed class Period
        {
            public Period(int number, float startHour, float endHour)
            {
                Number = number;
                StartHour = startHour;
                EndHour = endHour;
            }

            public int Number { get; }
            public float StartHour { get; }
            public float EndHour { get; }

            public bool Contains(float hour) => hour >= StartHour && hour < EndHour;

            /// <summary>授業の進み具合（0 = 始まり、1 = 終わり）。</summary>
            public float Progress(float hour)
            {
                float length = EndHour - StartHour;
                return length > 0f ? Mathf.Clamp01((hour - StartHour) / length) : 0f;
            }
        }

        /// <summary>板書の 1 行。英語が無ければ日本語を出す。</summary>
        public sealed class BoardLine
        {
            public BoardLine(string ja, string en)
            {
                Ja = ja ?? string.Empty;
                En = string.IsNullOrEmpty(en) ? Ja : en;
            }

            public string Ja { get; }
            public string En { get; }

            public string Pick(bool english) => english ? En : Ja;
        }

        /// <summary>科目。field は math / physics / info。板書の最初の行は題目で、授業中はいつも出す。</summary>
        public sealed class Subject
        {
            public Subject(string id, string field, string name, string nameEn, IReadOnlyList<BoardLine> lines)
            {
                Id = id ?? string.Empty;
                Field = field ?? string.Empty;
                Name = name ?? string.Empty;
                NameEn = string.IsNullOrEmpty(nameEn) ? Name : nameEn;
                Lines = lines;
            }

            public string Id { get; }
            public string Field { get; }
            public string Name { get; }
            public string NameEn { get; }
            public IReadOnlyList<BoardLine> Lines { get; }
        }

        /// <summary>分野ごとの講師の見た目（体と、髪・上着・ズボン・靴の色と身長）。</summary>
        public sealed class Lecturer
        {
            public Lecturer(string field, MobSchedule.Variant look)
            {
                Field = field ?? string.Empty;
                Look = look;
            }

            public string Field { get; }
            public MobSchedule.Variant Look { get; }
        }

        /// <summary>教室ごとの 1 週ぶん。Week[曜日][時限 - 1] が科目 id（空ならその時限は授業が無い）。</summary>
        public sealed class Room
        {
            public Room(string id, IReadOnlyList<IReadOnlyList<string>> week)
            {
                Id = id ?? string.Empty;
                Week = week;
            }

            public string Id { get; }
            public IReadOnlyList<IReadOnlyList<string>> Week { get; }

            /// <summary>その曜日・時限の科目 id。範囲の外か空きなら空。</summary>
            public string SubjectIdAt(int weekday, int periodNumber)
            {
                if (weekday < 0 || weekday >= Week.Count)
                {
                    return string.Empty;
                }

                IReadOnlyList<string> day = Week[weekday];
                int index = periodNumber - 1;
                return index >= 0 && index < day.Count ? day[index] ?? string.Empty : string.Empty;
            }
        }

        /// <summary>ある教室・ある時刻の授業。授業が無ければ <see cref="IsInClass"/> が false で、ほかは空。</summary>
        public readonly struct Session
        {
            public Session(Subject subject, Period period, int weekday, int boardLines)
            {
                Subject = subject;
                Period = period;
                Weekday = weekday;
                BoardLines = boardLines;
            }

            public Subject Subject { get; }
            public Period Period { get; }
            public int Weekday { get; }

            /// <summary>板書が何行まで進んだか（題目を含む）。授業が無ければ 0。</summary>
            public int BoardLines { get; }

            public bool IsInClass => Subject != null && Period != null;
            public string SubjectId => Subject != null ? Subject.Id : string.Empty;
            public int PeriodNumber => Period != null ? Period.Number : 0;
        }

        private static readonly Color FallbackColor = new Color(0.5f, 0.5f, 0.5f);

        private readonly Dictionary<string, Subject> _subjects;
        private readonly Dictionary<string, Room> _rooms;

        private ClassTimetable(
            IReadOnlyList<Period> periods, IReadOnlyList<BoardLine> weekdays, IReadOnlyList<Lecturer> lecturers,
            Dictionary<string, Subject> subjects, Dictionary<string, Room> rooms)
        {
            Periods = periods;
            Weekdays = weekdays;
            Lecturers = lecturers;
            _subjects = subjects;
            _rooms = rooms;
        }

        public IReadOnlyList<Period> Periods { get; }

        /// <summary>曜日の名前（月〜金）。</summary>
        public IReadOnlyList<BoardLine> Weekdays { get; }

        public IReadOnlyList<Lecturer> Lecturers { get; }
        public IEnumerable<Subject> Subjects => _subjects.Values;
        public IEnumerable<Room> Rooms => _rooms.Values;

        /// <summary>JSON から作る。読めなければ null。</summary>
        public static ClassTimetable Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || !(MiniJson.Deserialize(json) is Dictionary<string, object> root))
            {
                return null;
            }

            var periods = new List<Period>();
            foreach (object node in MiniJson.GetArray(root, "periods"))
            {
                if (node is Dictionary<string, object> p
                    && TryParseClock(MiniJson.GetString(p, "start"), out float start)
                    && TryParseClock(MiniJson.GetString(p, "end"), out float end) && end > start)
                {
                    periods.Add(new Period(MiniJson.GetInt(p, "period", periods.Count + 1), start, end));
                }
            }

            var weekdays = new List<BoardLine>();
            foreach (object node in MiniJson.GetArray(root, "weekdays"))
            {
                if (node is Dictionary<string, object> w)
                {
                    weekdays.Add(new BoardLine(MiniJson.GetString(w, "ja"), MiniJson.GetString(w, "en")));
                }
            }

            var lecturers = new List<Lecturer>();
            foreach (object node in MiniJson.GetArray(root, "lecturers"))
            {
                if (node is Dictionary<string, object> l)
                {
                    string field = MiniJson.GetString(l, "field");
                    lecturers.Add(new Lecturer(field, new MobSchedule.Variant(
                        "class_lecturer_" + field,
                        MiniJson.GetString(l, "body", "prof"),
                        ParseColor(MiniJson.GetString(l, "hair")),
                        ParseColor(MiniJson.GetString(l, "top")),
                        ParseColor(MiniJson.GetString(l, "bottom")),
                        ParseColor(MiniJson.GetString(l, "shoes")),
                        MiniJson.GetFloat(l, "height", 1.72f))));
                }
            }

            var subjects = new Dictionary<string, Subject>();
            foreach (object node in MiniJson.GetArray(root, "subjects"))
            {
                Subject subject = ParseSubject(node as Dictionary<string, object>);
                if (subject != null && !string.IsNullOrEmpty(subject.Id))
                {
                    subjects[subject.Id] = subject;
                }
            }

            var rooms = new Dictionary<string, Room>();
            foreach (object node in MiniJson.GetArray(root, "rooms"))
            {
                if (node is Dictionary<string, object> r)
                {
                    string id = MiniJson.GetString(r, "id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        rooms[id] = new Room(id, ParseWeek(MiniJson.GetArray(r, "week")));
                    }
                }
            }

            return new ClassTimetable(periods, weekdays, lecturers, subjects, rooms);
        }

        /// <summary>Resources から読む。無ければ null。</summary>
        public static ClassTimetable LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            return asset != null ? Parse(asset.text) : null;
        }

        /// <summary>ゲームの日数（1 日目から）を曜日（0 = 月 … 4 = 金）にする。1 日目は月曜。</summary>
        public static int WeekdayOf(int dayNumber)
        {
            int index = (dayNumber - 1) % WeekLength;
            return index < 0 ? index + WeekLength : index;
        }

        /// <summary>
        /// 板書が何行まで進んだか。題目の 1 行は始まりから出ていて、残りは授業の長さを行数で割った間隔で 1 行ずつ増える。
        /// 終わりの手前で全部の行が出そろう。
        /// </summary>
        public static int BoardLinesAt(float progress, int lineCount)
        {
            if (lineCount <= 0)
            {
                return 0;
            }

            int shown = 1 + Mathf.FloorToInt(Mathf.Clamp01(progress) * lineCount);
            return Mathf.Clamp(shown, 1, lineCount);
        }

        public bool TryGetSubject(string id, out Subject subject)
        {
            subject = null;
            return !string.IsNullOrEmpty(id) && _subjects.TryGetValue(id, out subject);
        }

        public bool TryGetRoom(string id, out Room room)
        {
            room = null;
            return !string.IsNullOrEmpty(id) && _rooms.TryGetValue(id, out room);
        }

        /// <summary>分野の講師。無ければ null。</summary>
        public Lecturer LecturerFor(string field)
        {
            foreach (Lecturer lecturer in Lecturers)
            {
                if (lecturer.Field == field)
                {
                    return lecturer;
                }
            }

            return null;
        }

        /// <summary>その時刻の時限。休み時間・昼休み・放課後なら null。</summary>
        public Period PeriodAt(float hour)
        {
            foreach (Period period in Periods)
            {
                if (period.Contains(hour))
                {
                    return period;
                }
            }

            return null;
        }

        /// <summary>教室・ゲームの日数・時刻から、そのときの授業。授業が無ければ IsInClass が false。</summary>
        public Session SessionAt(string roomId, int dayNumber, float hour)
        {
            int weekday = WeekdayOf(dayNumber);
            Period period = PeriodAt(hour);
            if (period == null || !TryGetRoom(roomId, out Room room)
                || !TryGetSubject(room.SubjectIdAt(weekday, period.Number), out Subject subject))
            {
                return new Session(null, null, weekday, 0);
            }

            return new Session(subject, period, weekday, BoardLinesAt(period.Progress(hour), subject.Lines.Count));
        }

        /// <summary>"09:40" → 9.6667。読めなければ false。</summary>
        public static bool TryParseClock(string text, out float hour)
        {
            hour = 0f;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)
                || h < 0 || h > 24 || m < 0 || m > 59)
            {
                return false;
            }

            hour = h + m / 60f;
            return true;
        }

        private static Subject ParseSubject(Dictionary<string, object> s)
        {
            if (s == null)
            {
                return null;
            }

            var lines = new List<BoardLine>();
            foreach (object node in MiniJson.GetArray(s, "lines"))
            {
                if (node is Dictionary<string, object> line)
                {
                    lines.Add(new BoardLine(MiniJson.GetString(line, "ja"), MiniJson.GetString(line, "en")));
                }
            }

            return new Subject(
                MiniJson.GetString(s, "id"),
                MiniJson.GetString(s, "field"),
                MiniJson.GetString(s, "name"),
                MiniJson.GetString(s, "name_en"),
                lines);
        }

        private static IReadOnlyList<IReadOnlyList<string>> ParseWeek(List<object> week)
        {
            var days = new List<IReadOnlyList<string>>();
            foreach (object node in week)
            {
                var day = new List<string>();
                if (node is List<object> slots)
                {
                    foreach (object slot in slots)
                    {
                        day.Add(slot as string ?? string.Empty);
                    }
                }

                days.Add(day);
            }

            return days;
        }

        private static Color ParseColor(string hex)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color)
                ? color
                : FallbackColor;
        }
    }
}
