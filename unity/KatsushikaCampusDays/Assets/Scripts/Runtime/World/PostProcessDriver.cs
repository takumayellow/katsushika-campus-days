using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD
{
    /// <summary>
    /// 時刻に合わせて色温度・彩度・コントラスト・ビネットを揺らす。屋内では中立に戻す。
    /// 夜は暗くせず、寒色・低い彩度・少し高いコントラストで夜らしく見せる（#166）。
    /// Volume のプロファイルはランタイム側でインスタンス化されるので、アセットは汚さない。
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public sealed class PostProcessDriver : MonoBehaviour
    {
        private struct Key
        {
            public float Hour;
            public float Temperature;
            public float Saturation;
            public float Vignette;
            public float Contrast;

            public Key(float hour, float temperature, float saturation, float vignette, float contrast)
            {
                Hour = hour;
                Temperature = temperature;
                Saturation = saturation;
                Vignette = vignette;
                Contrast = contrast;
            }

            public static Key Lerp(Key a, Key b, float t)
            {
                return new Key(
                    Mathf.Lerp(a.Hour, b.Hour, t),
                    Mathf.Lerp(a.Temperature, b.Temperature, t),
                    Mathf.Lerp(a.Saturation, b.Saturation, t),
                    Mathf.Lerp(a.Vignette, b.Vignette, t),
                    Mathf.Lerp(a.Contrast, b.Contrast, t));
            }
        }

        private static readonly Key[] Keys =
        {
            new Key(5.0f, -12f, -16f, 0.27f, 8f),
            new Key(6.5f, 8f, 4f, 0.26f, 0f),
            new Key(9.0f, 0f, 10f, 0.22f, 0f),
            new Key(15.0f, 0f, 10f, 0.22f, 0f),
            new Key(17.0f, 12f, 12f, 0.24f, 0f),
            new Key(18.3f, 25f, 18f, 0.26f, 2f),
            new Key(19.5f, -12f, -16f, 0.27f, 8f),
            new Key(24.0f, -12f, -16f, 0.27f, 8f)
        };

        [SerializeField] private float _lerpSpeed = 1.5f;

        private Volume _volume;
        private ColorAdjustments _color;
        private Vignette _vignette;
        private WhiteBalance _whiteBalance;
        private DayNightCycle _cycle;
        private float _baseSaturation;
        private float _baseContrast;
        private float _baseVignette;
        private float _temperature;
        private float _saturation;
        private float _contrast;
        private float _vignetteValue;

        private void Awake()
        {
            _volume = GetComponent<Volume>();
            VolumeProfile profile = _volume.profile;
            if (profile == null)
            {
                enabled = false;
                return;
            }

            if (!profile.TryGet(out _color))
            {
                _color = profile.Add<ColorAdjustments>(true);
            }

            if (!profile.TryGet(out _vignette))
            {
                _vignette = profile.Add<Vignette>(true);
            }

            if (!profile.TryGet(out _whiteBalance))
            {
                _whiteBalance = profile.Add<WhiteBalance>(true);
            }

            _baseSaturation = _color.saturation.value;
            _baseContrast = _color.contrast.value;
            _baseVignette = _vignette.intensity.value;
            _whiteBalance.temperature.overrideState = true;
            _color.saturation.overrideState = true;
            _color.contrast.overrideState = true;
            _vignette.intensity.overrideState = true;
        }

        private void Start()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            Evaluate(out _temperature, out _saturation, out _vignetteValue, out _contrast);
            Apply();
        }

        private void Update()
        {
            Evaluate(out float temperature, out float saturation, out float vignette, out float contrast);
            float t = 1f - Mathf.Exp(-_lerpSpeed * Time.deltaTime);
            _temperature = Mathf.Lerp(_temperature, temperature, t);
            _saturation = Mathf.Lerp(_saturation, saturation, t);
            _contrast = Mathf.Lerp(_contrast, contrast, t);
            _vignetteValue = Mathf.Lerp(_vignetteValue, vignette, t);
            Apply();
        }

        private void Apply()
        {
            _whiteBalance.temperature.value = _temperature;
            _color.saturation.value = _baseSaturation + _saturation;
            _color.contrast.value = _baseContrast + _contrast;
            _vignette.intensity.value = _vignetteValue;
        }

        private void Evaluate(out float temperature, out float saturation, out float vignette, out float contrast)
        {
            InteriorLoader loader = InteriorLoader.Instance;
            bool inside = loader != null && loader.IsInside;
            float hours = _cycle != null ? _cycle.Hours : 12f;
            Grade(hours, inside, _baseVignette, out temperature, out saturation, out vignette, out contrast);
        }

        /// <summary>
        /// その時刻の色温度・彩度（プロファイルの彩度への足し分）・ビネット。キーの間は線形につなぎ、
        /// 時刻は 24 時で折り返す。屋内は時刻によらず中立（色温度 0、彩度 +4、ビネットはプロファイルの値）。
        /// </summary>
        public static void Grade(float hours, bool inside, float baseVignette,
            out float temperature, out float saturation, out float vignette)
        {
            Grade(hours, inside, baseVignette, out temperature, out saturation, out vignette, out _);
        }

        /// <summary>
        /// <see cref="Grade(float, bool, float, out float, out float, out float)"/> に、
        /// コントラスト（プロファイルのコントラストへの足し分、屋内は 0）を加えたもの。
        /// </summary>
        public static void Grade(float hours, bool inside, float baseVignette,
            out float temperature, out float saturation, out float vignette, out float contrast)
        {
            if (inside)
            {
                temperature = 0f;
                saturation = 4f;
                vignette = baseVignette;
                contrast = 0f;
                return;
            }

            Key key = Sample(Mathf.Repeat(hours, 24f));
            temperature = key.Temperature;
            saturation = key.Saturation;
            vignette = key.Vignette;
            contrast = key.Contrast;
        }

        private static Key Sample(float hour)
        {
            Key previous = Keys[0];
            if (hour < previous.Hour)
            {
                return previous;
            }

            for (int i = 1; i < Keys.Length; i++)
            {
                Key next = Keys[i];
                if (hour <= next.Hour)
                {
                    return Key.Lerp(previous, next, Mathf.InverseLerp(previous.Hour, next.Hour, hour));
                }

                previous = next;
            }

            return previous;
        }
    }
}
