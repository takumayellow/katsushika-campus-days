using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 称号 16 件の判定 (#65, #175, #41, #389)。以前は collectibles.json の achievements を読むコードが無く、
    /// クエストの報酬の 4 件がトーストに名前を出すだけで、残りの 9 件は取る手段が無かった。
    /// 判定は実データの条件で見る。数え方は結果画面と同じ（一覧に載った hidden と ps_ だけ）。
    /// 見たエンド（ending_seen）は EndingProgress が持つので、ゲームの記録から作る道は空のフォルダに向けて通す。
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
            ICollection<string> completed = null, IEnumerable<string> endingsSeen = null)
        {
            return new AchievementRecord(collected ?? new string[0], photos ?? new string[0], buildings,
                talked ?? new string[0], quests ?? new List<QuestData>(),
                id => completed != null && completed.Contains(id), endingsSeen);
        }

        private static bool Met(CollectibleCatalog catalog, string id, AchievementRecord record)
        {
            return AchievementBook.IsMet(Find(catalog, id), catalog, record);
        }

        private string _folder;

        [SetUp]
        public void SetUp()
        {
            DayStats.Reset();

            // FromDayStats は見たエンドの記録を読む。この端末で遊んだ記録（persistentDataPath）に結果が左右されないよう、
            // 空のフォルダに向ける。EndingProgress は置き場が変わると読み直す。
            _folder = Path.Combine(Path.GetTempPath(), "kcd-editmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            SaveSystem.DirectoryOverride = _folder;
        }

        [TearDown]
        public void TearDown()
        {
            DayStats.Reset();
            SaveSystem.DirectoryOverride = null;
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
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
        public void BuildingCount_NeedsAllTenBuildings()
        {
            CollectibleCatalog catalog = Catalog();

            Assert.IsFalse(Met(catalog, "ach_all_buildings", Record(buildings: 9)));
            Assert.IsTrue(Met(catalog, "ach_all_buildings", Record(buildings: 10)));
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
                // 隠しクエストはクエストの数の称号に数えない (#175)。
                if (!quest.Secret)
                {
                    (quest.Side ? side : main).Add(quest.Id);
                }
            }

            Assert.AreEqual(8, main.Count, "本編のクエストが 8 本ではない: " + string.Join(", ", main));
            Assert.AreEqual(8, side.Count, "サブクエストが 8 本ではない: " + string.Join(", ", side));
            Assert.AreEqual(main.Count, Find(catalog, "ach_main_story").Count);
            Assert.AreEqual(AchievementBook.ScopeMain, Find(catalog, "ach_main_story").Scope);
            Assert.AreEqual(main.Count + side.Count, Find(catalog, "ach_all_quests").Count);

            // サブクエストを 8 本と本編を 7 本では「はじめての一日」にならない（本編とサブを区別しなければ 15 本で届く）。
            var mainButOne = new HashSet<string>(side);
            mainButOne.UnionWith(main.GetRange(0, main.Count - 1));
            Assert.IsFalse(Met(catalog, "ach_main_story", Record(quests: quests, completed: mainButOne)));

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
        public void AllFifteen_CanBeUnlocked()
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
                completed,
                new[] { DormRoute.EndingId });

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
            Assert.AreEqual(16, unlocked.Count);
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
                "{\"id\":\"ach_none\",\"condition\":{\"type\":\"quest_complete\"}}," +
                "{\"id\":\"ach_no_ending\",\"condition\":{\"type\":\"ending_seen\"}}]}");

            Assert.IsEmpty(new AchievementBook().Refresh(catalog,
                Record(completed: new HashSet<string> { "" }, endingsSeen: new[] { "" })));
            Assert.IsFalse(AchievementBook.IsMet(null, catalog, Record()));
            Assert.IsFalse(AchievementBook.IsMet(catalog.Achievements[0], null, Record()));
        }

        [Test]
        public void EndingSeen_NeedsTheEndingOfTheAchievement()
        {
            CollectibleCatalog catalog = Catalog();

            Assert.IsFalse(Met(catalog, "ach_skip_school", Record()));
            Assert.IsFalse(Met(catalog, "ach_skip_school", Record(endingsSeen: new[] { "end_other" })));
            Assert.IsTrue(Met(catalog, "ach_skip_school", Record(endingsSeen: new[] { DormRoute.EndingId })));

            // id の大文字小文字は区別する（ファイルに書かれた id と同じ並びだけを見たことにする）。
            Assert.IsFalse(Met(catalog, "ach_skip_school", Record(endingsSeen: new[] { DormRoute.EndingId.ToUpperInvariant() })));
        }

        [Test]
        public void SkipSchool_IsTiedToTheDormEnding()
        {
            CatalogAchievement achievement = Find(Catalog(), "ach_skip_school");

            Assert.AreEqual(AchievementBook.EndingSeen, achievement.ConditionType);
            Assert.AreEqual(DormRoute.EndingId, achievement.TargetId, "称号の条件が寮のエンドの id とずれた");
        }

        [Test]
        public void FromDayStats_ReadsTheEndingRecord()
        {
            CollectibleCatalog catalog = Catalog();
            Assert.IsFalse(Met(catalog, "ach_skip_school", AchievementRecord.FromDayStats(null)));

            EndingProgress.Note(DormRoute.EndingId);

            Assert.IsTrue(Met(catalog, "ach_skip_school", AchievementRecord.FromDayStats(null)));
        }

        [Test]
        public void SeenEndings_SurviveANewGame()
        {
            // 見たエンドはこの端末の記録で、プレイごとの記録ではない。「はじめから」は称号と DayStats を空にするが、
            // ファイルには触れないので、次に数えたときに自主休講はまた獲得になる。
            CollectibleCatalog catalog = Catalog();
            var book = new AchievementBook();
            EndingProgress.Note(DormRoute.EndingId);
            Assert.AreEqual(1, book.Refresh(catalog, AchievementRecord.FromDayStats(null)).Count);

            book.Reset();
            DayStats.Reset();

            Assert.IsFalse(book.IsUnlocked("ach_skip_school"));
            List<CatalogAchievement> again = book.Refresh(catalog, AchievementRecord.FromDayStats(null));
            Assert.AreEqual(1, again.Count);
            Assert.AreEqual("ach_skip_school", again[0].Id);
        }

        [Test]
        public void BeginNewGame_KeepsTheEndingRecordAndTheAchievementComesBack()
        {
            // 「はじめから」の道そのもの（GameManager.BeginNewGame）を通す。EditMode では Awake が走らない
            // ので、NewGameTests と同じく、ログの取りこぼしを許す。
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("GameManagerForTest");

            try
            {
                GameManager manager = go.AddComponent<GameManager>();
                EndingProgress.Note(DormRoute.EndingId);
                manager.SyncAchievementsQuietly();
                Assert.IsTrue(manager.Achievements.IsUnlocked("ach_skip_school"), "記録したのに称号が取れていない");

                manager.BeginNewGame();

                Assert.IsFalse(manager.Achievements.IsUnlocked("ach_skip_school"), "称号が前の周回から残っている");
                Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "「はじめから」で見たエンドの記録が消えた");
                Assert.IsTrue(File.Exists(SaveSystem.PathOf(EndingProgress.FileName)), "「はじめから」で記録のファイルが消えた");

                manager.SyncAchievementsQuietly();

                Assert.IsTrue(manager.Achievements.IsUnlocked("ach_skip_school"), "記録は残っているのに称号が戻らない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = ignoring;
            }
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
                // 隠しクエストもサブの扱い (#175)。
                bool expected = quest.Id.StartsWith("q_sq_") || quest.Secret;
                Assert.AreEqual(expected, quest.Side, quest.Id + " の side と id の q_sq_（または secret）が合わない");
            }
        }
    }
}
