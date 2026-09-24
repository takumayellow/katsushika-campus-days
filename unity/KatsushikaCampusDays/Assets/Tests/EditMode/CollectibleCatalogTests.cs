using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 収集物の一覧（collectibles.json）の読み方と数え方 (#65)。
    /// 結果画面は拾った物を全部数えていて、一覧に無い牛乳と葉で 2/20 のまま止まっていた。
    /// </summary>
    public sealed class CollectibleCatalogTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static string CatalogPath => Path.Combine(DataRoot, "Collectibles", "collectibles.json");

        private static CollectibleCatalog RealCatalog()
        {
            return CollectibleCatalog.Parse(File.ReadAllText(CatalogPath));
        }

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
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
        public void RealCatalog_HasTwentyHiddenThreeRewardsSixSpotsThirteenAchievements()
        {
            CollectibleCatalog catalog = RealCatalog();
            Dictionary<string, object> root = ReadObject(CatalogPath);

            // 重複した id は読み飛ばすので、JSON の件数と読めた件数が同じなら重複も無い。
            Assert.AreEqual(MiniJson.GetArray(root, "collectibles").Count, catalog.Items.Count, "collectibles の id が重複している");
            Assert.AreEqual(MiniJson.GetArray(root, "photo_spots").Count, catalog.PhotoSpots.Count, "photo_spots の id が重複している");
            Assert.AreEqual(MiniJson.GetArray(root, "achievements").Count, catalog.Achievements.Count);

            int rewards = 0;
            foreach (CatalogItem item in catalog.Items)
            {
                if (item.Source == CollectibleCatalog.SourceQuest)
                {
                    rewards++;
                }
            }

            Assert.AreEqual(20, catalog.HiddenCount);
            Assert.AreEqual(3, rewards);
            Assert.AreEqual(catalog.Items.Count, catalog.HiddenCount + rewards, "source が hidden でも quest でもない物がある");
            Assert.AreEqual(6, catalog.PhotoSpotCount);
            Assert.AreEqual(13, catalog.Achievements.Count);
        }

        [Test]
        public void RealCatalog_HiddenItemsAndSpotsArePlacedAndRewardsAreNot()
        {
            CollectibleCatalog catalog = RealCatalog();
            var rarities = new HashSet<string>(MiniJson.ToStringList(MiniJson.GetArray(ReadObject(CatalogPath), "rarities")));

            foreach (CatalogItem item in catalog.Items)
            {
                StringAssert.StartsWith("c_", item.Id);
                Assert.IsTrue(rarities.Contains(item.Rarity), item.Id + " のレア度 " + item.Rarity + " が rarities に無い");
                Assert.AreEqual(item.IsHidden, item.Place.IsPlaced,
                    item.IsHidden ? item.Id + " は隠しアイテムなのに置き場所が無い" : item.Id + " は報酬なのに置き場所がある");
            }

            foreach (CatalogPhotoSpot spot in catalog.PhotoSpots)
            {
                StringAssert.StartsWith("ps_", spot.Id, "DayStats.NotePhoto は ps_ で始まる id だけ数える");
                Assert.IsTrue(spot.Place.IsPlaced, spot.Id + " に置き場所が無い");
                Assert.Greater(spot.LookDir.sqrMagnitude, 0.01f, spot.Id + " に撮る向きが無い");
            }
        }

        [Test]
        public void Place_ReadsOutdoorPointsAndIndoorPois()
        {
            CollectibleCatalog catalog = RealCatalog();

            CatalogPlace gate = catalog.FindItem("c_gate_keyring").Place;
            Assert.IsTrue(gate.IsOutdoor);
            Assert.IsFalse(gate.IsIndoor);
            Assert.AreEqual(153.6f, gate.XZ.x, 1e-3f);
            Assert.AreEqual(-92.2f, gate.XZ.y, 1e-3f);

            CatalogPlace goggles = catalog.FindItem("c_lab_goggles").Place;
            Assert.IsTrue(goggles.IsIndoor);
            Assert.AreEqual("lab1", goggles.Building);
            Assert.AreEqual("poi_lab1_lab_b", goggles.Poi);

            Assert.IsFalse(catalog.FindItem("c_sora_charm").Place.IsPlaced);
        }

        [Test]
        public void CountHidden_IgnoresQuestPickupsRewardsAndDuplicates()
        {
            CollectibleCatalog catalog = RealCatalog();

            int count = catalog.CountHidden(new[] { "milk", "leaf", "c_gate_keyring", "c_gate_keyring", "c_sora_charm", "c_dome_key" });

            Assert.AreEqual(2, count, "隠しアイテムは c_gate_keyring と c_dome_key の 2 種");
        }

        [Test]
        public void CountPhotoSpots_IgnoresFreeShotsAndUnknownSpots()
        {
            CollectibleCatalog catalog = RealCatalog();

            int count = catalog.CountPhotoSpots(new[] { "kcd_20260924_120000.png", "ps_gate", "ps_mall_view", "ps_gym_catwalk" });

            Assert.AreEqual(2, count);
        }

        [Test]
        public void CountRarity_CountsOnlyThatRarity()
        {
            CollectibleCatalog catalog = RealCatalog();

            Assert.AreEqual(0, catalog.CountRarity(new[] { "c_gate_keyring", "milk" }, "legendary"));
            Assert.AreEqual(1, catalog.CountRarity(new[] { "c_gate_keyring", "c_dome_key" }, "legendary"));
        }

        [Test]
        public void DayStats_ResultCountsOnlyCatalogIds()
        {
            CollectibleCatalog catalog = RealCatalog();

            // 牛乳 1 本と葉 5 枚は q_lunch と q_park の拾い物。以前はこれで 2/20 になり、それ以上増えなかった。
            DayStats.NoteCollect("milk");
            DayStats.NoteCollect("leaf");
            DayStats.NoteCollect("c_bench_coin");
            DayStats.NotePhoto("ps_pond_library");
            DayStats.NotePhoto("kcd_20260924_120000.png");

            Assert.AreEqual(3, DayStats.CollectedCount, "拾った物の記録そのものは全部残す");
            Assert.AreEqual(1, DayStats.HiddenCollectedCount(catalog));
            Assert.AreEqual(1, DayStats.CatalogPhotoCount(catalog));
            Assert.AreEqual(0, DayStats.HiddenCollectedCount(null));
        }

        [Test]
        public void DisplayName_FallsBackToTheJsonName()
        {
            CollectibleCatalog catalog = CollectibleCatalog.Parse(
                "{\"collectibles\":[{\"id\":\"c_test_only\",\"name_ja\":\"テスト\",\"name_en\":\"Test\",\"source\":\"hidden\"," +
                "\"position\":{\"x\":1,\"z\":2}}]}");

            // ローカライズに無い id は JSON の名前を出す（キーそのものは出さない）。
            Assert.AreEqual(L.Pick("テスト", "Test"), catalog.FindItem("c_test_only").DisplayName);
            Assert.AreEqual(1, catalog.HiddenCount);
        }

        [Test]
        public void Parse_EmptyOrBrokenInputGivesAnEmptyCatalog()
        {
            Assert.AreEqual(0, CollectibleCatalog.Parse(null).Items.Count);
            Assert.AreEqual(0, CollectibleCatalog.Parse("[]").HiddenCount);
            Assert.IsNull(CollectibleCatalog.Parse("{}").FindItem("c_gate_keyring"));
        }

        [Test]
        public void EveryEntry_HasJapaneseAndEnglishNames()
        {
            Dictionary<string, object> ja = MiniJson.GetObject(ReadObject(Path.Combine(DataRoot, "Localization", "ja.json")), "strings");
            Dictionary<string, object> en = MiniJson.GetObject(ReadObject(Path.Combine(DataRoot, "Localization", "en.json")), "strings");
            CollectibleCatalog catalog = RealCatalog();

            var keys = new List<string>();
            foreach (CatalogItem item in catalog.Items)
            {
                keys.Add("item." + item.Id + ".name");
            }

            foreach (CatalogPhotoSpot spot in catalog.PhotoSpots)
            {
                keys.Add("photo." + spot.Id + ".name");
            }

            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                keys.Add("ach." + achievement.Id + ".name");
            }

            foreach (string key in keys)
            {
                Assert.IsTrue(ja.ContainsKey(key), "ja.json に " + key + " が無い");
                Assert.IsTrue(en.ContainsKey(key), "en.json に " + key + " が無い");
            }
        }

        [Test]
        public void QuestRewards_PointAtCatalogEntries()
        {
            CollectibleCatalog catalog = RealCatalog();
            var achievements = new HashSet<string>();
            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                achievements.Add(achievement.Id);
            }

            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(ReadObject(file));
                if (quest.RewardType == "collectible")
                {
                    CatalogItem reward = catalog.FindItem(quest.RewardId);
                    Assert.IsNotNull(reward, quest.Id + " の報酬 " + quest.RewardId + " が一覧に無い");
                    Assert.AreEqual(CollectibleCatalog.SourceQuest, reward.Source, quest.Id + " の報酬が隠しアイテムになっている");
                }
                else if (quest.RewardType == QuestData.RewardAchievement)
                {
                    Assert.IsTrue(achievements.Contains(quest.RewardId), quest.Id + " の報酬の称号 " + quest.RewardId + " が一覧に無い");
                }
                else
                {
                    Assert.IsEmpty(quest.RewardType, quest.Id + " の rewardType が分からない");
                }
            }
        }
    }
}
