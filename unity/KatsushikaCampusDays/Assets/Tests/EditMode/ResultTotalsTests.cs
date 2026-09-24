using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 結果画面の分母（Ending/result.json の totals）が実データの件数と合っているか (#65)。
    /// 分母が実際に集められる数より大きいと、全部集めても 100% にならない。
    /// 隠しアイテムの数は collectibles.json の source = hidden の件数（クエストの報酬 3 件は入れない）。
    /// </summary>
    public sealed class ResultTotalsTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static Dictionary<string, object> Totals()
        {
            string path = Path.Combine(DataRoot, "Ending", "result.json");
            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " はオブジェクトとして読めない");
            Dictionary<string, object> totals = MiniJson.GetObject(root, "totals");
            Assert.IsNotNull(totals, "result.json に totals が無い");
            return totals;
        }

        private static CollectibleCatalog Catalog()
        {
            return CollectibleCatalog.Parse(File.ReadAllText(Path.Combine(DataRoot, "Collectibles", "collectibles.json")));
        }

        [Test]
        public void Collectibles_EqualsTheHiddenItemsInTheCatalog()
        {
            Assert.AreEqual(Catalog().HiddenCount, MiniJson.GetInt(Totals(), "collectibles", -1));
        }

        [Test]
        public void Photos_EqualsThePhotoSpotsInTheCatalog()
        {
            Assert.AreEqual(Catalog().PhotoSpotCount, MiniJson.GetInt(Totals(), "photos", -1));
        }

        [Test]
        public void Quests_EqualsTheQuestFiles()
        {
            int files = Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json").Length;
            Assert.AreEqual(files, MiniJson.GetInt(Totals(), "quests", -1));
        }

        [Test]
        public void Buildings_EqualsTheInteriorsOtherThanTheDorm()
        {
            // 屋内は Models/Interiors/<building>.json。寮は本編の集計に入れない（DormEndingTests）。
            string folder = Path.Combine(Application.dataPath, "Models", "Interiors");
            var buildings = new List<string>();
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (id != "dorm" && !id.StartsWith("_"))
                {
                    buildings.Add(id);
                }
            }

            Assert.AreEqual(buildings.Count, MiniJson.GetInt(Totals(), "buildings", -1), string.Join(", ", buildings));
        }

        [Test]
        public void EvaluatorDefaults_MatchResultJson()
        {
            // result.json が読めないときの既定値も同じ分母にしておく。
            Dictionary<string, object> totals = Totals();
            Assert.AreEqual(MiniJson.GetInt(totals, "quests"), DayEndEvaluator.DefaultQuestTotal);
            Assert.AreEqual(MiniJson.GetInt(totals, "collectibles"), DayEndEvaluator.DefaultCollectibleTotal);
            Assert.AreEqual(MiniJson.GetInt(totals, "photos"), DayEndEvaluator.DefaultPhotoTotal);
            Assert.AreEqual(MiniJson.GetInt(totals, "buildings"), DayEndEvaluator.DefaultBuildingTotal);
        }

        [Test]
        public void AchievementTargets_AreReachable()
        {
            // 称号の数の条件が、集められる数を超えていないか。
            CollectibleCatalog catalog = Catalog();
            Dictionary<string, object> totals = Totals();
            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                switch (achievement.ConditionType)
                {
                    case "collect_count":
                        Assert.LessOrEqual(achievement.Count, catalog.HiddenCount, achievement.Id);
                        break;
                    case "photo_count":
                        Assert.LessOrEqual(achievement.Count, catalog.PhotoSpotCount, achievement.Id);
                        break;
                    case "building_count":
                        Assert.LessOrEqual(achievement.Count, MiniJson.GetInt(totals, "buildings"), achievement.Id);
                        break;
                    case "quest_count":
                        Assert.LessOrEqual(achievement.Count, MiniJson.GetInt(totals, "quests"), achievement.Id);
                        break;
                }
            }
        }

        [Test]
        public void ResourcesCopies_MatchTheData()
        {
            // ランタイムは Resources/KCD の写しを読む（DataBundler が Assets/Data から写す）。
            // 片方だけ直すと、テストは Data を、ゲームは古い Resources を見ることになる。
            string resources = Path.Combine(Application.dataPath, "Resources", "KCD");
            foreach (string folder in new[] { "Quests", "Dialogue", "Collectibles", "Localization", "Mobs", "Ending" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, folder), "*.json"))
                {
                    string copy = Path.Combine(resources, folder, Path.GetFileName(file));
                    Assert.IsTrue(File.Exists(copy), copy + " が無い");
                    Assert.AreEqual(Normalize(File.ReadAllText(file)), Normalize(File.ReadAllText(copy)),
                        folder + "/" + Path.GetFileName(file) + " の Resources の写しが古い");
                }
            }
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");
    }
}
