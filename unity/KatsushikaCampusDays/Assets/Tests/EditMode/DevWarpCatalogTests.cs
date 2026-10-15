using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 開発者用ワープメニューの行き先の一覧。シーンから拾ったゾーン・入口・屋内・NPC が欠けず、重ならず、
    /// 手書きの座標なしで作られること。
    /// </summary>
    public sealed class DevWarpCatalogTests
    {
        private static readonly DevZoneSource[] Zones =
        {
            new DevZoneSource("zone_b", "ゾーンB", new Vector3(10f, 0.1f, 20f)),
            new DevZoneSource("zone_a", "ゾーンA", new Vector3(-5f, 0.1f, 8f)),
            new DevZoneSource("zone_a", "重複A", new Vector3(99f, 0f, 99f)),
            new DevZoneSource(string.Empty, "id なし", Vector3.zero)
        };

        private static readonly DevEntranceSource[] Entrances =
        {
            new DevEntranceSource("lib", "図書館", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f)),
            new DevEntranceSource("gym", "体育館", new Vector3(30f, 0f, 0f), new Vector3(1f, 0f, 0f))
        };

        private static readonly DevInteriorSource[] Interiors =
        {
            new DevInteriorSource("lib", "図書館"),
            new DevInteriorSource("lab", "実験棟")
        };

        private static readonly DevNpcSource[] Npcs =
        {
            new DevNpcSource("inari", "稲荷先輩", new Vector3(3f, 0f, 3f), new Vector3(0f, 0f, 1f)),
            new DevNpcSource("botchan", "坊っちゃん", new Vector3(-3f, 0f, 3f), Vector3.zero)
        };

        private static DevWarpCatalog Build() => DevWarpCatalog.Build(Zones, Entrances, Interiors, Npcs);

        private static List<string> Ids(IReadOnlyList<DevWarpTarget> tab)
        {
            var ids = new List<string>();
            foreach (DevWarpTarget target in tab)
            {
                ids.Add(target.Id);
            }

            return ids;
        }

        [Test]
        public void Places_ListEveryZoneOnceThenEveryEntranceFront()
        {
            Assert.AreEqual(new[] { "zone_a", "zone_b", "door_gym", "door_lib" },
                Ids(Build().Tab((int)DevWarpKind.Place)));
        }

        [Test]
        public void Places_ZoneStandsWhereTheSceneSaysAndFirstDuplicateWins()
        {
            DevWarpTarget zone = Build().Tab((int)DevWarpKind.Place)[0];
            Assert.AreEqual(new Vector3(-5f, 0.1f, 8f), zone.Position);
            Assert.IsTrue(zone.HasPosition);
        }

        [Test]
        public void EntranceFront_StandsOutsideTheDoorFacingIt()
        {
            IReadOnlyList<DevWarpTarget> places = Build().Tab((int)DevWarpKind.Place);

            DevWarpTarget lib = places[3];
            Assert.AreEqual("door_lib", lib.Id);
            Assert.AreEqual(new Vector3(0f, 0f, DevWarpCatalog.EntranceStandOff), lib.Position);
            Assert.AreEqual(180f, Mathf.Abs(lib.Yaw), 0.01f);

            DevWarpTarget gym = places[2];
            Assert.AreEqual(new Vector3(30f + DevWarpCatalog.EntranceStandOff, 0f, 0f), gym.Position);
            Assert.AreEqual(-90f, gym.Yaw, 0.01f);
        }

        [Test]
        public void Interiors_ListEveryBuildingAndUseTheEntranceFrontWhenKnown()
        {
            IReadOnlyList<DevWarpTarget> tab = Build().Tab((int)DevWarpKind.Interior);
            Assert.AreEqual(new[] { "lab", "lib" }, Ids(tab));
            Assert.IsFalse(tab[0].HasPosition, "入口の無い屋内に位置がある");
            Assert.IsTrue(tab[1].HasPosition);
            Assert.AreEqual(new Vector3(0f, 0f, DevWarpCatalog.EntranceStandOff), tab[1].Position);
        }

        [Test]
        public void Npcs_StandInFrontOfThePersonFacingThem()
        {
            IReadOnlyList<DevWarpTarget> tab = Build().Tab((int)DevWarpKind.Npc);
            Assert.AreEqual(new[] { "botchan", "inari" }, Ids(tab));

            DevWarpTarget inari = tab[1];
            Assert.AreEqual(new Vector3(3f, 0f, 3f - DevWarpCatalog.NpcStandOff), inari.Position);

            Assert.IsFalse(float.IsNaN(tab[0].Position.x) || float.IsNaN(tab[0].Yaw));
        }

        [Test]
        public void Npcs_InAnInteriorEnterTheNearestBuildingFirst()
        {
            // 寮長のように屋内の置き場（x ≥ InteriorMinX）にいる人へ外から飛ぶと、歩ける範囲の外として戻される。
            var interiors = new[]
            {
                new DevInteriorSource("lib", "図書館", new Vector3(1200f, 0f, 0f)),
                new DevInteriorSource("dorm", "寮", new Vector3(4000f, 0f, 0f)),
                new DevInteriorSource("lab", "実験棟")
            };
            var npcs = new[]
            {
                new DevNpcSource("dorm_head", "寮長", new Vector3(4010f, 0f, 5f), new Vector3(0f, 0f, 1f)),
                new DevNpcSource("librarian", "司書", new Vector3(1230f, 3f, 0f), new Vector3(0f, 0f, 1f)),
                new DevNpcSource("inari", "稲荷先輩", new Vector3(3f, 0f, 3f), new Vector3(0f, 0f, 1f))
            };

            IReadOnlyList<DevWarpTarget> tab =
                DevWarpCatalog.Build(Zones, Entrances, interiors, npcs).Tab((int)DevWarpKind.Npc);
            Assert.AreEqual(new[] { "dorm_head", "inari", "librarian" }, Ids(tab));

            DevWarpTarget head = tab[0];
            Assert.IsTrue(head.IsIndoor);
            Assert.AreEqual("dorm", head.InteriorId);
            Assert.IsFalse(head.HasPosition, "寮の入口は Entrances に無いので、今いる場所から入る");
            Assert.AreEqual(new Vector3(4010f, 0f, 5f - DevWarpCatalog.NpcStandOff), head.InsidePosition);

            DevWarpTarget librarian = tab[2];
            Assert.AreEqual("lib", librarian.InteriorId);
            Assert.IsTrue(librarian.HasPosition, "図書館の入口の前に立ってから入る");
            Assert.AreEqual(new Vector3(0f, 0f, DevWarpCatalog.EntranceStandOff), librarian.Position);

            Assert.IsFalse(tab[1].IsIndoor, "外にいる人は今までどおり座標へ飛ぶ");
            Assert.AreEqual(string.Empty, tab[1].InteriorId);
        }

        [Test]
        public void Npcs_InAnInteriorWithoutAnyKnownBuildingAreLeftOut()
        {
            var npcs = new[] { new DevNpcSource("dorm_head", "寮長", new Vector3(4010f, 0f, 5f), Vector3.forward) };

            Assert.AreEqual(0, DevWarpCatalog.Build(null, null, Interiors, npcs).Tab((int)DevWarpKind.Npc).Count,
                "入った直後の位置が分からない屋内しか無いと、どの建物か決められない");
        }

        [Test]
        public void Times_HaveMorningNoonEveningNight()
        {
            IReadOnlyList<DevWarpTarget> tab = Build().Tab((int)DevWarpKind.Time);
            Assert.AreEqual(new[] { "morning", "noon", "evening", "night" }, Ids(tab));
            Assert.AreEqual(new[] { 7f, 12f, 17.5f, 21f },
                new[] { tab[0].Hours, tab[1].Hours, tab[2].Hours, tab[3].Hours });
        }

        [Test]
        public void Count_IsTheSumOfEveryTab_AndEmptySourcesStillGiveTheTimes()
        {
            Assert.AreEqual(4 + 2 + 2 + 4, Build().Count);

            DevWarpCatalog empty = DevWarpCatalog.Build(null, null, null, null);
            Assert.AreEqual(DevWarpCatalog.Times.Length, empty.Count);
            Assert.AreEqual(4, DevWarpCatalog.TabCount);
        }

        [Test]
        public void Labels_AreNeverEmpty()
        {
            DevWarpCatalog catalog = DevWarpCatalog.Build(
                new[] { new DevZoneSource("z_unnamed", string.Empty, Vector3.zero) },
                new[] { new DevEntranceSource("b_unnamed", string.Empty, Vector3.zero, Vector3.zero) },
                new[] { new DevInteriorSource("b_unnamed", string.Empty) },
                new[] { new DevNpcSource("n_unnamed", string.Empty, Vector3.zero, Vector3.forward) });
            for (int tab = 0; tab < DevWarpCatalog.TabCount; tab++)
            {
                foreach (DevWarpTarget target in catalog.Tab(tab))
                {
                    Assert.IsNotEmpty(target.Label, target.Kind + "/" + target.Id);
                }
            }
        }

        [Test]
        public void Times_HaveALabelInEveryDictionary()
        {
            foreach (string folder in new[] { "Data/Localization", "Resources/KCD/Localization" })
            {
                foreach (string locale in new[] { "ja", "en" })
                {
                    string path = Path.Combine(Application.dataPath, folder, locale + ".json");
                    string json = File.ReadAllText(path);
                    foreach ((string key, string _, float _) in DevWarpCatalog.Times)
                    {
                        StringAssert.Contains("\"ui.dev.time." + key + "\"", json, path);
                    }
                }
            }
        }

        [Test]
        public void Compose_MarksTheSelectedRowAndShowsOnlyAWindow()
        {
            var zones = new List<DevZoneSource>();
            for (int i = 0; i < 30; i++)
            {
                zones.Add(new DevZoneSource("z" + i.ToString("D2"), "地点" + i.ToString("D2"), Vector3.zero));
            }

            DevWarpCatalog catalog = DevWarpCatalog.Build(zones, null, null, null);
            string text = DevWarpMenu.Compose(catalog, (int)DevWarpKind.Place, 20);

            StringAssert.Contains("▶ " + catalog.Tab((int)DevWarpKind.Place)[20].Label, text);
            StringAssert.DoesNotContain("地点00", text);
            Assert.AreEqual(DevWarpMenu.VisibleRows, CountOccurrences(text, "地点"), "見せる行の数");
        }

        [TestCase(30, 0, 0)]
        [TestCase(30, 29, 18)]
        [TestCase(30, 15, 9)]
        [TestCase(5, 4, 0)]
        [TestCase(0, 0, 0)]
        public void FirstVisibleRow_KeepsTheSelectionInsideTheWindow(int count, int selected, int expected)
        {
            Assert.AreEqual(expected, DevWarpMenu.FirstVisibleRow(count, selected));
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            for (int at = text.IndexOf(needle, System.StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
