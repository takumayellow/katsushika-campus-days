using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 1 棟に扉が 2 つ以上ある建物（講義棟の南東の玄関）。扉と建物の対応（BuildingDoors）が
    /// blender/kcd_lib/entrances.py の SECOND_DOORS と一致し、入口・出口の扉の id が建物の id へ戻ること、
    /// 入った扉と別の扉から出たときだけその扉の外へ出すこと（InteriorLoader.LeavesByOtherDoor）。
    /// </summary>
    public sealed class BuildingDoorsTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
        }

        // ---- 扉と建物の対応 ----

        [Test]
        public void LectureSeDoor_BelongsToTheLectureBuilding()
        {
            Assert.AreEqual("lecture", BuildingDoors.BuildingOf("lecture_se"));
            Assert.IsFalse(BuildingDoors.IsMainDoor("lecture_se"), "lecture_se が講義棟の玄関扱いになっている");
            CollectionAssert.AreEqual(new[] { "lecture_se" }, BuildingDoors.SecondDoorsOf("lecture"));
        }

        [Test]
        public void MainDoors_AreTheirOwnBuilding()
        {
            foreach (string id in new[] { "lecture", "library", "gym", "research2", "dorm" })
            {
                Assert.AreEqual(id, BuildingDoors.BuildingOf(id), id + " の玄関が別の建物に結ばれている");
                Assert.IsTrue(BuildingDoors.IsMainDoor(id), id + " が玄関でない");
            }

            CollectionAssert.IsEmpty(BuildingDoors.SecondDoorsOf("library"), "図書館に 2 つ目の扉がある");
        }

        [Test]
        public void SecondDoors_MatchBlenderEntrances()
        {
            string path = Path.Combine(RepoRoot, "blender", "kcd_lib", "entrances.py");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            Match table = Regex.Match(File.ReadAllText(path), @"^SECOND_DOORS\s*=\s*\{([^}]*)\}", RegexOptions.Multiline);
            Assert.IsTrue(table.Success, "entrances.py に SECOND_DOORS が無い");

            var blender = new Dictionary<string, string>();
            foreach (Match pair in Regex.Matches(table.Groups[1].Value, "\"(\\w+)\"\\s*:\\s*\"(\\w+)\""))
            {
                blender[pair.Groups[1].Value] = pair.Groups[2].Value;
            }

            var unity = new Dictionary<string, string>();
            foreach (string door in BuildingDoors.SecondDoorIds)
            {
                unity[door] = BuildingDoors.BuildingOf(door);
            }

            CollectionAssert.AreEquivalent(blender, unity, "BuildingDoors の表が entrances.py の SECOND_DOORS と違う");
        }

        // ---- 入口・出口の扉の id ----

        [Test]
        public void DoorId_DefaultsToTheBuilding()
        {
            var entrance = Make("Entrance_lecture").AddComponent<EntranceTrigger>();
            entrance.BuildingId = "lecture";
            Assert.AreEqual("lecture", entrance.DoorId, "扉を指定しない入口は玄関");
            entrance.DoorId = "lecture_se";
            Assert.AreEqual("lecture_se", entrance.DoorId);
            Assert.AreEqual("lecture", entrance.BuildingId, "扉を変えても建物は講義棟のまま");
            entrance.DoorId = "lecture";
            Assert.AreEqual("lecture", entrance.DoorId, "玄関に戻した入口が玄関でない");

            var exit = Make("Exit_lecture").AddComponent<InteriorExit>();
            exit.BuildingId = "lecture";
            Assert.AreEqual("lecture", exit.DoorId, "扉を指定しない出口は玄関");
            exit.DoorId = "lecture_se";
            Assert.AreEqual("lecture_se", exit.DoorId);
        }

        // ---- どの扉の外へ出るか ----

        private static readonly Vector3 SouthOutside = new Vector3(0f, 0f, -2f);
        private static readonly Vector3 SeOutside = new Vector3(45f, 0f, -14f);

        private static List<InteriorLoader.Door> LectureDoors()
        {
            return new List<InteriorLoader.Door>
            {
                new InteriorLoader.Door { DoorId = "lecture", HasOutside = true, OutsidePosition = SouthOutside, OutsideYaw = 180f },
                new InteriorLoader.Door { DoorId = "lecture_se", HasOutside = true, OutsidePosition = SeOutside, OutsideYaw = 180f },
            };
        }

        [Test]
        public void EnteredSouth_LeavingSe_GoesOutsideTheSeDoor()
        {
            Vector3 returned = SouthOutside + new Vector3(0.3f, 0f, -0.4f);
            Assert.IsTrue(InteriorLoader.LeavesByOtherDoor(LectureDoors(), "lecture_se", returned, out InteriorLoader.Door door));
            Assert.AreEqual("lecture_se", door.DoorId);
        }

        [Test]
        public void EnteredSe_LeavingSe_ReturnsWhereItEntered()
        {
            Vector3 returned = SeOutside + new Vector3(-0.5f, 0f, 0.2f);
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(LectureDoors(), "lecture_se", returned, out _),
                "入った扉から出たのに別の扉の外へ出す");
            Assert.IsTrue(InteriorLoader.LeavesByOtherDoor(LectureDoors(), "lecture", returned, out InteriorLoader.Door door),
                "南東から入って南から出たのに南の外へ出ない");
            Assert.AreEqual("lecture", door.DoorId);
        }

        [Test]
        public void SingleDoorOrUnknownOutside_ReturnsWhereItEntered()
        {
            var single = new List<InteriorLoader.Door> { LectureDoors()[0] };
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(single, "lecture", SeOutside, out _), "扉が 1 つの建物");
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(null, "lecture", SeOutside, out _), "扉の登録が無い建物");
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(LectureDoors(), null, SouthOutside, out _), "扉を省いた出口");
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(LectureDoors(), "gym", SouthOutside, out _), "登録に無い扉");

            List<InteriorLoader.Door> unknown = LectureDoors();
            unknown[1].HasOutside = false;
            Assert.IsFalse(InteriorLoader.LeavesByOtherDoor(unknown, "lecture_se", SouthOutside, out _),
                "外の所が分からない扉の外へ出している");
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }
    }
}
