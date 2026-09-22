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

        private string _currentBgm = string.Empty;
        private string _currentAmbient = string.Empty;
        private float _bgmVolume = DefaultBgmVolume;
        private float _seVolume = DefaultSeVolume;
        private float _ambientVolume = DefaultAmbientVolume;
        private float _fade = 1f;
        private float _duck = 1f;
        private float _duckUntil;

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

        /// <summary>時報チャイム。鳴っている間 BGM を下げる（校歌の上にそのまま重ねると濁る, #38）。</summary>
        public void PlayChime()
        {
            AudioClip clip = Find("se_chime");
            if (clip == null)
            {
                return;
            }

            _se.PlayOneShot(clip, _seVolume * ChimeScale);
            Duck(clip.length * ChimeDuckFraction);
        }

        /// <summary>seconds の間 BGM を下げる。長い方を採る（重ねて呼んでも短くならない）。</summary>
        public void Duck(float seconds)
        {
            _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
        }

        /// <summary>チャイムの音量（SE 音量に掛ける）。</summary>
        public const float ChimeScale = 0.4f;

        /// <summary>チャイムの長さのうち BGM を下げる割合。残響の尾は BGM が戻る間に消える。</summary>
        public const float ChimeDuckFraction = 0.8f;

        /// <summary>床の種類に合わせた足音。4 種類からランダム。</summary>
        public void PlayFootstep(string surface, bool running)
        {
            string id = "step_" + surface + "_" + Random.Range(1, 5);
            PlaySe(id, running ? 0.75f : 0.5f);
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
            _duck = Mathf.MoveTowards(_duck, Time.unscaledTime < _duckUntil ? 0.35f : 1f, deltaTime * 2f);

            _bgmA.volume = Mathf.MoveTowards(_bgmA.volume, goalA * _bgmVolume * _duck, speed * _bgmVolume);
            _bgmB.volume = Mathf.MoveTowards(_bgmB.volume, goalB * _bgmVolume * _duck, speed * _bgmVolume);
            StopIfSilent(_bgmA);
            StopIfSilent(_bgmB);
            _ambient.volume = _ambientVolume;
        }

        private static void StopIfSilent(AudioSource source)
        {
            if (source.isPlaying && source.volume <= 0.0001f)
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
