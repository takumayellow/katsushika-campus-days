using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 称号の判定に使う、その時点の記録。ゲームでは <see cref="FromDayStats"/> で DayStats と QuestSystem から作り、
    /// テストでは好きな記録を直接渡す。
    /// </summary>
    public readonly struct AchievementRecord
    {
        public AchievementRecord(IEnumerable<string> collected, IEnumerable<string> photos, int buildingCount,
            IEnumerable<string> talked, IEnumerable<QuestData> quests, Func<string, bool> isCompleted)
        {
            Collected = collected;
            Photos = photos;
            BuildingCount = buildingCount;
            Talked = talked;
            Quests = quests;
            IsCompleted = isCompleted;
        }

        /// <summary>拾った物の id（一覧に無い物も混ざってよい）。</summary>
        public IEnumerable<string> Collected { get; }

        /// <summary>撮った写真の id（一覧に無い物も混ざってよい）。</summary>
        public IEnumerable<string> Photos { get; }

        /// <summary>入った建物の種類数（寮は入らない）。</summary>
        public int BuildingCount { get; }

        /// <summary>話しかけた NPC の id。</summary>
        public IEnumerable<string> Talked { get; }

        /// <summary>全クエスト。quest_count の本編 / サブの区別に使う。</summary>
        public IEnumerable<QuestData> Quests { get; }

        /// <summary>クエストを達成済みか。</summary>
        public Func<string, bool> IsCompleted { get; }

        /// <summary>いまの DayStats と QuestSystem から作る。quests が null ならクエストの条件はどれも満たさない。</summary>
        public static AchievementRecord FromDayStats(QuestSystem quests)
        {
            Func<string, bool> isCompleted = null;
            if (quests != null)
            {
                isCompleted = quests.IsCompleted;
            }

            return new AchievementRecord(DayStats.CollectedIds, DayStats.PhotoSpotIds, DayStats.BuildingCount,
                DayStats.TalkedIds, quests?.All, isCompleted);
        }
    }

    /// <summary>
    /// collectibles.json の称号 13 件を判定し、獲得済みを覚えておく (#65)。
    /// 以前は称号を読むコードが無く、クエストの報酬の 4 件がトーストに名前を出すだけだった。
    ///
    /// 数は結果画面と同じ数え方をする: 隠しアイテムは一覧の source = hidden だけ、写真は一覧の ps_ だけ。
    /// 獲得は取り消さない（条件を満たしたあとで記録が減ることは無いが、読み直しで消えないように覚えておく）。
    /// セーブには載せない。「はじめから」で <see cref="Reset"/> する。
    /// </summary>
    public sealed class AchievementBook
    {
        public const string CollectCount = "collect_count";
        public const string RarityCount = "rarity_count";
        public const string PhotoCount = "photo_count";
        public const string BuildingCount = "building_count";
        public const string NpcCount = "npc_count";
        public const string QuestCount = "quest_count";
        public const string QuestComplete = "quest_complete";

        /// <summary>quest_count の scope がこれなら、サブクエスト（side）を数えない。</summary>
        public const string ScopeMain = "main";

        private readonly HashSet<string> _unlocked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>獲得した称号の id。</summary>
        public IReadOnlyCollection<string> Unlocked => _unlocked;

        /// <summary>獲得済みか。</summary>
        public bool IsUnlocked(string achievementId) => !string.IsNullOrEmpty(achievementId) && _unlocked.Contains(achievementId);

        /// <summary>「はじめから」で全部なくす。</summary>
        public void Reset()
        {
            _unlocked.Clear();
        }

        /// <summary>
        /// 条件を満たしたのにまだ持っていない称号を獲得にして、一覧の順で返す。何も無ければ空。
        /// 呼び出し側（GameManager）がトーストを出す。
        /// </summary>
        public List<CatalogAchievement> Refresh(CollectibleCatalog catalog, AchievementRecord record)
        {
            var unlocked = new List<CatalogAchievement>();
            if (catalog == null)
            {
                return unlocked;
            }

            for (int i = 0; i < catalog.Achievements.Count; i++)
            {
                CatalogAchievement achievement = catalog.Achievements[i];
                if (!_unlocked.Contains(achievement.Id) && IsMet(achievement, catalog, record))
                {
                    _unlocked.Add(achievement.Id);
                    unlocked.Add(achievement);
                }
            }

            return unlocked;
        }

        /// <summary>称号の条件を満たしているか。知らない条件の種類は満たさない扱い。</summary>
        public static bool IsMet(CatalogAchievement achievement, CollectibleCatalog catalog, AchievementRecord record)
        {
            if (achievement == null || catalog == null)
            {
                return false;
            }

            switch (achievement.ConditionType)
            {
                case CollectCount:
                    return AtLeast(catalog.CountHidden(record.Collected), achievement.Count);
                case RarityCount:
                    return AtLeast(catalog.CountRarity(record.Collected, achievement.Rarity), achievement.Count);
                case PhotoCount:
                    return AtLeast(catalog.CountPhotoSpots(record.Photos), achievement.Count);
                case BuildingCount:
                    return AtLeast(record.BuildingCount, achievement.Count);
                case NpcCount:
                    return AtLeast(CountNpcs(achievement, record.Talked), achievement.Count);
                case QuestCount:
                    return AtLeast(CountQuests(achievement, record), achievement.Count);
                case QuestComplete:
                    return !string.IsNullOrEmpty(achievement.TargetId) && record.IsCompleted != null &&
                           record.IsCompleted(achievement.TargetId);
                default:
                    return false;
            }
        }

        /// <summary>数の条件。0 以下の値は「何もしなくても獲得」にならないよう満たさない扱いにする。</summary>
        private static bool AtLeast(int have, int need) => need > 0 && have >= need;

        /// <summary>話した NPC の種類数。condition.npcs があればその中だけ（寮の管理人などは数えない）。</summary>
        private static int CountNpcs(CatalogAchievement achievement, IEnumerable<string> talked)
        {
            if (talked == null)
            {
                return 0;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string npc in talked)
            {
                if (!string.IsNullOrEmpty(npc) && (achievement.Npcs.Count == 0 || achievement.Npcs.Contains(npc)))
                {
                    seen.Add(npc);
                }
            }

            return seen.Count;
        }

        /// <summary>達成したクエストの数。scope = main ならサブクエストを数えない。</summary>
        private static int CountQuests(CatalogAchievement achievement, AchievementRecord record)
        {
            if (record.Quests == null || record.IsCompleted == null)
            {
                return 0;
            }

            bool mainOnly = achievement.Scope == ScopeMain;
            int count = 0;
            foreach (QuestData quest in record.Quests)
            {
                if (quest != null && (!mainOnly || !quest.Side) && record.IsCompleted(quest.Id))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
