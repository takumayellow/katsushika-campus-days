using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// シーンをまたいで残る唯一の状態保持役。
    /// 選択キャラクター、ゲーム内時刻、クエスト進行を持ち、タイトルとキャンパスを橋渡しする。
    /// シーンに置き忘れても RuntimeInitializeOnLoad で自動生成されるので、
    /// Campus シーンを直接再生しても成立する。
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public const string TitleSceneName = "Title";
        public const string CampusSceneName = "Campus";

        /// <summary>DESIGN §2 のプレイアブルキャラクター id。</summary>
        public static readonly string[] PlayableCharacterIds = { "mirai", "botchan", "madonna" };

        /// <summary>表示名。キャラ選択と会話ウィンドウの話者名に使う。</summary>
        public static readonly string[] PlayableCharacterNames = { "新宿 みらい", "坊っちゃん", "マドンナちゃん" };

        /// <summary>会話ウィンドウで使う短い呼び名。</summary>
        public static readonly string[] PlayableShortNames = { "みらい", "坊っちゃん", "マドンナ" };

        /// <summary>選択キャラクターを覚えておく PlayerPrefs のキー。</summary>
        public const string CharacterPrefKey = "KCD.SelectedCharacter";

        private static GameManager _instance;

        [SerializeField] private string _selectedCharacterId = "mirai";

        /// <summary>唯一のインスタンス。無ければ作る。</summary>
        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GameManager");
                    _instance = go.AddComponent<GameManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>プレイヤーが操作するキャラクターの id。</summary>
        public string SelectedCharacterId
        {
            get => _selectedCharacterId;
            set
            {
                _selectedCharacterId = string.IsNullOrEmpty(value) ? "mirai" : value;
                PlayerPrefs.SetString(CharacterPrefKey, _selectedCharacterId);
                PlayerPrefs.Save();
            }
        }

        /// <summary>選択キャラクターの表示名。</summary>
        public string SelectedCharacterName => DisplayNameOf(_selectedCharacterId);

        /// <summary>選択キャラクターの短い呼び名。会話の話者欄に使う。</summary>
        public string SelectedCharacterShortName
        {
            get
            {
                for (int i = 0; i < PlayableCharacterIds.Length; i++)
                {
                    if (PlayableCharacterIds[i] == _selectedCharacterId)
                    {
                        return PlayableShortNames[i];
                    }
                }

                return _selectedCharacterId;
            }
        }

        /// <summary>クエスト進行。GameManager と寿命を共にする。</summary>
        public QuestSystem Quests { get; private set; }

        /// <summary>ゲーム内時刻（時間単位の実数、0-24）。DayNightCycle が毎フレーム更新する。</summary>
        public float GameTimeHours { get; set; } = 8.5f;

        /// <summary>タイトルからキャンパスへ入った回数。初回だけオリエンのクエストを自動開始する。</summary>
        public bool HasEnteredCampus { get; set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _selectedCharacterId = PlayerPrefs.GetString(CharacterPrefKey, _selectedCharacterId);

            if (Quests == null)
            {
                Quests = new QuestSystem();
                Quests.LoadFromResources();
            }
        }

        private void Update()
        {
            Quests?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>id から表示名を引く。未知の id はそのまま返す。</summary>
        public static string DisplayNameOf(string characterId)
        {
            for (int i = 0; i < PlayableCharacterIds.Length; i++)
            {
                if (PlayableCharacterIds[i] == characterId)
                {
                    return PlayableCharacterNames[i];
                }
            }

            return characterId;
        }

        /// <summary>キャンパスへ移動する。</summary>
        public void EnterCampus()
        {
            KCDInput.GameplayBlocked = false;
            SceneManager.LoadScene(CampusSceneName);
        }

        /// <summary>タイトルへ戻る。進行はメモリ上に残るのでそのまま再開できる。</summary>
        public void ReturnToTitle()
        {
            KCDInput.GameplayBlocked = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(TitleSceneName);
        }

        /// <summary>シーンにインスタンスが無くても起動時に必ず用意する。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
            // Web 版は品質レベルの取り違えで描画が崩れたことがあるので、どの設定で起動したかを残す。
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log("[KCD] quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
                      + " pipeline=" + (pipeline != null ? pipeline.name : "builtin"));
        }
    }
}
