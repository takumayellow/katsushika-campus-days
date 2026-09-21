using UnityEngine;

namespace KCD
{
    /// <summary>
    /// ゲーム内 1 日 = 実時間 12 分。08:30 から始まり、太陽の角度・色・環境光を動かす。
    /// 日没どきの色は葛飾キャンパスの西日を想定して橙寄りにしてある。
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [SerializeField] private float _realSecondsPerGameDay = 720f;
        [SerializeField] private float _startHour = 8.5f;
        [SerializeField] private float _sunriseHour = 5.5f;
        [SerializeField] private float _sunsetHour = 18.5f;
        [SerializeField] private float _northOffsetDegrees = 20f;

        [SerializeField] private Gradient _sunColor;
        [SerializeField] private AnimationCurve _sunIntensity;
        [SerializeField] private Gradient _ambientColor;
        [SerializeField] private Color _indoorAmbient = new Color(0.56f, 0.57f, 0.60f);
        private bool _outdoorFog = true;
        private bool _wasIndoor;

        private Light _sun;

        /// <summary>現在のゲーム内時刻（0-24 の実数）。</summary>
        public float Hours { get; private set; }

        /// <summary>"HH:MM" 形式の時刻。</summary>
        public string TimeText
        {
            get
            {
                int hour = Mathf.FloorToInt(Hours) % 24;
                int minute = Mathf.FloorToInt((Hours - Mathf.Floor(Hours)) * 60f);
                return hour.ToString("00") + ":" + minute.ToString("00");
            }
        }

        private void Awake()
        {
            _sun = GetComponent<Light>();
            _sun.type = LightType.Directional;
            Hours = _startHour;

            EnsureDefaults();
        }

        private void Start()
        {
            // 別シーンから戻ってきたときは GameManager が覚えている時刻を引き継ぐ。
            GameManager manager = GameManager.Instance;
            if (manager.HasEnteredCampus)
            {
                Hours = manager.GameTimeHours;
            }

            manager.HasEnteredCampus = true;
            Apply();
        }

        /// <summary>時刻を直接動かす。セーブの復元やスモークテストから使う。</summary>
        public void SetHours(float hours)
        {
            Hours = Mathf.Repeat(hours, 24f);
            GameManager.Instance.GameTimeHours = Hours;
            Apply();
        }

        private void Update()
        {
            if (_realSecondsPerGameDay <= 0f)
            {
                return;
            }

            Hours += Time.deltaTime * (24f / _realSecondsPerGameDay);
            if (Hours >= 24f)
            {
                Hours -= 24f;
            }

            GameManager.Instance.GameTimeHours = Hours;
            Apply();
        }

        private void Apply()
        {
            if (ApplyIndoor())
            {
                return;
            }

            // 日の出で地平線、南中で真上、日の入りで再び地平線になるよう写像する。
            float dayProgress = Mathf.InverseLerp(_sunriseHour, _sunsetHour, Hours);
            float elevation = Mathf.Lerp(-12f, 192f, dayProgress);

            transform.rotation = Quaternion.Euler(elevation, _northOffsetDegrees, 0f);

            float t = Hours / 24f;
            _sun.color = _sunColor.Evaluate(t);
            _sun.intensity = Mathf.Max(0f, _sunIntensity.Evaluate(t));

            RenderSettings.ambientLight = _ambientColor.Evaluate(t);
            _sun.enabled = _sun.intensity > 0.01f;
        }

        /// <summary>
        /// 建物の中では太陽を消して一定の環境光にする（天井が影を落として真っ暗になるのを防ぐ）。
        /// 霧も止め、外へ出たら元へ戻す。屋内なら true。
        /// </summary>
        private bool ApplyIndoor()
        {
            InteriorLoader loader = InteriorLoader.Instance;
            bool indoor = loader != null && loader.IsInside;
            if (indoor && !_wasIndoor)
            {
                _outdoorFog = RenderSettings.fog;
            }

            if (!indoor && _wasIndoor)
            {
                RenderSettings.fog = _outdoorFog;
            }

            _wasIndoor = indoor;
            if (!indoor)
            {
                return false;
            }

            _sun.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.ambientLight = _indoorAmbient;
            RenderSettings.ambientEquatorColor = _indoorAmbient * 0.92f;
            RenderSettings.ambientGroundColor = _indoorAmbient * 0.7f;
            return true;
        }

        private void EnsureDefaults()
        {
            if (_sunColor == null || _sunColor.colorKeys.Length == 0)
            {
                _sunColor = BuildGradient(
                    new Color(0.24f, 0.28f, 0.45f),
                    new Color(1.00f, 0.76f, 0.55f),
                    new Color(1.00f, 0.97f, 0.92f),
                    new Color(1.00f, 0.62f, 0.38f),
                    new Color(0.22f, 0.26f, 0.44f));
            }

            if (_ambientColor == null || _ambientColor.colorKeys.Length == 0)
            {
                _ambientColor = BuildGradient(
                    new Color(0.10f, 0.12f, 0.20f),
                    new Color(0.42f, 0.40f, 0.44f),
                    new Color(0.62f, 0.64f, 0.70f),
                    new Color(0.44f, 0.34f, 0.34f),
                    new Color(0.10f, 0.12f, 0.20f));
            }

            if (_sunIntensity == null || _sunIntensity.length == 0)
            {
                _sunIntensity = new AnimationCurve(
                    new Keyframe(0.00f, 0.02f),
                    new Keyframe(0.24f, 0.25f),
                    new Keyframe(0.50f, 1.35f),
                    new Keyframe(0.78f, 0.35f),
                    new Keyframe(1.00f, 0.02f));
            }
        }

        private static Gradient BuildGradient(params Color[] colors)
        {
            var gradient = new Gradient();
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(colors[i], i / (float)(colors.Length - 1));
            }

            gradient.SetKeys(colorKeys, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });

            return gradient;
        }
    }
}
