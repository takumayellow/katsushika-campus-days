using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// 場面に応じた自動選曲。タイトルは校歌 bgm_school_song、キャンパスは時刻で昼 / 夕方 / 夜、
    /// 建物の中は bgm_indoor と建物ごとの環境音。9 時・12 時・17 時にチャイム（鳴る間は BGM を下げる）。
    /// クエストの開始 / 進行 / 達成の音もここで拾う。
    /// </summary>
    public sealed partial class AudioManager
    {
        /// <summary>この時刻から夕方の曲。</summary>
        private const float EveningHour = 16.5f;

        /// <summary>この時刻から夜の曲。</summary>
        private const float NightHour = 19.5f;

        private static readonly int[] ChimeHours = { 9, 12, 17 };

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
        }

        /// <summary>この時刻の切り替わりでチャイムを鳴らすか（テストから呼ぶ純関数）。</summary>
        public static bool IsChimeHour(int hour)
        {
            return System.Array.IndexOf(ChimeHours, hour) >= 0;
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
