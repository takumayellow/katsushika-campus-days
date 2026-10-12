using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内に入った所が出口の範囲から離れていること（#212）。少し戻っただけで外へ出されず、
    /// 扉へ歩けば今までどおり外へ出られる。
    /// </summary>
    public sealed class InteriorExitSpawnPlayTests : SceneTestBase
    {
        private static readonly string[] Buildings =
        {
            "dorm", "greenhouse", "gym", "kyoso", "lab1", "lab2", "lecture", "library", "research1", "research2",
        };

        /// <summary>入った所から出口の範囲の縁まで空けておく水平の距離（m）。プレイヤーの半径 + 余裕。</summary>
        private const float MinGap = 0.75f;

        private const float WalkSeconds = 4f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EveryInterior_SpawnIsClearOfTheExit_AndTheDoorStillExits()
        {
            CollectErrorsInsteadOfLogAssert();
            PlayerController player = null;
            yield return LoadCampus(p => player = p);
            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            var bad = new System.Collections.Generic.List<string>();

            foreach (string id in Buildings)
            {
                yield return driver.Enter(id);
                if (InteriorLoader.Instance.CurrentId != id)
                {
                    bad.Add(id + ": 屋内へ入れない");
                    continue;
                }

                InteriorExit exit = FindExit(id);
                if (exit == null)
                {
                    bad.Add(id + ": 出口が無い");
                    continue;
                }

                var box = exit.GetComponent<BoxCollider>();
                Vector3 spawn = player.transform.position;

                // (1) 入った所から出口の範囲の縁まで。
                Vector3 edge = box.ClosestPoint(new Vector3(spawn.x, box.bounds.center.y, spawn.z));
                float gap = Flat(edge - spawn).magnitude;
                if (gap < MinGap)
                {
                    bad.Add(id + ": 入った所から出口の範囲まで " + gap.ToString("F2") + " m（" + MinGap + " m 以上いる）");
                }

                // (2) 奥へ 1 m 歩いて戻っても外へ出ない。
                Vector3 inward = Flat(spawn - box.bounds.center).normalized;
                yield return Walk(player, spawn + inward);
                yield return Walk(player, spawn);
                if (!InteriorLoader.Instance.IsInside || InteriorLoader.Instance.CurrentId != id)
                {
                    bad.Add(id + ": 奥へ 1 m 歩いて戻ると外へ出た");
                    continue;
                }

                // (3) 扉へ歩けば外へ出る。出口の箱の手前の縁から少し入った所を目指す。
                Vector3 door = exit.transform.TransformPoint(new Vector3(0f, 0f, -box.size.z * 0.5f + 0.4f));
                door.y = spawn.y;
                yield return Walk(player, door);
                float deadline = Time.realtimeSinceStartup + 6f;
                while (InteriorLoader.Instance.IsInside && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (InteriorLoader.Instance.IsInside)
                {
                    bad.Add(id + ": 扉へ歩いても外へ出ない（いま " + player.transform.position.ToString("F2") + "）");
                }

                yield return driver.Settle();
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("屋内の出口");
        }

        private IEnumerator LoadCampus(System.Action<PlayerController> found)
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

            found(player);
        }

        private static IEnumerator Walk(PlayerController player, Vector3 target)
        {
            player.SetAutopilot(target, player.RunSpeed);
            float deadline = Time.realtimeSinceStartup + WalkSeconds;
            while (player.AutopilotActive && Time.realtimeSinceStartup < deadline && InteriorLoader.Instance.IsInside)
            {
                yield return null;
            }

            player.ClearAutopilot();
        }

        private static InteriorExit FindExit(string id)
        {
            foreach (InteriorExit exit in Object.FindObjectsByType<InteriorExit>())
            {
                if (exit.BuildingId == id)
                {
                    return exit;
                }
            }

            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}
