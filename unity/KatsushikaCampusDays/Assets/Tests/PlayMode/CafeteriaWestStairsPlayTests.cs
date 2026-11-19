using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 第2研究棟の西の端の階段は食堂の 1F・2F にも下りる (#303, docs/ref/interiors/research2/spec.md §1-8)。
    /// 研究棟 4F の西の端の扉から食堂 2F・食堂 1F へ下り、食堂 1F から研究棟 6F へ上がって、また食堂 1F へ下りる。
    /// 着くたびに、いる建物・立つ位置と高さ・見出しの階・「調べる」の候補・外へ出たときの戻り先（着いた建物の玄関の外）を確かめ、
    /// 行き先の画面では別の建物の階に建物の名前が付く。最後に食堂の玄関から外へ出て、食堂の玄関の外に立つ。
    /// </summary>
    public sealed class CafeteriaWestStairsPlayTests : SceneTestBase
    {
        private const string Labs = "research2";
        private const string Dining = "cafeteria";
        private const string WestPrefix = "Stairs_west_";

        /// <summary>着いた高さの許容（m）。乗り場は床の上に置き、落ちて床に立つまで待つ。</summary>
        private const float FloorTolerance = 0.4f;

        /// <summary>着いた位置と乗り場の水平の距離の許容（m）。</summary>
        private const float LandingTolerance = 0.6f;

        /// <summary>出たときの戻り先と、玄関の外の所の水平の距離の許容（m）。どちらも同じ式で求めた点。</summary>
        private const float ReturnTolerance = 0.05f;

        /// <summary>外へ出て立った位置と、玄関の外の所の水平の距離の許容（m）。</summary>
        private const float OutsideTolerance = 1.0f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator WestStairs_JoinTheLabsAndTheCafeteria_AndTheCafeteriaLeadsOutItsOwnDoor()
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
            yield return driver.Enter(Labs);
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            InteriorLoader loader = InteriorLoader.Instance;
            Assert.AreEqual(Labs, loader.CurrentId, "第2研究棟の中へ入れていない");
            Assert.IsFalse(DayStats.HasEntered(Dining), "新しいゲームなのに食堂に入ったことになっている");

            ElevatorPanel[] labs = WestDoorsOf(Labs);
            ElevatorPanel[] dining = WestDoorsOf(Dining);
            Assert.AreEqual(4, labs.Length, "研究棟の西の端の階段室の扉は 3F〜6F の 4 つ");
            Assert.AreEqual(2, dining.Length, "食堂の西の端の階段室の扉は 1F・2F の 2 つ");
            var all = new List<ElevatorPanel>(labs);
            all.AddRange(dining);
            foreach (ElevatorPanel door in all)
            {
                string where = door.BuildingId + " の " + door.name;
                CollectionAssert.AreEqual(new[] { "1F", "2F", "3F", "4F", "5F", "6F" }, Floors(door.Stops),
                    where + " の行き先の階");
                CollectionAssert.AreEqual(new[] { Dining, Dining, Labs, Labs, Labs, Labs },
                    BuildingsOf(door.Stops, door.BuildingId), where + " の行き先の建物");
            }

            InteractionPrompt prompt = player.GetComponentInChildren<InteractionPrompt>();
            Assert.IsNotNull(prompt, "プレイヤーに InteractionPrompt が無い");
            ElevatorMenu menu = ElevatorMenu.Instance;
            Assert.IsNotNull(menu, "階を選ぶ画面が無い");
            InteriorFloorLights labLights = InteriorOf(Labs).GetComponentInChildren<InteriorFloorLights>(true);
            Assert.IsNotNull(labLights, "研究棟に階ごとの明かりの切り替えが無い");
            string[] labLightFloors = Floors(PanelsOf(Labs, "Elevator_")[0].Stops);

            var bad = new List<string>();
            // 玄関から 4F の西の端の扉の前まで（廊下の床は平らなので、歩く代わりに移す）。
            ElevatorPanel.Stop[] stops = labs[0].Stops;
            player.Teleport(stops[3].Landing.position + Vector3.up * 0.15f, stops[3].Landing.eulerAngles.y);
            yield return StandAtDoor(player, stops[3]);
            ElevatorPanel from = PanelOf(labs, "4F");

            // 研究棟 4F → 食堂 2F → 食堂 1F → 研究棟 6F → 食堂 1F。
            foreach (int target in new[] { 1, 0, 5, 0 })
            {
                string to = ElevatorPanel.BuildingOf(stops[target], from.BuildingId);
                ElevatorPanel arrived = PanelOf(to == Dining ? dining : labs, stops[target].Floor);
                yield return Ride(player, menu, from, target, prompt, arrived, bad);
                if (to == Labs)
                {
                    // 食堂から戻ったら、研究棟の明かりは着いた階のものになる。
                    int lit = System.Array.IndexOf(labLightFloors, stops[target].Floor);
                    if (labLights.ActiveFloor != lit)
                    {
                        bad.Add("食堂 → 研究棟 " + stops[target].Floor + ": 点いている明かりの階が " + labLights.ActiveFloor);
                    }
                }

                from = arrived;
            }

            Assert.IsTrue(DayStats.HasEntered(Dining), "階段で食堂に下りても、食堂に入ったことが 1 日の記録に残らない");
            Assert.AreEqual(1f, Time.timeScale, "画面を閉じても時間が止まったまま");
            Assert.IsFalse(KCDInput.GameplayBlocked, "画面を閉じても操作が封鎖されたまま");

            // 食堂の玄関から外へ。研究棟から階段で来ても、出るのは食堂の玄関の外。
            yield return driver.Leave();
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.IsFalse(loader.IsInside, "食堂の出口を踏んでも外へ出ない");
            Vector3 stand = Flat(player.transform.position);
            float toDining = Vector3.Distance(stand, Flat(OutsideOf(Dining)));
            float toLabs = Vector3.Distance(stand, Flat(OutsideOf(Labs)));
            Assert.Less(toDining, OutsideTolerance,
                "食堂から出たのに食堂の玄関の外に立っていない（研究棟の玄関の外まで " + toLabs.ToString("F1") + " m）");
            Assert.Less(toDining, toLabs, "食堂から出たのに研究棟の玄関の外のほうが近い");

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("第2研究棟の西の端の階段と食堂");
        }

        /// <summary>
        /// 階段室の扉 from から target 番目の階へ移る。画面に出る行き先の行、着いた建物・位置・高さ・見出しの階・
        /// 「調べる」の候補（arrived）・出たときの戻り先を確かめる。
        /// </summary>
        private static IEnumerator Ride(PlayerController player, ElevatorMenu menu, ElevatorPanel from, int target,
            InteractionPrompt prompt, ElevatorPanel arrived, List<string> bad)
        {
            InteriorLoader loader = InteriorLoader.Instance;
            ElevatorPanel.Stop[] stops = from.Stops;
            ElevatorPanel.Stop stop = stops[target];
            string to = ElevatorPanel.BuildingOf(stop, from.BuildingId);
            string trip = from.BuildingId + " " + from.Floor + " → " + to + " " + stop.Floor;
            if (prompt.Current != from)
            {
                bad.Add(trip + ": 扉の前にいるのに「調べる」の候補が " + NameOf(prompt.Current));
            }

            from.Interact(player.gameObject);
            Assert.IsTrue(menu.IsOpen, from.BuildingId + " の " + from.name + " で画面が開かない");
            Assert.AreEqual(from.CurrentIndex, menu.Index, trip + ": 画面のカーソルが今の階から始まらない");
            int frame = Time.frameCount + 1;
            int guard = 0;
            while (menu.Index != target && guard++ < 10)
            {
                menu.Tick(new ElevatorMenu.FrameInput(frame++, vertical: target > menu.Index ? -1 : 1));
            }

            Assert.AreEqual(target, menu.Index, trip + ": 行き先の行にカーソルが届かない");
            string caption = ElevatorMenu.Caption(stop, from.BuildingId);
            string name = L.Get("ui.building." + to, to);
            if (to != from.BuildingId && !caption.StartsWith(name))
            {
                bad.Add(trip + ": 別の建物の階なのに行き先に建物の名前が無い（「" + caption + "」）");
            }

            string row = stop.Floor + "  " + caption;
            if (!Shows(row))
            {
                bad.Add(trip + ": 行き先の画面に「" + row + "」の行が無い");
            }

            menu.Tick(new ElevatorMenu.FrameInput(frame, submit: true));
            Assert.IsFalse(menu.IsOpen, trip + ": 決めても画面が閉じない");
            yield return WaitMoved();
            yield return StandAtDoor(player, stop);

            if (!loader.IsInside || loader.CurrentId != to)
            {
                bad.Add(trip + ": いる建物が「" + loader.CurrentId + "」（中にいる: " + loader.IsInside + "）");
            }

            Vector3 offset = player.transform.position - stop.Landing.position;
            if (Flat(offset).magnitude > LandingTolerance)
            {
                bad.Add(trip + ": 乗り場から水平に " + Flat(offset).magnitude.ToString("F2") + " m 離れて立つ");
            }

            if (Mathf.Abs(offset.y) > FloorTolerance)
            {
                bad.Add(trip + ": 扉の前の床から " + offset.y.ToString("F2") + " m の高さにいる");
            }

            if (loader.CurrentFloor != stop.Floor)
            {
                bad.Add(trip + ": ミニマップの見出しの階が「" + loader.CurrentFloor + "」");
            }

            float off = Vector3.Distance(Flat(loader.ReturnPosition), Flat(OutsideOf(to)));
            if (off > ReturnTolerance)
            {
                bad.Add(trip + ": 出たときの戻り先が " + to + " の玄関の外から " + off.ToString("F2") + " m ずれる");
            }

            if (prompt.Current != arrived)
            {
                bad.Add(trip + ": 着いた扉の前で「調べる」の候補が " + NameOf(prompt.Current));
            }
        }

        /// <summary>画面に出ている文字のどこかに row の行があるか。</summary>
        private static bool Shows(string row)
        {
            foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude))
            {
                if (text.isActiveAndEnabled && text.text != null && text.text.Contains(row))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>建物 id の玄関の外の所（Entrance_&lt;id&gt; の正面へ InteriorLoader.OutsideStep）。</summary>
        private static Vector3 OutsideOf(string id)
        {
            GameObject entrance = GameObject.Find("Entrance_" + id);
            Assert.IsNotNull(entrance, "キャンパスに Entrance_" + id + " が無い");
            Vector3 outward = Flat(entrance.transform.forward).normalized;
            return entrance.transform.position + outward * InteriorLoader.OutsideStep;
        }

        private static Transform InteriorOf(string id)
        {
            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + id);
            Assert.IsNotNull(root, "屋内 " + id + " の入れ物が無い");
            return root.transform;
        }

        private static ElevatorPanel[] WestDoorsOf(string id)
        {
            return PanelsOf(id, WestPrefix);
        }

        private static ElevatorPanel[] PanelsOf(string id, string prefix)
        {
            var picked = new List<ElevatorPanel>();
            foreach (ElevatorPanel panel in InteriorOf(id).GetComponentsInChildren<ElevatorPanel>(true))
            {
                if (panel.name.StartsWith(prefix))
                {
                    picked.Add(panel);
                }
            }

            return picked.ToArray();
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

            Assert.Fail(floor + " の扉が無い");
            return null;
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

        private static string[] BuildingsOf(ElevatorPanel.Stop[] stops, string here)
        {
            var buildings = new string[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                buildings[i] = ElevatorPanel.BuildingOf(stops[i], here);
            }

            return buildings;
        }

        private static string NameOf(Interactable target)
        {
            return target == null ? "なし"
                : target is ElevatorPanel panel ? panel.BuildingId + " の " + panel.name
                : target.name;
        }

        /// <summary>着いた位置から扉の方へ向き直る（着いたときは扉に背を向けている）。</summary>
        private static IEnumerator StandAtDoor(PlayerController player, ElevatorPanel.Stop stop)
        {
            player.Teleport(player.transform.position, stop.Landing.eulerAngles.y + 180f);
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

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
