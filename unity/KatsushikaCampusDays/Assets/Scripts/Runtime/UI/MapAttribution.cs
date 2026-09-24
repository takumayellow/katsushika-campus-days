namespace KCD
{
    /// <summary>
    /// 地図データ（OpenStreetMap, ODbL）の帰属表示の文面 (#20)。
    /// タイトル画面のラベルとクレジット画面（CreditsView）は、どちらも辞書の同じキーから文面を取る。
    /// 辞書が欠けたり書き換えで帰属が消えたりしても、既定文に戻して必ず表示する。
    /// </summary>
    public static class MapAttribution
    {
        /// <summary>辞書のキー。CreditsView.BuildText の 1 行目も同じキーを読む。</summary>
        public const string Key = "ui.credits.map";

        /// <summary>OSM の著作権表示の決まり文句。言語によらずこの綴りで出す。</summary>
        public const string Notice = "© OpenStreetMap contributors";

        public const string License = "ODbL";

        public const string CopyrightUrl = "https://www.openstreetmap.org/copyright";

        public const string DefaultJa = "地図データ © OpenStreetMap contributors (ODbL)";

        public const string DefaultEn = "Map data © OpenStreetMap contributors (ODbL)";

        /// <summary>今の言語での帰属表示 1 行。</summary>
        public static string Line()
        {
            string fallback = L.Pick(DefaultJa, DefaultEn);
            return OrFallback(L.Get(Key, fallback), fallback);
        }

        /// <summary>著作権表示とライセンス名の両方を含むか。</summary>
        public static bool IsComplete(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Contains(Notice) && text.Contains(License);
        }

        /// <summary>text が帰属表示として足りていればそのまま、足りなければ fallback。</summary>
        public static string OrFallback(string text, string fallback)
        {
            return IsComplete(text) ? text : fallback;
        }
    }
}
