namespace KCD
{
    /// <summary>一日のうちの時間帯。台詞やエンドの文を時刻で言い分けるのに使う (#41)。</summary>
    public enum DayPartKind
    {
        /// <summary>1 限が始まる前。</summary>
        Morning,

        /// <summary>1 限から 5 限の終わりまで（休み時間・昼休みを含む）。</summary>
        Class,

        /// <summary>5 限が終わってから日が暮れるまで。</summary>
        AfterSchool,

        /// <summary>日が暮れてから。</summary>
        Night
    }

    /// <summary>ゲーム内の時刻から時間帯を決める。境目は時間割（timetable.json）の 1 限の始まり・5 限の終わりと、日の入りに合わせる。</summary>
    public static class DayPart
    {
        public const float ClassStartHour = 9f;
        public const float ClassEndHour = 17f;
        public const float NightStartHour = DayNightCycle.DefaultSunsetHour;

        public static DayPartKind Of(float hours)
        {
            if (float.IsNaN(hours) || hours < ClassStartHour)
            {
                return DayPartKind.Morning;
            }

            if (hours < ClassEndHour)
            {
                return DayPartKind.Class;
            }

            return hours < NightStartHour ? DayPartKind.AfterSchool : DayPartKind.Night;
        }

        /// <summary>いまの時間帯。GameManager が無いとき（エディタのテストなど）は授業中として扱う。</summary>
        public static DayPartKind Current =>
            GameManager.Instance != null ? Of(GameManager.Instance.GameTimeHours) : DayPartKind.Class;
    }
}
