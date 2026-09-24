using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 称号 13 件の判定 (#65)。以前は collectibles.json の achievements を読むコードが無く、
    /// クエストの報酬の 4 件がトーストに名前を出すだけで、残りの 9 件は取る手段が無かった。
    /// 判定は実データの条件で見る。数え方は結果画面と同じ（一覧に載った hidden と ps_ だけ）。
    /// </summary>
    public sealed class AchievementBookTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        private static CollectibleCatalog Catalog()
        {
            return CollectibleCatalog.Parse(File.ReadAllText(Path.Combine(DataRoot, "Collectibles", "collectibles.json")));
        }

        private static List<QuestData> Quests()
        {
            var quests = new List<QuestData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(ReadObject(file));
                Assert.IsNotNull(quest, file);
                quests.Add(quest);
            }

            return quests;
        }

        private static CatalogAchievement Find(CollectibleCatalog catalog, string id)
        {
            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                if (achievement.Id == id)
                {
                    return achievement;
                }
            }

            Assert.Fail(id + " が collectibles.json の achievements に無い");
            return null;
        }

        private static List<string> HiddenIds(CollectibleCatalog catalog, int count)
        {
            var ids = new List<string>();
            foreach (CatalogItem item in catalog.Items)
            {
                if (item.IsHidden && ids.Count < count)
                {
                    ids.Add(item.Id);
                }
            }

            return ids;
        }

        private static List<string> SpotIds(CollectibleCatalog catalog, int count)
        {
            var ids = new List<string>();
            foreach (CatalogPhotoSpot spot in catalog.PhotoSpots)
            {
                if (ids.Count < count)
                {
                    ids.Add(spot.Id);
                }
            }

            return ids;
        }

        private static AchievementRecord Record(IEnumerable<string> collected = null, IEnumerable<string> photos = null,
            int buildings = 0, IEnumerable<string> talked = null, IEnumerable<QuestData> quests = null,
            ICollection<string> completed = null)
        {
            return new AchievementRecord(collected ?? new string[0], photos ?? new string[0], buildings,
                talked ?? new string[0], quests ?? new List<QuestData>(),
                id => completed != null && completed.Contains(id));
        }

        private static bool Met(CollectibleCatalog catalog, string id, AchievementRecord record)
        {
            return AchievementBook.IsMet(Find(catalog, id), catalog, record);
        }

        [SetUp]
        public void SetUp()
        {
            DayStats.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            DayStats.Reset();
        }

        [Test]
        public void NothingDone_UnlocksNothing()
        {
            Assert.IsEmpty(new AchievementBook().Refresh(Catalog(), Record(quests: Quests())));
        }

        [Test]
        public void CollectCount_CountsOnlyHiddenItems()
        {
            CollectibleCatalog catalog = Catalog();

            // 牛乳と葉（クエストの拾い物）とクエストの報酬は、隠しアイテムの数に入らない。
            Assert.IsFalse(Met(catalog, "ach_first_find", Record(collected: new[] { "milk", "leaf", "c_sora_charm" })));
            Assert.IsTrue(Met(catalog, "ach_first_find", Record(collected: HiddenIds(catalog, 1))));

            Assert.IsFalse(Met(catalog, "ach_collector_5", Record(collected: HiddenIds(catalog, 4))));
            Assert.IsTrue(Met(catalog, "ach_collector_5", Record(collected: HiddenIds(catalog, 5))));

            Assert.IsFalse(Met(catalog, "ach_collector_all", Record(collected: HiddenIds(catalog, catalog.HiddenCount - 1))));
            Assert.IsTrue(Met(catalog, "ach_collector_all", Record(collected: HiddenIds(catalog, catalog.HiddenCount))));
        }

        [Test]
        public void RarityCount_NeedsTheLegendaryItem()
        {
            CollectibleCatalog catalog = Catalog();

            Assert.IsFalse(Met(catalog, "ach_legendary", Record(collected: new[] { "c_gate_keyring" })));
            Assert.IsTrue(Met(catalog, "ach_legendary", Record(collected: new[] { "c_dome_key" })));
        }

        [Test]
        public void PhotoCount_CountsOnlyCatalogSpots()
        {
            CollectibleCatalog catalog = Catalog();
            var five = new List<string>(SpotIds(catalog, 5)) { "kcd_20260924_120000.png", "ps_unknown" };

            Assert.IsFalse(Met(catalog, "ach_photographer", Record(photos: five)));
            Assert.IsTrue(Met(catalog, "ach_photographer", Record(photos: SpotIds(catalog, catalog.PhotoSpotCount))));
        }

        [Test]
        public void BuildingCount_NeedsAllNineBuildings()
        {
            CollectibleCatalog catalog = Catalog();

            Assert.IsFalse(Met(catalog, "ach_all_buildings", Record(buildings: 8)));
            Assert.IsTrue(Met(catalog, "ach_all_buildings", Record(buildings: 9)));
        }

        [Test]
        public void NpcCount_CountsOnlyTheFourCampusNpcs()
        {
            CollectibleCatalog catalog = Catalog();

            // 寮の管理人（裏の場所）は「4 人の NPC」に入らない。
            Assert.IsFalse(Met(catalog, "ach_friends",
                Record(talked: new[] { "dorm_head", "inari", "kaname", "sora", "sora" })));
            Assert.IsTrue(Met(catalog, "ach_friends", Record(talked: new[] { "inari", "kaname", "sora", "prof" })));
        }

        [Test]
        public void NpcCount_ListsNpcsThatHaveDialogue()
        {
            CatalogAchievement friends = Find(Catalog(), "ach_friends");

            Assert.AreEqual(AchievementBook.NpcCount, friends.ConditionType);
            Assert.AreEqual(friends.Count, friends.Npcs.Count, "npcs の人数と value が違う");
            foreach (string npc in friends.Npcs)
            {
                Assert.IsTrue(File.Exists(Path.Combine(DataRoot, "Dialogue", npc + ".json")), npc + " の会話データが無い");
            }
        }

        [Test]
        public void QuestCount_MainScopeSkipsSideQuests()
        {
            CollectibleCatalog catalog = Catalog();
            List<QuestData> quests = Quests();
            var main = new List<string>();
            var side = new List<string>();
            foreach (QuestData quest in quests)
            {
                (quest.Side ? side : main).Add(quest.Id);
            }

            Assert.AreEqual(7, main.Count, "本編のクエストが 7 本ではない: " + string.Join(", ", main));
            Assert.AreEqual(6, side.Count, "サブクエストが 6 本ではない: " + string.Join(", ", side));
            Assert.AreEqual(main.Count, Find(catalog, "ach_main_story").Count);
            Assert.AreEqual(AchievementBook.ScopeMain, Find(catalog, "ach_main_story").Scope);
            Assert.AreEqual(quests.Count, Find(catalog, "ach_all_quests").Count);

            // サブクエストを 6 本と本編を 6 本では「はじめての一日」にならない（以前の数え方なら 12 本で届く）。
            var sixMain = new HashSet<string>(side);
            sixMain.UnionWith(main.GetRange(0, 6));
            Assert.IsFalse(Met(catalog, "ach_main_story", Record(quests: quests, completed: sixMain)));

            var allMain = new HashSet<string>(main);
            Assert.IsTrue(Met(catalog, "ach_main_story", Record(quests: quests, completed: allMain)));
            Assert.IsFalse(Met(catalog, "ach_all_quests", Record(quests: quests, completed: allMain)));

            var all = new HashSet<string>(main);
            all.UnionWith(side);
            Assert.IsTrue(Met(catalog, "ach_all_quests", Record(quests: quests, completed: all)));
        }

        [Test]
        public void QuestComplete_FollowsTheQuest()
        {
            CollectibleCatalog catalog = Catalog();

            Assert.IsFalse(Met(catalog, "ach_sprinter", Record(completed: new HashSet<string> { "q_lunch" })));
            Assert.IsTrue(Met(catalog, "ach_sprinter", Record(completed: new HashSet<string> { "q_gym" })));
        }

        [Test]
        public void QuestRewardAchievements_AreUnlockedByTheirQuest()
        {
            // 報酬の称号のトーストは AchievementBook が出す（QuestTrackerView では出さない）。
            // そのため rewardType = achievement のクエストは、その称号の quest_complete の対象でなければならない。
            CollectibleCatalog catalog = Catalog();
            var ids = new HashSet<string>();
            foreach (QuestData quest in Quests())
            {
                ids.Add(quest.Id);
                if (quest.RewardType != QuestData.RewardAchievement)
                {
                    continue;
                }

                CatalogAchievement reward = Find(catalog, quest.RewardId);
                Assert.AreEqual(AchievementBook.QuestComplete, reward.ConditionType, quest.RewardId);
                Assert.AreEqual(quest.Id, reward.TargetId, quest.RewardId + " が " + quest.Id + " の達成で取れない");
            }

            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                if (achievement.ConditionType == AchievementBook.QuestComplete)
                {
                    Assert.IsTrue(ids.Contains(achievement.TargetId), achievement.Id + " の対象 " + achievement.TargetId + " が無い");
                }
            }
        }

        [Test]
        public void AllThirteen_CanBeUnlocked()
        {
            CollectibleCatalog catalog = Catalog();
            List<QuestData> quests = Quests();
            var completed = new HashSet<string>();
            foreach (QuestData quest in quests)
            {
                completed.Add(quest.Id);
            }

            AchievementRecord everything = Record(
                HiddenIds(catalog, catalog.HiddenCount),
                SpotIds(catalog, catalog.PhotoSpotCount),
                DayEndEvaluator.DefaultBuildingTotal,
                Find(catalog, "ach_friends").Npcs,
                quests,
                completed);

            var book = new AchievementBook();
            List<CatalogAchievement> unlocked = book.Refresh(catalog, everything);

            var missing = new List<string>();
            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                if (!book.IsUnlocked(achievement.Id))
                {
                    missing.Add(achievement.Id + "（" + achievement.ConditionType + "）");
                }
            }

            Assert.IsEmpty(missing, "取れない称号: " + string.Join(", ", missing));
            Assert.AreEqual(13, unlocked.Count);
        }

        [Test]
        public void Refresh_ReportsEachAchievementOnceUntilReset()
        {
            CollectibleCatalog catalog = Catalog();
            var book = new AchievementBook();
            AchievementRecord one = Record(collected: HiddenIds(catalog, 1));

            List<CatalogAchievement> first = book.Refresh(catalog, one);
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual("ach_first_find", first[0].Id);
            Assert.IsEmpty(book.Refresh(catalog, one), "同じ称号をもう一度知らせた");

            book.Reset();
            Assert.IsFalse(book.IsUnlocked("ach_first_find"));
            Assert.AreEqual(1, book.Refresh(catalog, one).Count);
        }

        [Test]
        public void UnknownOrZeroConditions_AreNeverMet()
        {
            CollectibleCatalog catalog = CollectibleCatalog.Parse(
                "{\"achievements\":[" +
                "{\"id\":\"ach_zero\",\"condition\":{\"type\":\"collect_count\",\"value\":0}}," +
                "{\"id\":\"ach_odd\",\"condition\":{\"type\":\"dance_count\",\"value\":1}}," +
                "{\"id\":\"ach_none\",\"condition\":{\"type\":\"quest_complete\"}}]}");

            Assert.IsEmpty(new AchievementBook().Refresh(catalog, Record(completed: new HashSet<string> { "" })));
            Assert.IsFalse(AchievementBook.IsMet(null, catalog, Record()));
            Assert.IsFalse(AchievementBook.IsMet(catalog.Achievements[0], null, Record()));
        }

        [Test]
        public void FromDayStats_ReadsTalksPickupsAndQuests()
        {
            CollectibleCatalog catalog = Catalog();
            var quests = new QuestSystem();
            quests.Load(new[]
            {
                QuestData.FromJson(MiniJson.Deserialize(
                    "{\"id\":\"q_gym\",\"order\":1,\"autoStart\":true," +
                    "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"gym\"}]}") as Dictionary<string, object>)
            });

            DayStats.NoteCollect("c_bench_coin");
            foreach (string npc in new[] { "inari", "kaname", "sora", "prof" })
            {
                DayStats.NoteTalk(npc);
            }

            quests.ReportEnter("gym");
            AchievementRecord record = AchievementRecord.FromDayStats(quests);

            Assert.IsTrue(Met(catalog, "ach_first_find", record));
            Assert.IsTrue(Met(catalog, "ach_friends", record));
            Assert.IsTrue(Met(catalog, "ach_sprinter", record));

            // QuestSystem が無くても落ちない（クエストの条件だけ満たさない）。
            AchievementRecord noQuests = AchievementRecord.FromDayStats(null);
            Assert.IsTrue(Met(catalog, "ach_first_find", noQuests));
            Assert.IsFalse(Met(catalog, "ach_sprinter", noQuests));
            Assert.IsFalse(Met(catalog, "ach_main_story", noQuests));
        }

        [Test]
        public void DayStats_NoteTalkCountsEachNpcOnceAndResets()
        {
            int before = DayStats.Version;
            DayStats.NoteTalk("inari");
            DayStats.NoteTalk("inari");
            DayStats.NoteTalk(null);

            Assert.AreEqual(before + 1, DayStats.Version, "同じ NPC や空の id で数え直しの印が進んだ");
            Assert.IsTrue(DayStats.HasTalked("inari"));
            Assert.AreEqual(1, DayStats.TalkedIds.Count);

            DayStats.Reset();
            Assert.IsFalse(DayStats.HasTalked("inari"));
            Assert.AreEqual(0, DayStats.TalkedIds.Count);
        }

        [Test]
        public void QuestRewards_RecordCollectibleRewardsButNotAsHiddenItems()
        {
            CollectibleCatalog catalog = Catalog();
            var quests = new QuestSystem();
            quests.Load(new[]
            {
                QuestData.FromJson(MiniJson.Deserialize(
                    "{\"id\":\"q_charm\",\"order\":1,\"autoStart\":true," +
                    "\"rewardType\":\"collectible\",\"rewardId\":\"c_sora_charm\"," +
                    "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"sora\"}]}") as Dictionary<string, object>),
                QuestData.FromJson(MiniJson.Deserialize(
                    "{\"id\":\"q_title\",\"order\":2,\"autoStart\":true," +
                    "\"rewardType\":\"achievement\",\"rewardId\":\"ach_sprinter\"," +
                    "\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"sora\"}]}") as Dictionary<string, object>)
            });

            Assert.AreEqual(0, QuestRewards.GrantCompleted(quests), "達成前に報酬を渡した");
            Assert.IsFalse(DayStats.HasCollected("c_sora_charm"));

            quests.ReportTalk("sora");

            Assert.AreEqual(1, QuestRewards.GrantCompleted(quests), "称号の報酬は拾った物にしない");
            Assert.IsTrue(DayStats.HasCollected("c_sora_charm"));
            Assert.IsFalse(DayStats.HasCollected("ach_sprinter"));
            Assert.AreEqual(0, DayStats.HiddenCollectedCount(catalog), "クエストの報酬を隠しアイテムに数えた");

            // 何度呼んでも増えない（GameManager は変化のたびに呼ぶ）。
            int version = DayStats.Version;
            QuestRewards.GrantCompleted(quests);
            Assert.AreEqual(version, DayStats.Version);
        }

        [Test]
        public void RealQuests_ReadTheSideFlag()
        {
            foreach (QuestData quest in Quests())
            {
                Assert.AreEqual(quest.Id.StartsWith("q_sq_"), quest.Side, quest.Id + " の side と id の q_sq_ が合わない");
            }
        }
    }
}
