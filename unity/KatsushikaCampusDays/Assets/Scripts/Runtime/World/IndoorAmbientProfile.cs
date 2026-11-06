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
        /// <summary>空・赤道・地面の倍率。既定は元の DayNightCycle の値。</summary>
        public readonly struct Ratios
        {
            public readonly float Scale;
            public readonly float Equator;
            public readonly float Ground;

            public Ratios(float scale, float equator, float ground)
            {
                Scale = scale;
                Equator = equator;
                Ground = ground;
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

        public static Ratios For(string buildingId)
        {
            return buildingId == LectureId ? Lecture : Default;
        }

        /// <summary>基準の屋内の色と建物から、空・赤道・地面の色を返す。</summary>
        public static void Resolve(string buildingId, Color baseAmbient,
            out Color sky, out Color equator, out Color ground)
        {
            Ratios r = For(buildingId);
            Color scaled = new Color(baseAmbient.r * r.Scale, baseAmbient.g * r.Scale,
                baseAmbient.b * r.Scale, baseAmbient.a);
            sky = scaled;
            equator = scaled * r.Equator;
            ground = scaled * r.Ground;
        }
    }
}
