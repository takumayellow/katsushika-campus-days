using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 第2研究棟 (#275): 玄関から入り、ロビーのエレベーターの前で「調べる」の候補に乗り場が出て、
    /// 行き先の画面から 3F〜6F の各階へ移れる。着いた階の乗り場だけが候補になり（真下の階の乗り場は拾わない）、
    /// 点いている明かりはその階のものだけ。1F へ戻って出口から外へ出られる。
    /// </summary>
    public sealed class Research2ElevatorPlayTests : SceneTestBase
    {
        private const string BuildingId = "research2";

        /// <summary>着いた高さの許容（m）。乗り場は床の上に置き、落ちて床に立つまで待つ。</summary>
        private const float FloorTolerance = 0.4f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Research2_ElevatorReachesEveryFloor_AndTheLobbyStillLeadsOut()
        {
            CollectErrorsInsteadOfLogAssert();
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

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Enter(BuildingId);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "第2研究棟の中へ入れていない");

            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + BuildingId);
            Assert.IsNotNull(root, "屋内の入れ物が無い");
            ElevatorPanel[] panels = root.GetComponentsInChildren<ElevatorPanel>(true);
            Assert.AreEqual(5, panels.Length, "乗り場は 1F・3F〜6F の 5 つ");
            ElevatorPanel.Stop[] stops = panels[0].Stops;
            CollectionAssert.AreEqual(new[] { "1F", "3F", "4F", "5F", "6F" }, Floors(stops), "止まる階");

            InteriorFloorLights lights = root.GetComponentInChildren<InteriorFloorLights>(true);
            Assert.IsNotNull(lights, "階ごとの明かりの切り替えが無い");
            InteractionPrompt prompt = player.GetComponentInChildren<InteractionPrompt>();
            Assert.IsNotNull(prompt, "プレイヤーに InteractionPrompt が無い");
            ElevatorMenu menu = ElevatorMenu.Instance;
            Assert.IsNotNull(menu, "エレベーターの画面が無い");

            var bad = new List<string>();
            int current = 0;
            yield return StandAtDoor(player, stops[0]);
            CheckFloor(player, prompt, lights, panels, stops, 0, bad);

            // 1F から上へ 1 階ずつ、最後に 6F から 1F へ。
            int[] route = { 1, 2, 3, 4, 0 };
            foreach (int target in route)
            {
                ElevatorPanel here = PanelOf(panels, stops[current].Floor);
                here.Interact(player.gameObject);
                Assert.IsTrue(menu.IsOpen, stops[current].Floor + " の乗り場で画面が開かない");
                Assert.AreEqual(current, menu.Index, "画面のカーソルが今の階から始まらない");

                // 画面の上下で行き先まで動かす（上の階ほど上の行）。
                int frame = Time.frameCount + 1;
                int guard = 0;
                while (menu.Index != target && guard++ < 10)
                {
                    menu.Tick(new ElevatorMenu.FrameInput(frame++, vertical: target > menu.Index ? -1 : 1));
                }

                Assert.AreEqual(target, menu.Index, stops[target].Floor + " の行にカーソルが届かない");
                menu.Tick(new ElevatorMenu.FrameInput(frame, submit: true));
                Assert.IsFalse(menu.IsOpen, "決めても画面が閉じない");
                yield return WaitMoved();

                current = target;
                yield return StandAtDoor(player, stops[current]);
                CheckFloor(player, prompt, lights, panels, stops, current, bad);
            }

            Assert.AreEqual(1f, Time.timeScale, "画面を閉じても時間が止まったまま");
            Assert.IsFalse(KCDInput.GameplayBlocked, "画面を閉じても操作が封鎖されたまま");

            yield return driver.Leave();
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.IsFalse(InteriorLoader.Instance.IsInside, "1F に戻っても外へ出られない");

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("第2研究棟のエレベーター");
        }

        private static string[] Floors(ElevatorPanel.Stop[] stops)
        {
            var floors = new string[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                floors[i] = stops[i].Floor;
            }

            return floors;
        }

        private static ElevatorPanel PanelOf(ElevatorPanel[] panels, string floor)
        {
            foreach (ElevatorPanel panel in panels)
            {
                if (panel.Floor == floor)
                {
                    return panel;
                }
            }

            Assert.Fail(floor + " の乗り場が無い");
            return null;
        }

        /// <summary>着いた位置から扉の方へ向き直る（着いたときは扉に背を向けている）。</summary>
        private static IEnumerator StandAtDoor(PlayerController player, ElevatorPanel.Stop stop)
        {
            Vector3 at = player.transform.position;
            player.Teleport(at, stop.Landing.eulerAngles.y + 180f);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitMoved()
        {
            InteriorLoader loader = InteriorLoader.Instance;
            float deadline = Time.realtimeSinceStartup + 5f;
            yield return null;
            while (loader.IsBusy && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static void CheckFloor(PlayerController player, InteractionPrompt prompt, InteriorFloorLights lights,
            ElevatorPanel[] panels, ElevatorPanel.Stop[] stops, int index, List<string> bad)
        {
            string floor = stops[index].Floor;
            float dy = player.transform.position.y - stops[index].Landing.position.y;
            if (Mathf.Abs(dy) > FloorTolerance)
            {
                bad.Add(floor + ": 乗り場の床から " + dy.ToString("F2") + " m の高さにいる");
            }

            if (prompt.Current != PanelOf(panels, floor))
            {
                string got = prompt.Current is ElevatorPanel other ? other.Floor + " の乗り場"
                    : prompt.Current != null ? prompt.Current.name : "なし";
                bad.Add(floor + ": 扉の前で「調べる」の候補が " + got);
            }

            if (lights.ActiveFloor != index)
            {
                bad.Add(floor + ": 点いている明かりの階が " + lights.ActiveFloor);
            }

            int lit = 0;
            foreach (Light light in lights.GetComponentsInChildren<Light>(true))
            {
                if (!light.enabled)
                {
                    continue;
                }

                lit++;
                int at = InteriorFloorLights.FloorOf(BottomsOf(stops), light.transform.position.y);
                if (at != index)
                {
                    bad.Add(floor + ": " + stops[at].Floor + " の明かり " + light.name + " が点いている");
                }
            }

            if (lit == 0)
            {
                bad.Add(floor + ": 明かりが 1 つも点いていない");
            }
        }

        private static float[] BottomsOf(ElevatorPanel.Stop[] stops)
        {
            var bottoms = new float[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                bottoms[i] = stops[i].Landing.position.y - 0.5f;
            }

            return bottoms;
        }
    }
}
