using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 屋内の環境光（Trilight の空・赤道・地面の 3 色）を建物ごとに決める。
    /// 天井は下向きの面なので地面の色だけを受ける。地面の色を暗くしすぎると、
    /// 床からの照り返しが無いぶん天井が写真より黒く潰れる。
    /// </summary>
    public static class IndoorAmbientProfile
    {
        /// <summary>空・赤道・地面の倍率と色味。既定は元の DayNightCycle の値。</summary>
        public readonly struct Ratios
        {
            public readonly float Scale;
            public readonly float Equator;
            public readonly float Ground;

            /// <summary>RGB ごとに掛ける色味。白なら基準の色のまま。</summary>
            public readonly Color Tint;

            public Ratios(float scale, float equator, float ground)
                : this(scale, equator, ground, Color.white)
            {
            }

            public Ratios(float scale, float equator, float ground, Color tint)
            {
                Scale = scale;
                Equator = equator;
                Ground = ground;
                Tint = tint;
            }
        }

        /// <summary>講義棟の建物 ID（Interior_&lt;ID&gt; と BuildingDoors の値）。</summary>
        public const string LectureId = "lecture";

        public static readonly Ratios Default = new Ratios(1f, 0.92f, 0.7f);

        /// <summary>
        /// 講義棟は天井と打放しの写真の色に合わせる（#423）。廊下の天井と 2F より上の
        /// 灰色の天井は床の照り返しで写真の明るさになるので、地面の色を赤道に近づける。
        /// </summary>
        public static readonly Ratios Lecture = new Ratios(1.25f, 0.95f, 0.92f);

        /// <summary>共創棟の建物 ID。</summary>
        public const string KyosoId = "kyoso";

        /// <summary>
        /// 共創棟は 1F のラーニングスクエアの写真に合わせる（#425）。写真は窓と照明で
        /// 床も木ルーバーの天井も明るく、電球色に寄っているので、全体を明るくして青みを抜く。
        /// </summary>
        public static readonly Ratios Kyoso = new Ratios(1.6f, 0.95f, 0.8f, new Color(1.05f, 1f, 0.9f, 1f));

        public static Ratios For(string buildingId)
        {
            switch (buildingId)
            {
                case LectureId:
                    return Lecture;
                case KyosoId:
                    return Kyoso;
                default:
                    return Default;
            }
        }

        /// <summary>基準の屋内の色と建物から、空・赤道・地面の色を返す。</summary>
        public static void Resolve(string buildingId, Color baseAmbient,
            out Color sky, out Color equator, out Color ground)
        {
            Ratios r = For(buildingId);
            Color scaled = new Color(baseAmbient.r * r.Scale * r.Tint.r, baseAmbient.g * r.Scale * r.Tint.g,
                baseAmbient.b * r.Scale * r.Tint.b, baseAmbient.a);
            sky = scaled;
            equator = scaled * r.Equator;
            ground = scaled * r.Ground;
        }
    }
}
