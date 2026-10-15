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
    /// 点いている明かりはその階のものだけで、ミニマップの見出しの階も合う。扉のすぐ前まで寄っても乗れる (#303)。
    /// 階段室の扉 2 か所（EV ホールの北 = ロビーの階段室とつながる、廊下の西の端）でも階を移れる (#303)。
    /// 1F へ戻って出口から外へ出られる。
    /// </summary>
    public sealed class Research2ElevatorPlayTests : SceneTestBase
    {
        private const string BuildingId = "research2";

        /// <summary>扉のすぐ前に寄って立つ位置の、扉の面からの距離（m）。乗り場の Empty は扉から 1.4 m 前。</summary>
        private const float CloseToDoor = 0.35f;

        /// <summary>着いた高さの許容（m）。乗り場は床の上に置き、落ちて床に立つまで待つ。</summary>
        private const float FloorTolerance = 0.4f;

        /// <summary>
        /// 看板などの Empty の上に吊った明かりの、吊り元からの高さ（InteriorStage.AnchorLift。Editor のアセンブリなので
        /// ここからは参照できない）。lamp_ の明かりは自分の高さ、それ以外はここだけ下げた高さで階を決める。
        /// </summary>
        private const float AnchorLift = 3.2f;

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
            ElevatorPanel[] panels = Of(root.GetComponentsInChildren<ElevatorPanel>(true), "Elevator_");
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
            // 玄関から 1F の乗り場まで（ロビーの床は平らなので、歩く代わりに移す）。
            int current = 0;
            player.Teleport(stops[0].Landing.position + Vector3.up * 0.15f, stops[0].Landing.eulerAngles.y);
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
                yield return StandCloseToDoor(player, stops[current], 1.4f);
                CheckPrompt(prompt, panels, stops[current].Floor, "扉のすぐ前", bad);
                if (current == stops.Length - 1)
                {
                    // 6F でセーブを読んだときと同じ順（中にいる扱いに戻してから立たせる）でも見出しに階が付く。
                    InteriorLoader loader = InteriorLoader.Instance;
                    loader.RestoreInside(BuildingId, loader.ReturnPosition, loader.ReturnYaw);
                    yield return StandAtDoor(player, stops[current]);
                    CheckFloor(player, prompt, lights, panels, stops, current, bad);
                }
            }

            // 階段: 1F のロビーのガラスの扉 → 4F（east）、4F の西の端 → 6F → 3F（west）、3F の EV ホールの北 → 1F（east）。
            ElevatorPanel[] east = Of(root.GetComponentsInChildren<ElevatorPanel>(true), "Stairs_east_");
            ElevatorPanel[] west = Of(root.GetComponentsInChildren<ElevatorPanel>(true), "Stairs_west_");
            Assert.AreEqual(5, east.Length, "EV ホールの北の階段室の扉は 1F・3F〜6F の 5 つ");
            Assert.AreEqual(4, west.Length, "廊下の西の端の階段室の扉は 3F〜6F の 4 つ");
            CollectionAssert.AreEqual(new[] { "1F", "3F", "4F", "5F", "6F" }, Floors(east[0].Stops), "east の階");
            CollectionAssert.AreEqual(new[] { "3F", "4F", "5F", "6F" }, Floors(west[0].Stops), "west の階");
            yield return Walk(player, east[0].Stops, 0, prompt, east, bad);
            yield return Ride(player, menu, PanelOf(east, "1F"), 2, prompt, east, bad);
            yield return Walk(player, west[0].Stops, 1, prompt, west, bad);
            yield return Ride(player, menu, PanelOf(west, "4F"), 3, prompt, west, bad);
            yield return Ride(player, menu, PanelOf(west, "6F"), 0, prompt, west, bad);
            yield return Walk(player, east[0].Stops, 1, prompt, east, bad);
            yield return Ride(player, menu, PanelOf(east, "3F"), 0, prompt, east, bad);

            // 1F の EV の乗り場の前へ戻る（玄関へ出る道は CampusDriver が歩く）。
            player.Teleport(stops[0].Landing.position + Vector3.up * 0.15f, stops[0].Landing.eulerAngles.y);
            yield return StandAtDoor(player, stops[0]);

            Assert.AreEqual(1f, Time.timeScale, "画面を閉じても時間が止まったまま");
            Assert.IsFalse(KCDInput.GameplayBlocked, "画面を閉じても操作が封鎖されたまま");

            yield return driver.Leave();
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.IsFalse(InteriorLoader.Instance.IsInside, "1F に戻っても外へ出られない");

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("第2研究棟のエレベーター");
        }

        private static ElevatorPanel[] Of(ElevatorPanel[] all, string prefix)
        {
            var picked = new List<ElevatorPanel>();
            foreach (ElevatorPanel panel in all)
            {
                if (panel.name.StartsWith(prefix))
                {
                    picked.Add(panel);
                }
            }

            return picked.ToArray();
        }

        /// <summary>同じ階の中を、i 番目の階段室の扉の前まで移り（床は平らなので歩く代わりに移す）、扉の方を向く。</summary>
        private static IEnumerator Walk(PlayerController player, ElevatorPanel.Stop[] stops, int i,
            InteractionPrompt prompt, ElevatorPanel[] doors, List<string> bad)
        {
            player.Teleport(stops[i].Landing.position + Vector3.up * 0.15f, stops[i].Landing.eulerAngles.y);
            yield return StandAtDoor(player, stops[i]);
            CheckPrompt(prompt, doors, stops[i].Floor, "階段室の扉の前", bad);
        }

        /// <summary>階段室の扉 from から target 番目の階へ移り、着いた扉の前で候補・高さ・見出しの階を確かめる。</summary>
        private static IEnumerator Ride(PlayerController player, ElevatorMenu menu, ElevatorPanel from, int target,
            InteractionPrompt prompt, ElevatorPanel[] doors, List<string> bad)
        {
            ElevatorPanel.Stop[] stops = from.Stops;
            if (prompt.Current != from)
            {
                bad.Add(from.name + ": 扉の前にいるのに「調べる」の候補でない");
            }

            from.Interact(player.gameObject);
            Assert.IsTrue(menu.IsOpen, from.name + " で画面が開かない");
            StringAssert.Contains(L.Pick("階段", "Stairs"),
                ElevatorMenu.Compose(stops, from.CurrentIndex, target, false, from.Stairs));
            int frame = Time.frameCount + 1;
            int guard = 0;
            while (menu.Index != target && guard++ < 10)
            {
                menu.Tick(new ElevatorMenu.FrameInput(frame++, vertical: target > menu.Index ? -1 : 1));
            }

            Assert.AreEqual(target, menu.Index, from.name + " から " + stops[target].Floor + " の行にカーソルが届かない");
            menu.Tick(new ElevatorMenu.FrameInput(frame, submit: true));
            Assert.IsFalse(menu.IsOpen, from.name + " で決めても画面が閉じない");
            yield return WaitMoved();
            yield return StandAtDoor(player, stops[target]);

            string floor = stops[target].Floor;
            float dy = player.transform.position.y - stops[target].Landing.position.y;
            if (Mathf.Abs(dy) > FloorTolerance)
            {
                bad.Add(from.name + " → " + floor + ": 扉の前の床から " + dy.ToString("F2") + " m の高さにいる");
            }

            if (InteriorLoader.Instance.CurrentFloor != floor)
            {
                bad.Add(from.name + " → " + floor + ": ミニマップの見出しの階が「" + InteriorLoader.Instance.CurrentFloor + "」");
            }

            CheckPrompt(prompt, doors, floor, from.name + " から着いた扉の前", bad);
        }

        private static void CheckPrompt(InteractionPrompt prompt, ElevatorPanel[] panels, string floor, string where,
            List<string> bad)
        {
            if (prompt.Current != PanelOf(panels, floor))
            {
                string got = prompt.Current != null ? prompt.Current.name : "なし";
                bad.Add(floor + ": " + where + "で「調べる」の候補が " + got);
            }
        }

        /// <summary>扉の面から <see cref="CloseToDoor"/> まで寄って、扉の方を向く。landingOut は乗り場から扉の面まで（m）。</summary>
        private static IEnumerator StandCloseToDoor(PlayerController player, ElevatorPanel.Stop stop, float landingOut)
        {
            Vector3 at = stop.Landing.position - stop.Landing.forward * (landingOut - CloseToDoor);
            player.Teleport(at + Vector3.up * 0.15f, stop.Landing.eulerAngles.y + 180f);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
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

            if (InteriorLoader.Instance.CurrentFloor != floor)
            {
                bad.Add(floor + ": ミニマップの見出しの階が「" + InteriorLoader.Instance.CurrentFloor + "」");
            }

            int lit = 0;
            foreach (Light light in lights.GetComponentsInChildren<Light>(true))
            {
                if (!light.enabled)
                {
                    continue;
                }

                lit++;
                int at = InteriorFloorLights.FloorOf(BottomsOf(stops), LevelOf(light));
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

        private static float LevelOf(Light light)
        {
            float y = light.transform.position.y;
            return light.name.StartsWith("Light_lamp_" + BuildingId + "_") ? y : y - AnchorLift;
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
