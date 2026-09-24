using UnityEngine;

namespace KCD
{
    /// <summary>
    /// ある時刻の空と光の色。<see cref="SkyPalette.Evaluate"/> が作り、DayNightCycle が
    /// 空のマテリアル（KCD/Sky）・霧・環境光・太陽へ配る。色はすべてガンマ空間の値
    /// （マテリアルの Color と RenderSettings の色と同じ扱い）。
    /// </summary>
    public readonly struct SkyState
    {
        /// <summary>真上の空の色。</summary>
        public readonly Color Zenith;
        /// <summary>地平線の空の色。霧の色と地平線より下の空もこの色にそろえる (#40)。</summary>
        public readonly Color Horizon;
        /// <summary>太陽のまわりの色。a は混ぜる強さ（夜は 0）。</summary>
        public readonly Color SunGlow;
        /// <summary>雲の色。a は雲の不透明度。</summary>
        public readonly Color Cloud;
        /// <summary>太陽光の色。</summary>
        public readonly Color SunColor;
        /// <summary>Trilight の上・横・下の環境光。</summary>
        public readonly Color AmbientSky;
        public readonly Color AmbientEquator;
        public readonly Color AmbientGround;
        /// <summary>太陽の明るさ（地平線に近いときの減衰は DayNightCycle が掛ける）。</summary>
        public readonly float SunIntensity;
        /// <summary>月明かりの明るさ（太陽が沈みきったときの値）。</summary>
        public readonly float MoonIntensity;
        /// <summary>星と月の見え方（0 = 見えない、1 = 夜空）。遠景の窓明かりもこれで灯る。</summary>
        public readonly float Stars;
        /// <summary>街灯の明るさ（0 = 消灯、1 = 点灯）。</summary>
        public readonly float LampGlow;
        /// <summary>RenderSettings.reflectionIntensity。夜に金属が昼の空を映して光るのを抑える。</summary>
        public readonly float ReflectionIntensity;

        public SkyState(
            Color zenith, Color horizon, Color sunGlow, Color cloud, Color sunColor,
            Color ambientSky, Color ambientEquator, Color ambientGround,
            float sunIntensity, float moonIntensity, float stars, float lampGlow, float reflectionIntensity)
        {
            Zenith = zenith;
            Horizon = horizon;
            SunGlow = sunGlow;
            Cloud = cloud;
            SunColor = sunColor;
            AmbientSky = ambientSky;
            AmbientEquator = ambientEquator;
            AmbientGround = ambientGround;
            SunIntensity = sunIntensity;
            MoonIntensity = moonIntensity;
            Stars = stars;
            LampGlow = lampGlow;
            ReflectionIntensity = reflectionIntensity;
        }

        /// <summary>霧の色。地平線の色と同じにして、霧に溶けた地面と空の境目を消す。</summary>
        public Color Fog => Horizon;

        /// <summary>遠くの街並みのシルエットの色。地平線より少し暗く、天頂の色へ寄せて空気遠近を出す。</summary>
        public Color Skyline
        {
            get
            {
                Color color = Color.Lerp(Horizon, Zenith, 0.3f) * 0.85f;
                color.a = 1f;
                return color;
            }
        }

        public static SkyState Lerp(SkyState a, SkyState b, float t)
        {
            return new SkyState(
                Color.Lerp(a.Zenith, b.Zenith, t),
                Color.Lerp(a.Horizon, b.Horizon, t),
                Color.Lerp(a.SunGlow, b.SunGlow, t),
                Color.Lerp(a.Cloud, b.Cloud, t),
                Color.Lerp(a.SunColor, b.SunColor, t),
                Color.Lerp(a.AmbientSky, b.AmbientSky, t),
                Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
                Color.Lerp(a.AmbientGround, b.AmbientGround, t),
                Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                Mathf.Lerp(a.MoonIntensity, b.MoonIntensity, t),
                Mathf.Lerp(a.Stars, b.Stars, t),
                Mathf.Lerp(a.LampGlow, b.LampGlow, t),
                Mathf.Lerp(a.ReflectionIntensity, b.ReflectionIntensity, t));
        }
    }

    /// <summary>
    /// 時刻ごとのトゥーン調の空の配色 (#8)。朝は淡い水色、昼は青、夕方は橙と桃色、夜は紺に星。
    /// 日の出 5:30・日の入り 18:30（DayNightCycle の既定）に合わせてキーを置き、あいだは線形に補間する。
    /// </summary>
    public static class SkyPalette
    {
        /// <summary>月明かりの色（青白い）。</summary>
        public static readonly Color MoonColor = new Color(0.55f, 0.63f, 0.90f);

        private struct Key
        {
            public float Hour;
            public SkyState State;
        }

        private static readonly SkyState Night = new SkyState(
            zenith: C(0.03f, 0.05f, 0.13f),
            horizon: C(0.11f, 0.14f, 0.26f),
            sunGlow: C(0.20f, 0.20f, 0.35f, 0f),
            cloud: C(0.16f, 0.18f, 0.28f, 0.55f),
            sunColor: C(0.50f, 0.55f, 0.80f),
            ambientSky: C(0.10f, 0.13f, 0.24f),
            ambientEquator: C(0.07f, 0.09f, 0.16f),
            ambientGround: C(0.035f, 0.04f, 0.06f),
            sunIntensity: 0f, moonIntensity: 0.24f, stars: 1f, lampGlow: 1f, reflectionIntensity: 0.25f);

        private static readonly SkyState Predawn = new SkyState(
            zenith: C(0.10f, 0.13f, 0.30f),
            horizon: C(0.42f, 0.36f, 0.50f),
            sunGlow: C(0.85f, 0.50f, 0.45f, 0.35f),
            cloud: C(0.45f, 0.38f, 0.50f, 0.70f),
            sunColor: C(1.00f, 0.60f, 0.45f),
            ambientSky: C(0.24f, 0.25f, 0.36f),
            ambientEquator: C(0.20f, 0.19f, 0.26f),
            ambientGround: C(0.09f, 0.08f, 0.10f),
            sunIntensity: 0f, moonIntensity: 0.12f, stars: 0.4f, lampGlow: 0.8f, reflectionIntensity: 0.4f);

        private static readonly SkyState Dawn = new SkyState(
            zenith: C(0.30f, 0.40f, 0.64f),
            horizon: C(0.94f, 0.72f, 0.64f),
            sunGlow: C(1.00f, 0.66f, 0.46f, 0.80f),
            cloud: C(1.00f, 0.80f, 0.74f, 0.85f),
            sunColor: C(1.00f, 0.72f, 0.52f),
            ambientSky: C(0.44f, 0.46f, 0.58f),
            ambientEquator: C(0.46f, 0.40f, 0.42f),
            ambientGround: C(0.17f, 0.15f, 0.14f),
            sunIntensity: 0.35f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 0.7f);

        private static readonly SkyState Morning = new SkyState(
            zenith: C(0.48f, 0.68f, 0.90f),
            horizon: C(0.82f, 0.89f, 0.95f),
            sunGlow: C(1.00f, 0.90f, 0.76f, 0.60f),
            cloud: C(1.00f, 0.98f, 0.95f, 0.85f),
            sunColor: C(1.00f, 0.88f, 0.74f),
            ambientSky: C(0.56f, 0.62f, 0.72f),
            ambientEquator: C(0.50f, 0.52f, 0.56f),
            ambientGround: C(0.26f, 0.26f, 0.22f),
            sunIntensity: 0.8f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 0.9f);

        private static readonly SkyState Day = new SkyState(
            zenith: C(0.26f, 0.53f, 0.90f),
            horizon: C(0.70f, 0.83f, 0.95f),
            sunGlow: C(1.00f, 0.96f, 0.88f, 0.45f),
            cloud: C(1.00f, 1.00f, 1.00f, 0.90f),
            sunColor: C(1.00f, 0.97f, 0.92f),
            ambientSky: C(0.62f, 0.66f, 0.74f),
            ambientEquator: C(0.52f, 0.55f, 0.60f),
            ambientGround: C(0.30f, 0.29f, 0.24f),
            sunIntensity: 1.15f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 1f);

        private static readonly SkyState Noon = new SkyState(
            zenith: C(0.24f, 0.51f, 0.90f),
            horizon: C(0.70f, 0.83f, 0.95f),
            sunGlow: C(1.00f, 0.97f, 0.90f, 0.40f),
            cloud: C(1.00f, 1.00f, 1.00f, 0.90f),
            sunColor: C(1.00f, 0.98f, 0.94f),
            ambientSky: C(0.64f, 0.68f, 0.76f),
            ambientEquator: C(0.54f, 0.57f, 0.62f),
            ambientGround: C(0.31f, 0.30f, 0.25f),
            sunIntensity: 1.3f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 1f);

        private static readonly SkyState Afternoon = new SkyState(
            zenith: C(0.26f, 0.53f, 0.90f),
            horizon: C(0.72f, 0.83f, 0.93f),
            sunGlow: C(1.00f, 0.94f, 0.84f, 0.45f),
            cloud: C(1.00f, 0.99f, 0.97f, 0.90f),
            sunColor: C(1.00f, 0.95f, 0.88f),
            ambientSky: C(0.62f, 0.65f, 0.72f),
            ambientEquator: C(0.53f, 0.54f, 0.58f),
            ambientGround: C(0.30f, 0.28f, 0.23f),
            sunIntensity: 1.2f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 1f);

        private static readonly SkyState LateAfternoon = new SkyState(
            zenith: C(0.30f, 0.50f, 0.84f),
            horizon: C(0.92f, 0.84f, 0.72f),
            sunGlow: C(1.00f, 0.82f, 0.56f, 0.60f),
            cloud: C(1.00f, 0.93f, 0.84f, 0.90f),
            sunColor: C(1.00f, 0.85f, 0.66f),
            ambientSky: C(0.58f, 0.58f, 0.64f),
            ambientEquator: C(0.56f, 0.50f, 0.48f),
            ambientGround: C(0.28f, 0.25f, 0.21f),
            sunIntensity: 0.95f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 0.95f);

        private static readonly SkyState Sunset = new SkyState(
            zenith: C(0.34f, 0.36f, 0.62f),
            horizon: C(0.99f, 0.62f, 0.42f),
            sunGlow: C(1.00f, 0.50f, 0.28f, 0.95f),
            cloud: C(1.00f, 0.70f, 0.64f, 0.90f),
            sunColor: C(1.00f, 0.60f, 0.36f),
            ambientSky: C(0.46f, 0.40f, 0.48f),
            ambientEquator: C(0.54f, 0.40f, 0.36f),
            ambientGround: C(0.20f, 0.16f, 0.15f),
            sunIntensity: 0.6f, moonIntensity: 0f, stars: 0f, lampGlow: 0f, reflectionIntensity: 0.7f);

        private static readonly SkyState Dusk = new SkyState(
            zenith: C(0.18f, 0.20f, 0.42f),
            horizon: C(0.70f, 0.44f, 0.50f),
            sunGlow: C(0.90f, 0.42f, 0.36f, 0.70f),
            cloud: C(0.72f, 0.46f, 0.56f, 0.80f),
            sunColor: C(0.90f, 0.50f, 0.40f),
            ambientSky: C(0.28f, 0.26f, 0.38f),
            ambientEquator: C(0.30f, 0.24f, 0.28f),
            ambientGround: C(0.11f, 0.09f, 0.10f),
            sunIntensity: 0f, moonIntensity: 0.06f, stars: 0.15f, lampGlow: 0.7f, reflectionIntensity: 0.45f);

        private static readonly SkyState BlueHour = new SkyState(
            zenith: C(0.07f, 0.09f, 0.24f),
            horizon: C(0.26f, 0.26f, 0.42f),
            sunGlow: C(0.50f, 0.32f, 0.42f, 0.30f),
            cloud: C(0.28f, 0.26f, 0.40f, 0.65f),
            sunColor: C(0.60f, 0.55f, 0.75f),
            ambientSky: C(0.14f, 0.16f, 0.28f),
            ambientEquator: C(0.11f, 0.12f, 0.20f),
            ambientGround: C(0.05f, 0.05f, 0.07f),
            sunIntensity: 0f, moonIntensity: 0.18f, stars: 0.7f, lampGlow: 1f, reflectionIntensity: 0.3f);

        // 時刻の昇順。最初と最後は同じ夜空にして 24 時と 0 時をつなぐ。
        private static readonly Key[] Keys =
        {
            new Key { Hour = 0f, State = Night },
            new Key { Hour = 4.6f, State = Night },
            new Key { Hour = 5.3f, State = Predawn },
            new Key { Hour = 6.0f, State = Dawn },
            new Key { Hour = 7.0f, State = Morning },
            new Key { Hour = 9.5f, State = Day },
            new Key { Hour = 12.0f, State = Noon },
            new Key { Hour = 15.0f, State = Afternoon },
            new Key { Hour = 16.8f, State = LateAfternoon },
            new Key { Hour = 18.0f, State = Sunset },
            new Key { Hour = 18.7f, State = Dusk },
            new Key { Hour = 19.4f, State = BlueHour },
            new Key { Hour = 20.2f, State = Night },
            new Key { Hour = 24f, State = Night },
        };

        /// <summary>時刻（0-24、範囲外は 24 で折り返す）の空と光。</summary>
        public static SkyState Evaluate(float hours)
        {
            float h = Mathf.Repeat(hours, 24f);
            for (int i = 1; i < Keys.Length; i++)
            {
                if (h <= Keys[i].Hour)
                {
                    Key a = Keys[i - 1];
                    Key b = Keys[i];
                    return SkyState.Lerp(a.State, b.State, Mathf.InverseLerp(a.Hour, b.Hour, h));
                }
            }

            return Keys[Keys.Length - 1].State;
        }

        private static Color C(float r, float g, float b, float a = 1f)
        {
            return new Color(r, g, b, a);
        }
    }
}
