using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟 1F の西ロビーの学生食堂側のガラスの扉 2 か所（lecture_ws・lecture_wn）。
    /// 外の入口から入ると西ロビーの扉の内側に立ち、扉の出口を踏むとその扉の外へ建物に背を向けて戻る。
    /// 南の玄関から入って西の扉から出ると、その扉の外 2 m に建物へ背を向けて立つ。
    /// </summary>
    public sealed class LectureWestDoorPlayTests : SceneTestBase
    {
        private const string BuildingId = "lecture";
        private const string SouthWestDoor = "lecture_ws";
        private const string NorthWestDoor = "lecture_wn";

        /// <summary>入った所・出た所の水平の許容（m）。</summary>
        private const float Tolerance = 0.6f;

        /// <summary>南の玄関の内側と西ロビーの扉の内側の間の水平の距離の下限（m）。</summary>
        private const float DoorsApart = 20f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EnterWs_StandsInTheWestLobby_AndExitWsLeadsOutsideTheWsDoor()
        {
            yield return EnterAndLeaveThrough(SouthWestDoor);
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EnterWn_StandsInTheWestLobby_AndExitWnLeadsOutsideTheWnDoor()
        {
            yield return EnterAndLeaveThrough(NorthWestDoor);
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EnterSouth_ExitWn_LeadsOutsideTheWnDoor()
        {
            CollectErrorsInsteadOfLogAssert();
            CampusDriver driver = null;
            PlayerController player = null;
            yield return LoadCampus((d, p) => { driver = d; player = p; });

            yield return driver.Enter(BuildingId);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "南の玄関から講義棟へ入れていない");
            Transform southSpawn = FindInInterior("Spawn_" + BuildingId);
            Assert.Less(Flat(player.transform.position - southSpawn.position).magnitude, Tolerance,
                "南の玄関から入ったのに南の玄関の内側にいない");

            yield return driver.LeaveThrough(NorthWestDoor);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            EntranceTrigger wn = FindEntrance(NorthWestDoor);
            AssertOutsideDoor(player, NorthWestDoor, wn.transform.position + wn.Outward * InteriorLoader.OutsideStep);
            AssertNoErrors("講義棟の南から入り西の北の扉から出る");
        }

        private IEnumerator EnterAndLeaveThrough(string doorId)
        {
            CollectErrorsInsteadOfLogAssert();
            CampusDriver driver = null;
            PlayerController player = null;
            yield return LoadCampus((d, p) => { driver = d; player = p; });

            yield return driver.EnterThrough(doorId);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, doorId + " から講義棟へ入れていない");

            Transform spawn = FindInInterior("Spawn_" + doorId);
            Transform southSpawn = FindInInterior("Spawn_" + BuildingId);
            float atDoor = Flat(player.transform.position - spawn.position).magnitude;
            Assert.Less(atDoor, Tolerance, doorId + " から入ったのに西ロビーの扉の内側にいない（" + atDoor.ToString("F2") + " m）");
            Assert.Greater(Flat(player.transform.position - southSpawn.position).magnitude, DoorsApart,
                doorId + " から入ったのに南の玄関の内側にいる");

            // 入った扉から出るので、入ったときに覚えた戻り先（その扉の外）へ戻る。
            Vector3 returnTo = InteriorLoader.Instance.ReturnPosition;
            yield return driver.LeaveThrough(doorId);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            AssertOutsideDoor(player, doorId, returnTo);
            AssertNoErrors("講義棟の " + doorId + " から出入り");
        }

        private IEnumerator LoadCampus(System.Action<CampusDriver, PlayerController> found)
        {
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            found(new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>()), player);
        }

        /// <summary>外にいて、扉 doorId の正面の外（expected）に建物へ背を向けて立っている。</summary>
        private static void AssertOutsideDoor(PlayerController player, string doorId, Vector3 expected)
        {
            Assert.IsFalse(InteriorLoader.Instance.IsInside, doorId + " の出口を踏んでも外へ出ない");

            EntranceTrigger entrance = FindEntrance(doorId);
            Vector3 fromDoor = Flat(player.transform.position - entrance.transform.position);
            float outward = Vector3.Dot(fromDoor, entrance.Outward);
            float side = (fromDoor - entrance.Outward * outward).magnitude;
            Assert.IsTrue(outward >= InteriorLoader.OutsideStep - Tolerance && side < Tolerance,
                doorId + " の正面の外にいない（外へ " + outward.ToString("F2") + " m、横へ " + side.ToString("F2") + " m）");

            float off = Flat(player.transform.position - expected).magnitude;
            Assert.Less(off, Tolerance, "出た所がずれている（ずれ " + off.ToString("F2") + " m, いま "
                + player.transform.position.ToString("F2") + "）");

            Vector3 facing = Flat(player.transform.forward).normalized;
            Assert.Greater(Vector3.Dot(facing, entrance.Outward), 0.9f, "出たあと建物へ背を向けていない");
        }

        private static Transform FindInInterior(string name)
        {
            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + BuildingId);
            Assert.IsNotNull(root, "講義棟の屋内の入れ物が無い");
            foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
            {
                if (each.name == name)
                {
                    return each;
                }
            }

            Assert.Fail("講義棟の屋内に " + name + " が無い");
            return null;
        }

        private static EntranceTrigger FindEntrance(string doorId)
        {
            foreach (EntranceTrigger each in Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Include))
            {
                if (each.DoorId == doorId)
                {
                    return each;
                }
            }

            Assert.Fail("キャンパスに Entrance_" + doorId + " が無い");
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
