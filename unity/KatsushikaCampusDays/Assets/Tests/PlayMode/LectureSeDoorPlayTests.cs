using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の南東の玄関。外の入口 Entrance_lecture_se から入ると南東の風除室に立ち、
    /// 風除室の出口 Exit_lecture_se を踏むと入った所（南東の玄関の外）へ建物に背を向けて戻る。
    /// 南の玄関から入って南東の出口から出ると、南東の玄関の外 2 m に建物へ背を向けて立つ。
    /// </summary>
    public sealed class LectureSeDoorPlayTests : SceneTestBase
    {
        private const string BuildingId = "lecture";
        private const string SeDoor = "lecture_se";

        /// <summary>入った所・出た所の水平の許容（m）。</summary>
        private const float Tolerance = 0.6f;

        /// <summary>南の玄関の内側と南東の風除室の間の水平の距離の下限（m）。</summary>
        private const float DoorsApart = 20f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EnterSe_StandsInTheVestibule_AndExitSeLeadsOutsideTheSeDoor()
        {
            CollectErrorsInsteadOfLogAssert();
            CampusDriver driver = null;
            PlayerController player = null;
            yield return LoadCampus((d, p) => { driver = d; player = p; });

            yield return driver.EnterThrough(SeDoor);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "南東の玄関から講義棟へ入れていない");

            Transform seSpawn = FindInInterior("Spawn_" + SeDoor);
            Transform southSpawn = FindInInterior("Spawn_" + BuildingId);
            float atSe = Flat(player.transform.position - seSpawn.position).magnitude;
            Assert.Less(atSe, Tolerance, "南東の玄関から入ったのに風除室の立ち位置にいない（" + atSe.ToString("F2") + " m）");
            Assert.Greater(Flat(player.transform.position - southSpawn.position).magnitude, DoorsApart,
                "南東の玄関から入ったのに南の玄関の内側にいる");

            // 入った扉から出るので、入ったときに覚えた戻り先（南東の玄関の外）へ戻る。
            Vector3 returnTo = InteriorLoader.Instance.ReturnPosition;
            yield return driver.LeaveThrough(SeDoor);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            AssertOutsideSeDoor(player, returnTo);
            AssertNoErrors("講義棟の南東の玄関から出入り");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EnterSouth_ExitSe_LeadsOutsideTheSeDoor()
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

            yield return driver.LeaveThrough(SeDoor);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            EntranceTrigger se = FindEntrance(SeDoor);
            AssertOutsideSeDoor(player, se.transform.position + se.Outward * InteriorLoader.OutsideStep);
            AssertNoErrors("講義棟の南から入り南東から出る");
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

        /// <summary>外にいて、南東の玄関の正面の外（expected）に建物へ背を向けて立っている。</summary>
        private static void AssertOutsideSeDoor(PlayerController player, Vector3 expected)
        {
            Assert.IsFalse(InteriorLoader.Instance.IsInside, "南東の出口を踏んでも外へ出ない");

            EntranceTrigger entrance = FindEntrance(SeDoor);
            Vector3 fromDoor = Flat(player.transform.position - entrance.transform.position);
            float outward = Vector3.Dot(fromDoor, entrance.Outward);
            float side = (fromDoor - entrance.Outward * outward).magnitude;
            Assert.IsTrue(outward >= InteriorLoader.OutsideStep - Tolerance && side < Tolerance,
                "南東の玄関の正面の外にいない（外へ " + outward.ToString("F2") + " m、横へ " + side.ToString("F2") + " m）");

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
