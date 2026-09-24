using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// モブの学生の時間割（Assets/Data/Mobs/schedule.json）を守る (#22)。
    ///
    /// 朝は門から建物へ 24 人、昼は食堂とモールに 24 人、午後は建物の間を 16 人、夕方は門へ 12 人、
    /// 夜はほとんど誰もいない（2 人）。8 時より前は誰も出さない。どの時間帯も同時に出す上限の 24 人を超えない。
    /// </summary>
    public sealed class MobScheduleTests
    {
        private static readonly string[] KnownBuildings =
        {
            "greenhouse", "gym", "kyoso", "lab1", "lab2", "lecture", "library", "research1", "research2"
        };

        private static readonly string[] KnownBodies = { "sora", "inari", "kaname", "prof", "mirai" };

        private static string DataPath => Path.Combine(Application.dataPath, "Data", "Mobs", "schedule.json");

        private static string ResourcesPath =>
            Path.Combine(Application.dataPath, "Resources", "KCD", "Mobs", "schedule.json");

        private static MobSchedule Load()
        {
            Assert.IsTrue(File.Exists(DataPath), DataPath + " が無い");
            MobSchedule schedule = MobSchedule.Parse(File.ReadAllText(DataPath));
            Assert.IsNotNull(schedule, DataPath + " を読めない");
            return schedule;
        }

        [TestCase(0f, 0)]
        [TestCase(7.99f, 0)]
        [TestCase(8f, 24)]
        [TestCase(10f, 24)]
        [TestCase(11.49f, 24)]
        [TestCase(11.5f, 24)]
        [TestCase(12.5f, 24)]
        [TestCase(13.5f, 16)]
        [TestCase(16.99f, 16)]
        [TestCase(17f, 12)]
        [TestCase(18.5f, 12)]
        [TestCase(19f, 2)]
        [TestCase(23.5f, 2)]
        [TestCase(24.5f, 0)]
        public void 時間帯ごとの人数(float hour, int expected)
        {
            Assert.AreEqual(expected, Load().CountAt(hour), hour + " 時の人数");
        }

        [TestCase(9f, "morning")]
        [TestCase(12f, "lunch")]
        [TestCase(15f, "afternoon")]
        [TestCase(18f, "evening")]
        [TestCase(21f, "night")]
        [TestCase(-1f, "night")]
        public void 時刻から時間帯を引ける(float hour, string expected)
        {
            MobSchedule.Band band = Load().BandAt(hour);
            Assert.IsNotNull(band, hour + " 時の時間帯が無い");
            Assert.AreEqual(expected, band.Id);
        }

        [Test]
        public void 夜は昼より少ない()
        {
            MobSchedule schedule = Load();
            Assert.LessOrEqual(schedule.CountAt(21f), 2, "夜はほとんど誰もいない");
            Assert.Less(schedule.CountAt(18f), schedule.CountAt(12f), "夕方は昼より少ない");
        }

        [Test]
        public void どの時間帯も同時に出す上限を超えない()
        {
            foreach (MobSchedule.Band band in Load().Bands)
            {
                Assert.LessOrEqual(band.Count, MobScheduler.MaxActive, band.Id + " の人数が上限を超える");
                Assert.AreEqual(
                    band.Count,
                    MobSchedule.ClampTarget(band.Count, MobScheduler.MaxActive, MobScheduler.MaxActive),
                    band.Id + " の人数が上限で切られる");
            }
        }

        [Test]
        public void 時間帯が重ならない()
        {
            IReadOnlyList<MobSchedule.Band> bands = Load().Bands;
            for (int i = 0; i < bands.Count; i++)
            {
                Assert.Less(bands[i].FromHour, bands[i].ToHour, bands[i].Id + " の始まりが終わり以降");
                for (int j = i + 1; j < bands.Count; j++)
                {
                    bool overlap = bands[i].FromHour < bands[j].ToHour && bands[j].FromHour < bands[i].ToHour;
                    Assert.IsFalse(overlap, bands[i].Id + " と " + bands[j].Id + " が重なる");
                }
            }
        }

        [Test]
        public void どの時間帯にも歩ける道がある()
        {
            foreach (MobSchedule.Band band in Load().Bands)
            {
                Assert.Greater(band.Routes.Count, 0, band.Id + " に道が無い");
                Assert.Greater(band.TotalWeight, 0f, band.Id + " の重みが 0");
                foreach (MobSchedule.Route route in band.Routes)
                {
                    Assert.GreaterOrEqual(route.Waypoints.Count, 2, band.Id + " に点が 1 つしかない道がある");
                }
            }
        }

        [Test]
        public void 入口はキャンパスの建物()
        {
            foreach (MobSchedule.Waypoint waypoint in Load().AllWaypoints())
            {
                if (waypoint.Kind == MobSchedule.KindEntrance)
                {
                    CollectionAssert.Contains(KnownBuildings, waypoint.Id, waypoint.Key + " は入口のある建物ではない");
                }
            }
        }

        [Test]
        public void 朝は門か駅から建物へ_夕方は門へ()
        {
            MobSchedule schedule = Load();
            foreach (MobSchedule.Route route in schedule.BandAt(9f).Routes)
            {
                MobSchedule.Waypoint last = route.Waypoints[route.Waypoints.Count - 1];
                Assert.AreEqual(MobSchedule.KindEntrance, last.Kind, "朝の道は建物の入口で終わる: " + last.Key);
            }

            foreach (MobSchedule.Route route in schedule.BandAt(18f).Routes)
            {
                MobSchedule.Waypoint last = route.Waypoints[route.Waypoints.Count - 1];
                Assert.AreEqual("place:gate_main", last.Key, "夕方の道は正門で終わる");
            }
        }

        [Test]
        public void 見た目の組み合わせは既存の体を使う()
        {
            MobSchedule schedule = Load();
            Assert.GreaterOrEqual(schedule.Variants.Count, 4, "見た目の組み合わせが少ない");

            var ids = new HashSet<string>();
            foreach (MobSchedule.Variant variant in schedule.Variants)
            {
                Assert.IsTrue(ids.Add(variant.Id), "見た目の id が重複: " + variant.Id);
                CollectionAssert.Contains(KnownBodies, variant.BodyId, variant.Id + " の体が無い");
                string fbx = Path.Combine(
                    Application.dataPath, "Models", "Characters", variant.BodyId, variant.BodyId + ".fbx");
                Assert.IsTrue(File.Exists(fbx), variant.Id + " の体の FBX が無い: " + fbx);
            }
        }

        [Test]
        public void 重みに比例して道を選ぶ()
        {
            MobSchedule.Band band = Load().BandAt(9f);
            Assert.AreSame(band.Routes[0], MobSchedule.PickRoute(band, 0f));
            Assert.AreSame(band.Routes[band.Routes.Count - 1], MobSchedule.PickRoute(band, 1f));

            float firstShare = band.Routes[0].Weight / band.TotalWeight;
            Assert.AreSame(band.Routes[0], MobSchedule.PickRoute(band, firstShare * 0.99f));
            Assert.AreNotSame(band.Routes[0], MobSchedule.PickRoute(band, firstShare * 1.01f));
            Assert.IsNull(MobSchedule.PickRoute(null, 0.5f));
        }

        [TestCase(30, 24, 24, 24)]
        [TestCase(12, 24, 24, 12)]
        [TestCase(12, 8, 24, 8)]
        [TestCase(-3, 24, 24, 0)]
        public void 人数を上限で切る(int count, int poolSize, int maxActive, int expected)
        {
            Assert.AreEqual(expected, MobSchedule.ClampTarget(count, poolSize, maxActive));
        }

        [Test]
        public void 座標の点のキーは書式によらない()
        {
            Assert.AreEqual("xz:81:-63.4", MobSchedule.KeyOf(MobSchedule.KindXz, null, 81f, -63.4f));
            Assert.AreEqual("entrance:lecture", MobSchedule.KeyOf(MobSchedule.KindEntrance, "lecture", 0f, 0f));
        }

        [Test]
        public void Resources_の写しが_Data_と同じ()
        {
            Assert.IsTrue(File.Exists(ResourcesPath), ResourcesPath + " が無い（DataBundler.SyncAll を通す）");
            Assert.AreEqual(File.ReadAllText(DataPath), File.ReadAllText(ResourcesPath), "Resources の schedule.json が古い");
        }

        [Test]
        public void 時間帯ごとの台詞が日本語と英語の両方にある()
        {
            foreach (string locale in new[] { "ja", "en" })
            {
                foreach (string root in new[] { "Data/Localization", "Resources/KCD/Localization" })
                {
                    string path = Path.Combine(Application.dataPath, root, locale + ".json");
                    var json = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                    Assert.IsNotNull(json, path + " を読めない");
                    Dictionary<string, object> strings = MiniJson.GetObject(json, "strings");
                    Assert.IsNotNull(strings, path + " に strings が無い");

                    AssertHasText(strings, "ui.mob.nod", path);
                    AssertHasText(strings, "ui.mob.say", path);
                    StringAssert.Contains("{0}", (string)strings["ui.mob.say"], path + " の ui.mob.say に {0} が無い");

                    foreach (MobSchedule.Band band in Load().Bands)
                    {
                        for (int i = 0; i < MobTalker.LinesPerBand; i++)
                        {
                            AssertHasText(strings, MobTalker.LineKey(band.Id, i), path);
                        }
                    }
                }
            }
        }

        private static void AssertHasText(Dictionary<string, object> strings, string key, string path)
        {
            Assert.IsTrue(strings.TryGetValue(key, out object value), path + " に " + key + " が無い");
            Assert.IsFalse(string.IsNullOrEmpty(value as string), path + " の " + key + " が空");
        }
    }
}
