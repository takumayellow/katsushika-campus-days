using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// 場面に応じた自動選曲。タイトルは校歌 bgm_school_song、キャンパスは時刻で昼 / 夕方 / 夜、
    /// 建物の中は bgm_indoor と建物ごとの環境音。12 時（昼休み）と 17 時（下校）に時報チャイム。
    /// チャイムの間は BGM を止め、HUD に時刻のトーストを出す（#38）。
    /// クエストの開始 / 進行 / 達成の音もここで拾う。
    /// </summary>
    public sealed partial class AudioManager
    {
        /// <summary>この時刻から夕方の曲。</summary>
        private const float EveningHour = 16.5f;

        /// <summary>この時刻から夜の曲。</summary>
        private const float NightHour = 19.5f;

        /// <summary>時報を鳴らす正時。昼休みと下校だけ。9 時は始業前で、始まって 15 秒で鳴るのがうるさかった（#38）。</summary>
        private static readonly int[] ChimeHours = { 12, 17 };

        private QuestSystem _quests;
        private int _lastChimeHour = -1;
        private int _questEventFrame = -1;
        private FootstepEmitter _footsteps;

        private void SubscribeScene()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void UnsubscribeScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DetachQuests();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _lastChimeHour = -1;
            _footsteps = null;
            AttachQuests();
        }

        private void AttachQuests()
        {
            QuestSystem quests = GameManager.Instance != null ? GameManager.Instance.Quests : null;
            if (quests == _quests)
            {
                return;
            }

            DetachQuests();
            _quests = quests;
            if (_quests == null)
            {
                return;
            }

            _quests.QuestStarted += OnQuestStarted;
            _quests.QuestCompleted += OnQuestCompleted;
            _quests.Changed += OnQuestChanged;
        }

        private void DetachQuests()
        {
            if (_quests == null)
            {
                return;
            }

            _quests.QuestStarted -= OnQuestStarted;
            _quests.QuestCompleted -= OnQuestCompleted;
            _quests.Changed -= OnQuestChanged;
            _quests = null;
        }

        private void OnQuestStarted(QuestData quest)
        {
            _questEventFrame = Time.frameCount;
            PlaySe("quest_start");
        }

        private void OnQuestCompleted(QuestData quest)
        {
            _questEventFrame = Time.frameCount;
            PlayJingle("jingle_quest");
        }

        private void OnQuestChanged()
        {
            // 開始・達成の直後の Changed は進行音を重ねない。
            if (_questEventFrame != Time.frameCount)
            {
                PlaySe("quest_update", 0.8f);
            }
        }

        private void UpdateScene()
        {
            if (_quests == null)
            {
                AttachQuests();
            }

            string scene = SceneManager.GetActiveScene().name;
            if (scene == GameManager.TitleSceneName)
            {
                PlayBgm("bgm_school_song");
                PlayAmbient(string.Empty);
                return;
            }

            if (scene != GameManager.CampusSceneName)
            {
                return;
            }

            EnsureFootsteps();
            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null && loader.IsInside)
            {
                PlayBgm("bgm_indoor");
                PlayAmbient(AmbientFor(loader.CurrentId));
                return;
            }

            float hours = GameManager.Instance.GameTimeHours;
            bool evening = hours >= EveningHour;
            PlayBgm(hours >= NightHour ? "bgm_night" : evening ? "bgm_evening" : "bgm_day");
            PlayAmbient(evening ? "amb_campus_evening" : "amb_campus_day");
            UpdateChime(hours);
        }

        /// <summary>プレイヤーに足音の発音役を付ける（シーンごとに 1 回）。</summary>
        private void EnsureFootsteps()
        {
            if (_footsteps != null)
            {
                return;
            }

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                return;
            }

            _footsteps = player.GetComponent<FootstepEmitter>();
            if (_footsteps == null)
            {
                _footsteps = player.gameObject.AddComponent<FootstepEmitter>();
            }
        }

        private void UpdateChime(float hours)
        {
            int hour = Mathf.FloorToInt(hours);
            if (hour == _lastChimeHour)
            {
                return;
            }

            // 開始直後（前回未設定）は鳴らさず、時刻の切り替わりだけで鳴らす。
            bool first = _lastChimeHour < 0;
            _lastChimeHour = hour;
            if (first || !IsChimeHour(hour))
            {
                return;
            }

            PlayChime();
            string label = ChimeLabelKey(hour);
            if (label != null)
            {
                HUD.Instance?.ShowToast(L.Get(label, ChimeLabelFallback(hour)));
            }
        }

        /// <summary>この時刻の切り替わりでチャイムを鳴らすか（テストから呼ぶ純関数）。</summary>
        public static bool IsChimeHour(int hour)
        {
            return System.Array.IndexOf(ChimeHours, hour) >= 0;
        }

        /// <summary>時報のトーストの翻訳キー。鳴らさない時刻は null。</summary>
        public static string ChimeLabelKey(int hour)
        {
            switch (hour)
            {
                case 12:
                    return "ui.hud.chime_noon";
                case 17:
                    return "ui.hud.chime_evening";
                default:
                    return null;
            }
        }

        private static string ChimeLabelFallback(int hour)
        {
            return hour == 12 ? "12:00 昼休み" : "17:00 下校の時刻";
        }

        /// <summary>建物ごとの環境音。</summary>
        public static string AmbientFor(string buildingId)
        {
            switch (buildingId)
            {
                case "library":
                    return "amb_library";
                case "gym":
                    return "amb_gym";
                case "greenhouse":
                    return "amb_greenhouse";
                case "kyoso":
                    return "amb_cafe";
                case "research2":
                    return "amb_cafeteria";
                default:
                    return "amb_library";
            }
        }
    }
}
