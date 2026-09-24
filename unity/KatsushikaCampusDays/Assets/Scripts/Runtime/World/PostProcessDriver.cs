using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD
{
    /// <summary>
    /// 時刻に合わせて色温度・彩度・ビネットを揺らす。屋内では中立に戻す。
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

            public Key(float hour, float temperature, float saturation, float vignette)
            {
                Hour = hour;
                Temperature = temperature;
                Saturation = saturation;
                Vignette = vignette;
            }
        }

        private static readonly Key[] Keys =
        {
            new Key(5.0f, -12f, -12f, 0.34f),
            new Key(6.5f, 8f, 4f, 0.26f),
            new Key(9.0f, 0f, 10f, 0.22f),
            new Key(15.0f, 0f, 10f, 0.22f),
            new Key(17.0f, 12f, 12f, 0.24f),
            new Key(18.3f, 25f, 18f, 0.30f),
            new Key(19.5f, -12f, -12f, 0.34f),
            new Key(24.0f, -12f, -12f, 0.34f)
        };

        [SerializeField] private float _lerpSpeed = 1.5f;

        private Volume _volume;
        private ColorAdjustments _color;
        private Vignette _vignette;
        private WhiteBalance _whiteBalance;
        private DayNightCycle _cycle;
        private float _baseSaturation;
        private float _baseVignette;
        private float _temperature;
        private float _saturation;
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
            _baseVignette = _vignette.intensity.value;
            _whiteBalance.temperature.overrideState = true;
            _color.saturation.overrideState = true;
            _vignette.intensity.overrideState = true;
        }

        private void Start()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            Evaluate(out _temperature, out _saturation, out _vignetteValue);
            Apply();
        }

        private void Update()
        {
            Evaluate(out float temperature, out float saturation, out float vignette);
            float t = 1f - Mathf.Exp(-_lerpSpeed * Time.deltaTime);
            _temperature = Mathf.Lerp(_temperature, temperature, t);
            _saturation = Mathf.Lerp(_saturation, saturation, t);
            _vignetteValue = Mathf.Lerp(_vignetteValue, vignette, t);
            Apply();
        }

        private void Apply()
        {
            _whiteBalance.temperature.value = _temperature;
            _color.saturation.value = _baseSaturation + _saturation;
            _vignette.intensity.value = _vignetteValue;
        }

        private void Evaluate(out float temperature, out float saturation, out float vignette)
        {
            InteriorLoader loader = InteriorLoader.Instance;
            bool inside = loader != null && loader.IsInside;
            float hours = _cycle != null ? _cycle.Hours : 12f;
            Grade(hours, inside, _baseVignette, out temperature, out saturation, out vignette);
        }

        /// <summary>
        /// その時刻の色温度・彩度（プロファイルの彩度への足し分）・ビネット。キーの間は線形につなぎ、
        /// 時刻は 24 時で折り返す。屋内は時刻によらず中立（色温度 0、彩度 +4、ビネットはプロファイルの値）。
        /// </summary>
        public static void Grade(float hours, bool inside, float baseVignette,
            out float temperature, out float saturation, out float vignette)
        {
            if (inside)
            {
                temperature = 0f;
                saturation = 4f;
                vignette = baseVignette;
                return;
            }

            float hour = Mathf.Repeat(hours, 24f);
            Key previous = Keys[0];
            if (hour < previous.Hour)
            {
                temperature = previous.Temperature;
                saturation = previous.Saturation;
                vignette = previous.Vignette;
                return;
            }

            for (int i = 1; i < Keys.Length; i++)
            {
                Key next = Keys[i];
                if (hour <= next.Hour)
                {
                    float t = Mathf.InverseLerp(previous.Hour, next.Hour, hour);
                    temperature = Mathf.Lerp(previous.Temperature, next.Temperature, t);
                    saturation = Mathf.Lerp(previous.Saturation, next.Saturation, t);
                    vignette = Mathf.Lerp(previous.Vignette, next.Vignette, t);
                    return;
                }

                previous = next;
            }

            temperature = previous.Temperature;
            saturation = previous.Saturation;
            vignette = previous.Vignette;
        }
    }
}
