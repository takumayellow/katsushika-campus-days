using UnityEngine;

namespace KCD
{
    /// <summary>
    /// ゲーム内 1 日 = 実時間 12 分。08:30 から始まり、太陽の角度・色、空（KCD/Sky）・霧・環境光を動かす。
    /// 色は <see cref="SkyPalette"/> が時刻ごとに決める（日没どきは葛飾キャンパスの西日を想定して橙と桃色）。
    /// 太陽は東（+x）から昇り、南（-z）の空を通って西（-x）へ沈む。太陽が沈んだあとは同じライトを月明かりにする。
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class DayNightCycle : MonoBehaviour
    {
        /// <summary>既定の日の出・日の入りの時刻と緯度。シーン生成（SkyFactory）が朝の太陽の向きを出すのにも使う。</summary>
        public const float DefaultSunriseHour = 5.5f;
        public const float DefaultSunsetHour = 18.5f;
        public const float DefaultLatitudeDegrees = 35.7f;

        [SerializeField] private float _realSecondsPerGameDay = 720f;
        // 一日の始まりは DayRestart が正。ここに 8.5f と書き直すと「もう一日歩く」の朝とずれる。
        [SerializeField] private float _startHour = DayRestart.DayStartHour;
        [SerializeField] private float _sunriseHour = DefaultSunriseHour;
        [SerializeField] private float _sunsetHour = DefaultSunsetHour;
        // 葛飾（東京）の緯度。南中の高さが 90° − 緯度になる（真上を通ると昼の影が足もとに潰れる）。
        [SerializeField] private float _latitudeDegrees = DefaultLatitudeDegrees;
        [SerializeField] private Color _indoorAmbient = new Color(0.56f, 0.57f, 0.60f);
        private bool _outdoorFog = true;
        private bool _wasIndoor;

        /// <summary>太陽は地平線から 5° 昇るまでに明るくなり、月明かりは太陽が 10° 沈むまでに灯る。</summary>
        public const float SunFadeDegrees = 5f;
        public const float MoonFadeDegrees = 10f;

        /// <summary>月の方へ向かう単位ベクトル。月は時刻で動かさず、南東の空の 38° に置く。</summary>
        public static readonly Vector3 MoonDirection = new Vector3(0.557f, 0.616f, -0.557f).normalized;

        private Light _sun;
        private Material _sharedSky;
        private Material _skyMaterial;

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

            // シーンの空のマテリアルを複製してから書き換える。共有のアセットを直接書くと、
            // エディタで再生しただけで .mat が書き換わって差分が出る。
            _sharedSky = RenderSettings.skybox;
            if (SkyMaterial.IsSky(_sharedSky))
            {
                _skyMaterial = new Material(_sharedSky) { name = _sharedSky.name + " (Runtime)" };
                RenderSettings.skybox = _skyMaterial;
            }
        }

        private void OnDestroy()
        {
            if (_skyMaterial == null)
            {
                return;
            }

            if (RenderSettings.skybox == _skyMaterial)
            {
                RenderSettings.skybox = _sharedSky;
            }

            Destroy(_skyMaterial);
            _skyMaterial = null;
        }

        /// <summary>
        /// キャンパスに入ったときの時計の始まり。別シーンから戻ってきた（＝入場済みの）ときは
        /// GameManager が覚えている時刻を引き継ぎ、そうでなければ朝から始める。
        ///
        /// 「つづきから」(#38) は <c>TitleMenu</c> が入場済みにしてからキャンパスへ入るのでセーブの時刻を継ぎ、
        /// 「はじめから」(#53) は <c>GameManager.BeginNewGame</c> が入場済みを落とすので必ず朝になる。
        /// </summary>
        public static float StartingHours(bool hasEnteredCampus, float rememberedHours, float startHour)
        {
            return hasEnteredCampus ? rememberedHours : startHour;
        }

        private void Start()
        {
            GameManager manager = GameManager.Instance;
            Hours = StartingHours(manager.HasEnteredCampus, manager.GameTimeHours, _startHour);
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

        /// <summary>
        /// 太陽の方へ向かう単位ベクトル（ライトの forward の逆）。日の出で真東の地平線、
        /// 日の出と日の入りのまん中で真南の 90° − 緯度、日の入りで真西の地平線。夜は同じ円の地平線より下を回る。
        /// </summary>
        public static Vector3 SunDirection(float hours, float sunriseHour, float sunsetHour, float latitudeDegrees)
        {
            float span = Mathf.Max(0.01f, sunsetHour - sunriseHour);
            float angle = (Mathf.Repeat(hours, 24f) - sunriseHour) / span * Mathf.PI;
            float latitude = latitudeDegrees * Mathf.Deg2Rad;
            // 日周の円は東西を通り、真上から南へ緯度ぶん傾いている。
            Vector3 noon = new Vector3(0f, Mathf.Cos(latitude), -Mathf.Sin(latitude));
            return (Vector3.right * Mathf.Cos(angle) + noon * Mathf.Sin(angle)).normalized;
        }

        /// <summary>既定の日の出・日の入り・緯度での太陽の向き。</summary>
        public static Vector3 DefaultSunDirection(float hours)
        {
            return SunDirection(hours, DefaultSunriseHour, DefaultSunsetHour, DefaultLatitudeDegrees);
        }

        /// <summary>太陽の明るさ。空の配色の値に、地平線ぎわで 0 へ落ちる減衰を掛ける。</summary>
        public static float SunLightIntensity(SkyState sky, Vector3 sunDirection)
        {
            return sky.SunIntensity * Mathf.Clamp01(HeightDegrees(sunDirection) / SunFadeDegrees);
        }

        /// <summary>向きの地平線からの高さ（度、地平線より下なら負）。</summary>
        public static float HeightDegrees(Vector3 direction)
        {
            return Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void Apply()
        {
            SkyState sky = SkyPalette.Evaluate(Hours);
            Vector3 sunDirection = SunDirection(Hours, _sunriseHour, _sunsetHour, _latitudeDegrees);
            SkyMaterial.Apply(_skyMaterial, sky, sunDirection, MoonDirection);

            if (ApplyIndoor())
            {
                return;
            }

            float height = HeightDegrees(sunDirection);
            float sunIntensity = SunLightIntensity(sky, sunDirection);
            float moonIntensity = sky.MoonIntensity * Mathf.Clamp01(-height / MoonFadeDegrees);
            if (sunIntensity >= moonIntensity)
            {
                transform.rotation = Quaternion.LookRotation(-sunDirection, Vector3.up);
                _sun.color = sky.SunColor;
                _sun.intensity = sunIntensity;
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(-MoonDirection, Vector3.up);
                _sun.color = SkyPalette.MoonColor;
                _sun.intensity = moonIntensity;
            }

            _sun.enabled = _sun.intensity > 0.01f;

            // 霧の色・環境光を毎回空に合わせる。屋内で上書きした横と下の環境光もここで戻る。
            SkyMaterial.ApplyEnvironment(sky);
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
    }
}
