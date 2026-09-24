using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// セーブの形と読み方 (#61)。版番号の無い古いセーブを読めること、壊れた入力で例外を出さないこと、
    /// 読み込んだ値を使ってよい値へ寄せること、屋内でセーブしたものをどこへ立たせるか。
    /// </summary>
    public sealed class SaveDataTests
    {
        /// <summary>
        /// 版番号を持たない最初の形で、実際に書かれていた JSON（JsonUtility.ToJson(data, true) の出力と同じ並び）。
        /// </summary>
        private const string LegacyJson =
            "{\n" +
            "    \"CharacterId\": \"botchan\",\n" +
            "    \"TimeHours\": 14.5,\n" +
            "    \"PlayerX\": 12.0,\n" +
            "    \"PlayerY\": 0.5,\n" +
            "    \"PlayerZ\": -20.0,\n" +
            "    \"PlayerYaw\": 90.0,\n" +
            "    \"Quests\": {\n" +
            "        \"QuestIds\": [\"q_orientation\"],\n" +
            "        \"StepsDone\": [1],\n" +
            "        \"States\": [1]\n" +
            "    }\n" +
            "}";

        [TearDown]
        public void TearDown()
        {
            // DayStats は static でテストをまたいで残る。
            DayStats.Reset();
        }

        [Test]
        public void SaveData_RoundTripsThroughJsonUtility()
        {
            var data = new SaveData
            {
                CharacterId = "madonna",
                TimeHours = 17.25f,
                PlayerX = 1.5f,
                PlayerY = -2f,
                PlayerZ = 30.75f,
                PlayerYaw = 271f
            };
            data.Quests.QuestIds.Add("q_intro");
            data.Quests.StepsDone.Add(2);
            data.Quests.States.Add(1);

            SaveData restored = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));

            Assert.AreEqual("madonna", restored.CharacterId);
            Assert.AreEqual(17.25f, restored.TimeHours, 1e-6f);
            Assert.AreEqual(1.5f, restored.PlayerX, 1e-6f);
            Assert.AreEqual(-2f, restored.PlayerY, 1e-6f);
            Assert.AreEqual(30.75f, restored.PlayerZ, 1e-6f);
            Assert.AreEqual(271f, restored.PlayerYaw, 1e-6f);
            CollectionAssert.AreEqual(new[] { "q_intro" }, restored.Quests.QuestIds);
            CollectionAssert.AreEqual(new[] { 2 }, restored.Quests.StepsDone);
            CollectionAssert.AreEqual(new[] { 1 }, restored.Quests.States);
        }

        [Test]
        public void SaveData_DefaultsMatchNewGame()
        {
            SaveData restored = JsonUtility.FromJson<SaveData>("{}");

            Assert.AreEqual("mirai", restored.CharacterId);
            Assert.AreEqual(8.5f, restored.TimeHours, 1e-6f);
            Assert.AreEqual(DayRestart.FirstDay, restored.DayNumber);
            Assert.IsNotNull(restored.Quests);
            Assert.AreEqual(0, restored.Quests.QuestIds.Count);
        }

        [Test]
        public void SaveData_MissingVersionReadsAsLegacy()
        {
            // 版番号の既定値が 0 でないと、版番号の無い古いセーブを今の版と取り違える。
            Assert.AreEqual(0, JsonUtility.FromJson<SaveData>("{}").SaveVersion);
            Assert.LessOrEqual(0, SaveData.LegacyVersion);
            Assert.Greater(SaveData.CurrentVersion, SaveData.LegacyVersion);
        }

        // ---- 今の版の往復 ----

        [Test]
        public void Parse_CurrentVersion_RoundTripsEverything()
        {
            var data = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                CharacterId = "madonna",
                TimeHours = 18.75f,
                DayNumber = 3,
                HasPosition = true,
                PlayerX = 1250f,
                PlayerY = 0.25f,
                PlayerZ = 40f,
                PlayerYaw = 180f,
                InteriorId = "library",
                ReturnX = 10f,
                ReturnY = 0.5f,
                ReturnZ = -5f,
                ReturnYaw = 270f
            };
            data.Quests.QuestIds.Add("q_library");
            data.Quests.StepsDone.Add(1);
            data.Quests.States.Add(QuestProgress.StateCompleted);
            data.Buildings.Add("library");
            data.Collected.Add("item_card");
            data.PhotoSpots.Add("ps_gate");

            SaveData restored = SaveDataRules.Parse(SaveDataRules.ToJson(data));

            Assert.IsNotNull(restored);
            Assert.AreEqual(SaveData.CurrentVersion, restored.SaveVersion);
            Assert.AreEqual("madonna", restored.CharacterId);
            Assert.AreEqual(18.75f, restored.TimeHours, 1e-5f);
            Assert.AreEqual(3, restored.DayNumber);
            Assert.IsTrue(restored.HasPosition);
            Assert.AreEqual(new Vector3(1250f, 0.25f, 40f), restored.PlayerPosition);
            Assert.AreEqual(180f, restored.PlayerYaw, 1e-4f);
            Assert.AreEqual("library", restored.InteriorId);
            Assert.AreEqual(new Vector3(10f, 0.5f, -5f), restored.ReturnPosition);
            Assert.AreEqual(270f, restored.ReturnYaw, 1e-4f);
            CollectionAssert.AreEqual(new[] { "q_library" }, restored.Quests.QuestIds);
            CollectionAssert.AreEqual(new[] { QuestProgress.StateCompleted }, restored.Quests.States);
            CollectionAssert.AreEqual(new[] { "library" }, restored.Buildings);
            CollectionAssert.AreEqual(new[] { "item_card" }, restored.Collected);
            CollectionAssert.AreEqual(new[] { "ps_gate" }, restored.PhotoSpots);
        }

        // ---- 古いセーブ ----

        [Test]
        public void Parse_LegacySave_StillLoadsWithTheNewFieldsAtTheirDefaults()
        {
            SaveData data = SaveDataRules.Parse(LegacyJson);

            Assert.IsNotNull(data, "版番号の無いセーブが読めない");
            Assert.AreEqual(SaveData.CurrentVersion, data.SaveVersion, "読み込んだら今の版として扱う");
            Assert.AreEqual("botchan", data.CharacterId);
            Assert.AreEqual(14.5f, data.TimeHours, 1e-5f);
            Assert.AreEqual(DayRestart.FirstDay, data.DayNumber, "日数を持たないセーブは 1 日目");
            Assert.IsTrue(data.HasPosition, "キャンパスの中の位置は使う");
            Assert.AreEqual(new Vector3(12f, 0.5f, -20f), data.PlayerPosition);
            Assert.AreEqual(90f, data.PlayerYaw, 1e-4f);
            Assert.IsFalse(data.IsInside);
            CollectionAssert.AreEqual(new[] { "q_orientation" }, data.Quests.QuestIds);
            CollectionAssert.AreEqual(new[] { 1 }, data.Quests.StepsDone);
            CollectionAssert.AreEqual(new[] { 1 }, data.Quests.States);
            Assert.IsNotNull(data.Buildings);
            Assert.AreEqual(0, data.Buildings.Count);
            Assert.AreEqual(0, data.Collected.Count);
            Assert.AreEqual(0, data.PhotoSpots.Count);
            Assert.AreEqual(SavePlacement.Outside, SaveDataRules.Placement(data, false));
        }

        [Test]
        public void Parse_LegacySaveWrittenIndoors_StartsAtTheSpawn()
        {
            // 古い版は屋内でも位置だけ書いていた。屋内モデルは x ≥ 1200 m にあり、どの建物かは分からない。
            // その位置へワープすると WorldBounds が「キャンパスの外」と見て正門側へ戻すだけなので、最初から使わない。
            string json = LegacyJson.Replace("\"PlayerX\": 12.0", "\"PlayerX\": 1250.0");

            SaveData data = SaveDataRules.Parse(json);

            Assert.IsNotNull(data);
            Assert.IsFalse(data.HasPosition);
            Assert.IsFalse(data.IsInside);
            Assert.AreEqual(SavePlacement.Stay, SaveDataRules.Placement(data, true));
            Assert.AreEqual(14.5f, data.TimeHours, 1e-5f, "位置以外はそのまま読む");
        }

        // ---- 壊れた入力 ----

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   \n")]
        [TestCase("{")]
        [TestCase("{\"CharacterId\":")]
        [TestCase("this is not json")]
        [TestCase("[]")]
        [TestCase("[1,2,3]")]
        [TestCase("null")]
        [TestCase("42")]
        public void Parse_BrokenInput_ReturnsNullWithoutThrowing(string json)
        {
            SaveData data = null;
            Assert.DoesNotThrow(() => data = SaveDataRules.Parse(json));
            Assert.IsNull(data, "壊れたセーブは「セーブ無し」として扱う");
        }

        [Test]
        public void Sanitize_ReplacesValuesThatCannotBeUsed()
        {
            var broken = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                CharacterId = "nobody",
                TimeHours = float.NaN,
                DayNumber = -4,
                HasPosition = true,
                PlayerX = float.PositiveInfinity,
                PlayerYaw = float.NaN,
                InteriorId = null,
                Quests = null,
                Buildings = null,
                Collected = null,
                PhotoSpots = null
            };

            SaveData data = SaveDataRules.Sanitize(broken);

            Assert.AreEqual("mirai", data.CharacterId, "知らないキャラは既定のキャラ");
            Assert.AreEqual(DayRestart.DayStartHour, data.TimeHours, 1e-6f, "NaN の時刻は朝");
            Assert.AreEqual(DayRestart.FirstDay, data.DayNumber);
            Assert.IsFalse(data.HasPosition, "無限大の位置は使わない");
            Assert.AreEqual(0f, data.PlayerYaw, 1e-6f);
            Assert.AreEqual(string.Empty, data.InteriorId);
            Assert.IsNotNull(data.Quests);
            Assert.AreEqual(0, QuestProgress.RowCount(data.Quests));
            Assert.IsNotNull(data.Buildings);
            Assert.IsNotNull(data.Collected);
            Assert.IsNotNull(data.PhotoSpots);
        }

        [TestCase(25.5f, 1.5f)]
        [TestCase(-1f, 23f)]
        [TestCase(24f, 0f)]
        [TestCase(12f, 12f)]
        public void Sanitize_KeepsTheClockWithinOneDay(float saved, float expected)
        {
            SaveData data = SaveDataRules.Sanitize(new SaveData { TimeHours = saved });
            Assert.AreEqual(expected, data.TimeHours, 1e-4f);
        }

        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Sanitize_InfiniteClockGoesBackToMorning(float saved)
        {
            SaveData data = SaveDataRules.Sanitize(new SaveData { TimeHours = saved });
            Assert.AreEqual(DayRestart.DayStartHour, data.TimeHours, 1e-6f);
        }

        [Test]
        public void Sanitize_DoesNotTouchTheSource()
        {
            var source = new SaveData { CharacterId = "nobody", TimeHours = float.NaN, Buildings = null };

            SaveDataRules.Sanitize(source);

            Assert.AreEqual("nobody", source.CharacterId);
            Assert.IsTrue(float.IsNaN(source.TimeHours));
            Assert.IsNull(source.Buildings);
        }

        [Test]
        public void Sanitize_CleansTheQuestRows()
        {
            var source = new SaveData { SaveVersion = SaveData.CurrentVersion };
            source.Quests.QuestIds.AddRange(new[] { "q_a", "", "q_b", "q_c", "q_extra" });
            source.Quests.StepsDone.AddRange(new[] { -3, 1, 2, 0 });
            source.Quests.States.AddRange(new[] { 1, 1, 7, 2 });

            QuestProgress quests = SaveDataRules.Sanitize(source).Quests;

            // 列の長さが食い違えば短い方まで（q_extra は StepsDone / States が無いので読まない）。空の id は捨てる。
            CollectionAssert.AreEqual(new[] { "q_a", "q_b", "q_c" }, quests.QuestIds);
            CollectionAssert.AreEqual(new[] { 0, 2, 0 }, quests.StepsDone, "負のステップ数は 0");
            CollectionAssert.AreEqual(new[] { 1, QuestProgress.StateNotStarted, 2 }, quests.States, "知らない状態は未受注");
        }

        [Test]
        public void Sanitize_DropsEmptyAndRepeatedIdsFromTheDayRecord()
        {
            var source = new SaveData { SaveVersion = SaveData.CurrentVersion };
            source.Buildings.AddRange(new[] { "library", null, "", "library", "gym" });

            SaveData data = SaveDataRules.Sanitize(source);

            CollectionAssert.AreEqual(new[] { "library", "gym" }, data.Buildings);
        }

        [Test]
        public void Sanitize_OutsidePositionBeyondTheCampusIsNotUsed()
        {
            var source = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                HasPosition = true,
                PlayerX = WorldBounds.HalfExtent + 500f
            };

            Assert.IsFalse(SaveDataRules.Sanitize(source).HasPosition);
        }

        [Test]
        public void Sanitize_CurrentVersionWithoutPosition_StaysWithoutPosition()
        {
            // プレイヤーが見つからないまま書いたセーブ。0,0,0 へ飛ばさない。
            var source = new SaveData { SaveVersion = SaveData.CurrentVersion, HasPosition = false };

            Assert.IsFalse(SaveDataRules.Sanitize(source).HasPosition);
        }

        [Test]
        public void Sanitize_IndoorSaveWithBrokenReturnPoint_StartsAtTheSpawn()
        {
            var source = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                HasPosition = true,
                PlayerX = 1250f,
                InteriorId = "library",
                ReturnX = float.NaN
            };

            SaveData data = SaveDataRules.Sanitize(source);

            Assert.IsFalse(data.IsInside, "戻り先の無い屋内には戻さない");
            Assert.IsFalse(data.HasPosition, "屋内の位置は外では使えない");
            Assert.AreEqual(SavePlacement.Stay, SaveDataRules.Placement(data, true));
        }

        // ---- 立たせ方 ----

        [Test]
        public void Placement_IndoorSave_GoesBackInsideWhenTheInteriorExists()
        {
            SaveData data = IndoorSave();

            Assert.AreEqual(SavePlacement.Inside, SaveDataRules.Placement(data, true));
        }

        [Test]
        public void Placement_IndoorSave_WithoutTheInterior_StandsAtTheDoor()
        {
            // 屋内が組まれていない（fbx 未着など）シーンで読んだ。入口の手前（入ったときに覚えた戻り先）に立つ。
            SaveData data = IndoorSave();

            Assert.AreEqual(SavePlacement.ReturnPoint, SaveDataRules.Placement(data, false));
        }

        [Test]
        public void Placement_NoSave_StaysPut()
        {
            Assert.AreEqual(SavePlacement.Stay, SaveDataRules.Placement(null, true));
        }

        [Test]
        public void StandingAt_MovesThePlayerOutsideWithoutTouchingTheRest()
        {
            SaveData indoor = IndoorSave();
            indoor.DayNumber = 2;

            SaveData moved = SaveDataRules.StandingAt(indoor, new Vector3(3f, 0f, 4f), 45f);

            Assert.IsTrue(moved.HasPosition);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), moved.PlayerPosition);
            Assert.AreEqual(45f, moved.PlayerYaw, 1e-4f);
            Assert.IsFalse(moved.IsInside);
            Assert.AreEqual(2, moved.DayNumber);
            Assert.AreEqual("library", indoor.InteriorId, "元は変えない");
        }

        [Test]
        public void AtSpawn_WritesTheNextMorningAtTheSpawn_EvenFromIndoors()
        {
            // 一日の終わりに建物の中から「タイトルへ」を選んだ形。時計と日数は BeginNextDay で翌朝になっている。
            SaveData indoor = IndoorSave();
            indoor.DayNumber = 3;
            indoor.TimeHours = DayRestart.DayStartHour;

            SaveData saved = SaveDataRules.AtSpawn(indoor, true, new Vector3(5f, 0.1f, -60f), 180f);
            SaveData read = SaveDataRules.Parse(SaveDataRules.ToJson(saved));

            Assert.AreEqual(SavePlacement.Outside, SaveDataRules.Placement(read, true), "屋内の形が残っていても外のスポーンに立つ");
            Assert.AreEqual(new Vector3(5f, 0.1f, -60f), read.PlayerPosition);
            Assert.AreEqual(180f, read.PlayerYaw, 1e-4f);
            Assert.AreEqual(3, read.DayNumber);
            Assert.AreEqual(DayRestart.DayStartHour, read.TimeHours, 1e-4f);
            Assert.AreEqual("library", indoor.InteriorId, "元は変えない");
        }

        [Test]
        public void AtSpawn_WithoutAKnownSpawn_LeavesItToTheScene()
        {
            SaveData indoor = IndoorSave();

            SaveData saved = SaveDataRules.AtSpawn(indoor, false, Vector3.zero, 0f);
            SaveData read = SaveDataRules.Parse(SaveDataRules.ToJson(saved));

            Assert.IsFalse(read.HasPosition);
            Assert.IsFalse(read.IsInside);
            Assert.AreEqual(SavePlacement.Stay, SaveDataRules.Placement(read, true), "シーンに置かれたスポーンから始める");
        }

        [Test]
        public void AtSpawn_NoState_ReturnsNull()
        {
            Assert.IsNull(SaveDataRules.AtSpawn(null, true, Vector3.zero, 0f));
        }

        // ---- 一日の記録（DayStats）----

        [Test]
        public void DayStats_CaptureAndRestore_RoundTrip()
        {
            DayStats.NoteEnter("library");
            DayStats.NoteEnter("gym");
            DayStats.NoteCollect("item_card");
            DayStats.NotePhoto("ps_gate");

            List<string> buildings = DayStats.BuildingIds();
            List<string> collected = DayStats.CollectedIds();
            List<string> photos = DayStats.PhotoSpotIds();

            DayStats.Reset();
            DayStats.NoteEnter("cafeteria");

            DayStats.Restore(buildings, collected, photos);

            Assert.AreEqual(2, DayStats.BuildingCount);
            Assert.IsTrue(DayStats.HasEntered("library"));
            Assert.IsTrue(DayStats.HasEntered("gym"));
            Assert.IsFalse(DayStats.HasEntered("cafeteria"), "読み込む前の記録は捨てる");
            Assert.IsTrue(DayStats.HasCollected("item_card"));
            Assert.AreEqual(1, DayStats.PhotoSpotCount);
        }

        [Test]
        public void DayStats_Restore_ToleratesNullAndJunk()
        {
            DayStats.NoteEnter("library");

            Assert.DoesNotThrow(() => DayStats.Restore(null, new[] { "", null, "item_card" },
                new[] { "kcd_20260924_120000.png", "ps_gate" }));

            Assert.AreEqual(0, DayStats.BuildingCount);
            Assert.AreEqual(1, DayStats.CollectedCount);
            Assert.AreEqual(1, DayStats.PhotoSpotCount, "ps_ で始まらないものは写真スポットとして数えない");
        }

        [Test]
        public void DayStats_Ids_AreSortedSoTheSaveIsStable()
        {
            DayStats.NoteEnter("gym");
            DayStats.NoteEnter("library");
            DayStats.NoteEnter("cafeteria");

            CollectionAssert.AreEqual(new[] { "cafeteria", "gym", "library" }, DayStats.BuildingIds());
        }

        private static SaveData IndoorSave()
        {
            return SaveDataRules.Sanitize(new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                HasPosition = true,
                PlayerX = 1250f,
                PlayerZ = 40f,
                InteriorId = "library",
                ReturnX = 10f,
                ReturnZ = -5f,
                ReturnYaw = 270f
            });
        }
    }
}
