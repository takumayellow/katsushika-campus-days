using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 音のまとめ役。BGM（2 本でクロスフェード）・環境音・効果音・UI 音を 1 か所で鳴らす。
    /// クリップは SceneBuilder（AudioFactory）が Assets/Audio 以下から差し込む。
    /// シーンをまたいで残り、2 つ目は自分を消す。音量は PlayerPrefs に覚える。
    /// 場面に応じた BGM・環境音の自動切り替えは AudioManager.Scene.cs。
    /// </summary>
    public sealed partial class AudioManager : MonoBehaviour
    {
        /// <summary>tools/audio/manifest.json の suggested_volume。</summary>
        public const float DefaultBgmVolume = 0.55f;
        public const float DefaultSeVolume = 0.8f;
        public const float DefaultAmbientVolume = 0.35f;

        private const string BgmPrefKey = "KCD.Volume.Bgm";
        private const string SePrefKey = "KCD.Volume.Se";
        private const string AmbientPrefKey = "KCD.Volume.Ambient";

        public static AudioManager Instance { get; private set; }

        [SerializeField] private AudioClip[] _clips = System.Array.Empty<AudioClip>();
        [SerializeField] private float _bgmFadeSeconds = 1.6f;

        private readonly Dictionary<string, AudioClip> _byName = new Dictionary<string, AudioClip>();
        private AudioSource _bgmA;
        private AudioSource _bgmB;
        private AudioSource _ambient;
        private AudioSource _se;
        private AudioSource _ui;
        private AudioSource _voice;
        private AudioSource _chime;

        private string _currentBgm = string.Empty;
        private string _currentAmbient = string.Empty;
        private float _bgmVolume = DefaultBgmVolume;
        private float _seVolume = DefaultSeVolume;
        private float _ambientVolume = DefaultAmbientVolume;
        private float _fade = 1f;
        private float _levelA;
        private float _levelB;
        private float _duck = 1f;
        private float _duckUntil;
        private float _duckLevel = JingleDuckLevel;

        /// <summary>いま鳴っている BGM の id。</summary>
        public string CurrentBgm => _currentBgm;

        /// <summary>いま鳴っている環境音の id。</summary>
        public string CurrentAmbient => _currentAmbient;

        public float BgmVolume
        {
            get => _bgmVolume;
            set => SetVolume(ref _bgmVolume, value, BgmPrefKey);
        }

        public float SeVolume
        {
            get => _seVolume;
            set => SetVolume(ref _seVolume, value, SePrefKey);
        }

        public float AmbientVolume
        {
            get => _ambientVolume;
            set => SetVolume(ref _ambientVolume, value, AmbientPrefKey);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);

            foreach (AudioClip clip in _clips)
            {
                if (clip != null)
                {
                    _byName[clip.name] = clip;
                }
            }

            _bgmVolume = PlayerPrefs.GetFloat(BgmPrefKey, DefaultBgmVolume);
            _seVolume = PlayerPrefs.GetFloat(SePrefKey, DefaultSeVolume);
            _ambientVolume = PlayerPrefs.GetFloat(AmbientPrefKey, DefaultAmbientVolume);

            _bgmA = MakeSource("BGM_A", true);
            _bgmB = MakeSource("BGM_B", true);
            _ambient = MakeSource("Ambient", true);
            _se = MakeSource("SE", false);
            _ui = MakeSource("UI", false);
            _voice = MakeSource("Voice", false);
            _chime = MakeSource("Chime", false);
            _ui.ignoreListenerPause = true;
            SubscribeScene();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                UnsubscribeScene();
                Instance = null;
            }
        }

        private void Update()
        {
            UpdateScene();
            UpdateBgmFade(Time.unscaledDeltaTime);
        }

        /// <summary>id のクリップ。無ければ null。</summary>
        public AudioClip Find(string id)
        {
            return !string.IsNullOrEmpty(id) && _byName.TryGetValue(id, out AudioClip clip) ? clip : null;
        }

        /// <summary>BGM を切り替える。同じ曲なら何もしない。</summary>
        public void PlayBgm(string id)
        {
            if (id == _currentBgm)
            {
                return;
            }

            AudioClip clip = Find(id);
            _currentBgm = id ?? string.Empty;
            if (clip == null)
            {
                _fade = 0f;
                return;
            }

            // 鳴っていない方に次の曲を入れ、フェードで入れ替える。
            bool useB = NextBgmIsB(_fade);
            AudioSource next = useB ? _bgmB : _bgmA;
            next.clip = clip;
            next.loop = true;
            next.volume = 0f;
            if (useB)
            {
                _levelB = 0f;
            }
            else
            {
                _levelA = 0f;
            }

            next.Play();
            _fade = useB ? 1f : 0f;
        }

        /// <summary>
        /// 次の曲を B に入れるか。_fade は 0 = A が鳴る, 1 = B が鳴る なので, いま鳴っていない側を返す。
        /// </summary>
        public static bool NextBgmIsB(float fade)
        {
            return fade < 0.5f;
        }

        /// <summary>BGM を止める（フェードアウト）。</summary>
        public void StopBgm()
        {
            _currentBgm = string.Empty;
            _fade = 0f;
        }

        /// <summary>環境音を切り替える。空文字で止める。</summary>
        public void PlayAmbient(string id)
        {
            if (id == _currentAmbient)
            {
                return;
            }

            _currentAmbient = id ?? string.Empty;
            AudioClip clip = Find(id);
            if (clip == null)
            {
                _ambient.Stop();
                return;
            }

            _ambient.clip = clip;
            _ambient.loop = true;
            _ambient.volume = _ambientVolume;
            _ambient.Play();
        }

        /// <summary>効果音を 1 回鳴らす。</summary>
        public void PlaySe(string id, float scale = 1f)
        {
            AudioClip clip = Find(id);
            if (clip != null)
            {
                _se.PlayOneShot(clip, _seVolume * scale);
            }
        }

        /// <summary>UI 音。ポーズ中（timeScale 0）でも鳴る。</summary>
        public void PlayUi(string id, float scale = 1f)
        {
            AudioClip clip = Find(id);
            if (clip != null)
            {
                _ui.PlayOneShot(clip, _seVolume * scale);
            }
        }

        /// <summary>会話の文字送りの音。前の音が鳴り終わっていなくても重ねる。</summary>
        public void PlayBlip(string id)
        {
            AudioClip clip = Find(id);
            if (clip != null)
            {
                _voice.pitch = Random.Range(0.96f, 1.04f);
                _voice.PlayOneShot(clip, _seVolume * 0.7f);
            }
        }

        /// <summary>ジングル。鳴っている間 BGM を下げる。</summary>
        public void PlayJingle(string id)
        {
            AudioClip clip = Find(id);
            if (clip == null)
            {
                return;
            }

            _se.PlayOneShot(clip, _bgmVolume);
            Duck(clip.length);
        }

        /// <summary>
        /// 時報チャイム。鳴っている間は BGM を止める（校歌の上に鐘を重ねると調が合わず濁る, #38）。
        /// 最初の鐘はクリップの頭（0 秒）から鳴るので、専用の AudioSource で ChimeLeadSeconds 遅らせて鳴らし、
        /// その間に BGM を無音まで下げ切る。残響の尾が消えるころに BGM がフェードで戻る。
        /// </summary>
        public void PlayChime()
        {
            AudioClip clip = Find("se_chime");
            if (clip == null)
            {
                return;
            }

            // PlayDelayed はオーディオの時計で数えるので timeScale やフレーム落ちに左右されない。
            _chime.clip = clip;
            _chime.volume = _seVolume * ChimeScale;
            _chime.PlayDelayed(ChimeLeadSeconds);
            Duck(ChimeLeadSeconds + clip.length * ChimeDuckFraction, ChimeDuckLevel);
        }

        /// <summary>
        /// seconds の間 BGM を level 倍に下げる。時間は長い方、レベルは低い方を採る（重ねて呼んでも弱くならない）。
        /// </summary>
        public void Duck(float seconds, float level = JingleDuckLevel)
        {
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            bool active = Time.unscaledTime < _duckUntil;
            _duckLevel = active ? Mathf.Min(_duckLevel, level) : Mathf.Clamp01(level);
            _duckUntil = Mathf.Max(_duckUntil, until);
        }

        /// <summary>ジングルの間の BGM 音量の倍率。</summary>
        public const float JingleDuckLevel = 0.35f;

        /// <summary>チャイムの間の BGM 音量の倍率。0 = 止める。</summary>
        public const float ChimeDuckLevel = 0f;

        /// <summary>チャイムの音量（SE 音量に掛ける）。校内放送のスピーカーから遠く聞こえる程度。</summary>
        public const float ChimeScale = 0.45f;

        /// <summary>チャイムの長さのうち BGM を止める割合。最後の 1 割ほどは残響の尾で、BGM が戻る間に消える。</summary>
        public const float ChimeDuckFraction = 0.9f;

        /// <summary>チャイムを遅らせる秒数。この間に BGM を DuckAttackSeconds で無音まで下げ切ってから最初の鐘を鳴らす。</summary>
        public const float ChimeLeadSeconds = 0.4f;

        /// <summary>ダックで BGM を下げ切る秒数（倍率 1 → 0）。クロスフェード（_bgmFadeSeconds）とは別に速くする。</summary>
        public const float DuckAttackSeconds = 0.25f;

        /// <summary>ダックの後で BGM を戻す秒数（倍率 0 → 1）。これまでどおりクロスフェードと同じ 1.6 秒でなめらかに戻す。</summary>
        public const float DuckReleaseSeconds = 1.6f;

        /// <summary>
        /// 床の種類に合わせた足音。4 種類からランダム。
        /// その床の音が読み込まれていなければ（シーンを作り直す前の carpet など）タイルの音で代える。
        /// </summary>
        public void PlayFootstep(string surface, bool running)
        {
            int variant = Random.Range(1, 5);
            string id = FootstepClipId(surface, variant);
            if (Find(id) == null)
            {
                id = FootstepClipId("tile", variant);
            }

            PlaySe(id, running ? 0.75f : 0.5f);
        }

        /// <summary>足音のクリップ名（step_床_1〜4）。tools/audio/sfx.py の build_steps が書き出す名前と同じ。</summary>
        public static string FootstepClipId(string surface, int variant)
        {
            return "step_" + surface + "_" + variant;
        }

        private AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private void UpdateBgmFade(float deltaTime)
        {
            float target = string.IsNullOrEmpty(_currentBgm) ? 0f : 1f;
            float speed = _bgmFadeSeconds > 0.01f ? deltaTime / _bgmFadeSeconds : 1f;

            // _fade: 0 = A が鳴る, 1 = B が鳴る。曲が無いときは両方 0 へ。
            float goalA = target * (1f - Mathf.Round(_fade));
            float goalB = target * Mathf.Round(_fade);

            // クロスフェードはダック前の音量 _levelA / _levelB を _bgmFadeSeconds でゆっくり動かし、ダックは最後に掛ける。
            // 音量そのものを 1.6 秒かけて動かすとダックもその速さに縛られ、最初の鐘の下で BGM が鳴り続けていた（#38）。
            _levelA = Mathf.MoveTowards(_levelA, goalA, speed);
            _levelB = Mathf.MoveTowards(_levelB, goalB, speed);
            _duck = StepDuck(_duck, Time.unscaledTime < _duckUntil ? _duckLevel : 1f, deltaTime);

            _bgmA.volume = _levelA * _bgmVolume * _duck;
            _bgmB.volume = _levelB * _bgmVolume * _duck;
            StopIfSilent(_bgmA, _levelA, goalA);
            StopIfSilent(_bgmB, _levelB, goalB);
            _ambient.volume = _ambientVolume;
        }

        /// <summary>
        /// ダックの倍率を 1 フレーム進める。下げるときは DuckAttackSeconds で速く、戻すときは DuckReleaseSeconds でなめらかに。
        /// </summary>
        public static float StepDuck(float duck, float target, float deltaTime)
        {
            float seconds = target < duck ? DuckAttackSeconds : DuckReleaseSeconds;
            return Mathf.MoveTowards(duck, target, deltaTime / seconds);
        }

        /// <summary>
        /// フェードアウトし切った側を止める。level はダック前の音量、goal はダック前の目標なので、
        /// チャイムで 0 まで下げている間も現在の曲は（無音のまま）流れ続け、戻るときに頭から始まらない。
        /// </summary>
        public static bool ShouldStopSilent(bool isPlaying, float level, float goal)
        {
            return isPlaying && goal <= 0f && level <= 0.0001f;
        }

        private static void StopIfSilent(AudioSource source, float level, float goal)
        {
            if (ShouldStopSilent(source.isPlaying, level, goal))
            {
                source.Stop();
            }
        }

        private static void SetVolume(ref float field, float value, string key)
        {
            field = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(key, field);
            PlayerPrefs.Save();
        }
    }
}
