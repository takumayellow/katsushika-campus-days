using System.Collections.Generic;
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

        /// <summary>獲得した称号 (#65)。GameManager と寿命を共にし、「はじめから」で空にする。セーブには載せない。</summary>
        public AchievementBook Achievements { get; } = new AchievementBook();

        /// <summary>クエストが動いたので称号を数え直す。</summary>
        private bool _achievementsDirty = true;

        /// <summary>最後に称号を数えたときの DayStats.Version。</summary>
        private int _achievementStatsVersion = -1;

        /// <summary>購読している QuestSystem。付け替えのときに外す。</summary>
        private QuestSystem _subscribedQuests;

        /// <summary>ゲーム内時刻（時間単位の実数、0-24）。DayNightCycle が毎フレーム更新する。</summary>
        public float GameTimeHours { get; set; } = DayRestart.DayStartHour;

        /// <summary>タイトルからキャンパスへ入った回数。初回だけオリエンのクエストを自動開始する。</summary>
        public bool HasEnteredCampus { get; set; }

        /// <summary>何日目か。「もう一日歩く」を選ぶたびに 1 つ進む（#16）。セーブにも載せる (#61)。</summary>
        public int DayNumber { get; set; } = DayRestart.FirstDay;

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
                Quests = CreateQuests();
                Quests.LoadFromResources();
            }

            SubscribeQuests();
        }

        /// <summary>クエストの変化で称号を数え直す。同じ QuestSystem に二重には付けない。</summary>
        private void SubscribeQuests()
        {
            if (_subscribedQuests == Quests)
            {
                return;
            }

            if (_subscribedQuests != null)
            {
                _subscribedQuests.Changed -= OnQuestsChanged;
            }

            _subscribedQuests = Quests;
            if (_subscribedQuests != null)
            {
                _subscribedQuests.Changed += OnQuestsChanged;
            }

            _achievementsDirty = true;
        }

        private void OnQuestsChanged()
        {
            _achievementsDirty = true;
        }

        /// <summary>クエスト進行を作る。達成したら自動セーブを頼む (#61)。</summary>
        private static QuestSystem CreateQuests()
        {
            var quests = new QuestSystem();
            AutoSave.Watch(quests);
            return quests;
        }

        /// <summary>
        /// タイトルの「はじめから」。前の周回の残りを初期値に戻す (#53)。
        ///
        /// GameManager は DontDestroyOnLoad でシーンをまたいで生き続けるので、ここで戻さないと
        /// 裏エンド（<see cref="DormEnding"/>）やリザルトの「タイトルへ」でタイトルに帰ったあとの新規開始が、
        /// サボった時刻・2 日目・達成済みのクエストのまま始まる。
        ///
        /// セーブファイルは消さない。消すと「つづきから」(F9) の戻り先を潰すし、
        /// 戻すべきものはすべてメモリ上の値なのでファイルとは独立している（次のセーブで上書きされる）。
        /// </summary>
        public void BeginNewGame()
        {
            // 時計は DayNightCycle が持つが、キャンパスに入るまで（HUD の時刻表示など）はここの値が使われる。
            GameTimeHours = DayRestart.DayStartHour;

            // 入場済みを落とすのが肝。DayNightCycle.Start はこれが true のときだけ
            // GameTimeHours を引き継ぐので、false に戻すと自分の _startHour（= DayRestart.DayStartHour）
            // から始め直す。時計の初期値をここでもう一つ持たないための書き方。
            HasEnteredCampus = false;

            DayNumber = DayRestart.FirstDay;

            // 探索率のもと。static なのでアプリを起動している間ずっと残る（シーンでは消えない）。
            DayStats.Reset();

            // 「つづきから」を選びかけて読んだセーブが残っていたら、キャンパスで当てないよう捨てる。
            SaveSystem.DiscardPending();
            AutoSave.Cancel();

            // クエストも GameManager と寿命を共にするので読み直す。タイトルで受注音を鳴らさない口を使う。
            if (Quests == null)
            {
                Quests = CreateQuests();
            }

            Quests.ResetForNewGame();
            SubscribeQuests();

            // 称号も前の周回のものを持ち越さない。ResetForNewGame は黙って作り直す（Changed を出さない）ので、ここで印を付ける。
            Achievements.Reset();
            _achievementsDirty = true;

            // 選択キャラクター（SelectedCharacterId / PlayerPrefs）はここでは触らない。
            // 「はじめから」はこのあとキャラ選択で決まるし、次回の既定値として覚えておいてよい。
        }

        private void Update()
        {
            Quests?.Tick(Time.deltaTime);

            // 頼まれていた自動セーブを、封鎖が外れた最初のフレームで書く (#61)。
            if (AutoSave.Pending)
            {
                AutoSave.Tick(HasEnteredCampus && SceneManager.GetActiveScene().name == CampusSceneName);
            }

            RefreshAchievements(true);
        }

        /// <summary>
        /// セーブを読んだあとに呼ぶ。読む前に取っていた称号は、トーストを出さずに獲得済みにする
        /// （ロードのたびに「称号を獲得」が並ばないように）。
        /// </summary>
        public void SyncAchievementsQuietly()
        {
            _achievementsDirty = true;
            RefreshAchievements(false);
        }

        /// <summary>
        /// クエストか DayStats が動いたフレームだけ称号を数え直し、announce なら新しく取ったものをトーストで知らせる。
        /// クエストの報酬の収集物もここで記録する（QuestSystem.Restore のあとも追いつくように、達成済みを全部見る）。
        /// </summary>
        private void RefreshAchievements(bool announce)
        {
            if (!_achievementsDirty && _achievementStatsVersion == DayStats.Version)
            {
                return;
            }

            _achievementsDirty = false;
            QuestRewards.GrantCompleted(Quests);
            _achievementStatsVersion = DayStats.Version;

            List<CatalogAchievement> unlocked =
                Achievements.Refresh(CollectibleCatalog.Instance, AchievementRecord.FromDayStats(Quests));
            HUD hud = announce ? HUD.Instance : null;
            if (hud == null)
            {
                return;
            }

            for (int i = 0; i < unlocked.Count; i++)
            {
                hud.ShowToast(L.Format("ui.hud.achievement_unlocked", unlocked[i].DisplayName));
            }
        }

        private void OnDestroy()
        {
            if (_subscribedQuests != null)
            {
                _subscribedQuests.Changed -= OnQuestsChanged;
                _subscribedQuests = null;
            }

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
            // 封鎖を掛けた画面・演出はシーンごと消えるので、残った封鎖をまとめて外す (#40)。
            KCDInput.ClearAllBlocks();

            // 前のキャンパスで頼まれたまま書けなかった自動セーブを、次のキャンパスの最初のフレーム
            // （「つづきから」の位置を当てる前）に書かないよう捨てる (#61)。
            AutoSave.Cancel();
            SceneManager.LoadScene(CampusSceneName);
        }

        /// <summary>
        /// タイトルへ戻る。封鎖と時間停止を解いてから Title を読む。
        /// メモリ上の進行はタイトルでは使わない。「つづきから」はセーブを読み直し、「はじめから」は
        /// <see cref="BeginNewGame"/> で初期値に戻すので、セーブしていない進行はここで失われる。
        /// </summary>
        public void ReturnToTitle()
        {
            KCDInput.ClearAllBlocks();
            Time.timeScale = 1f;

            // 封鎖を外したので、シーンが切り替わるまでのフレームで自動セーブが書かないよう捨てる (#61)。
            AutoSave.Cancel();
            SceneManager.LoadScene(TitleSceneName);
        }

        /// <summary>シーンにインスタンスが無くても起動時に必ず用意する。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // ドメインの再読み込みを切った再生（Enter Play Mode Options）でも、前回の再生の封鎖と自動セーブを持ち越さない。
            KCDInput.ClearAllBlocks();
            AutoSave.Cancel();
            _ = Instance;
            // Web 版は品質レベルの取り違えで描画が崩れたことがあるので、どの設定で起動したかを残す。
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log("[KCD] quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
                      + " pipeline=" + (pipeline != null ? pipeline.name : "builtin"));
        }
    }
}
