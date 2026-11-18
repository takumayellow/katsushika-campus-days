using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 階段室の扉で階を移る (#303)。FBX の st_&lt;id&gt;_&lt;階段&gt;_&lt;階&gt;（扉から <see cref="StairOut"/> 前の床）を
    /// 階段ごとに集め、扉の前に <see cref="ElevatorPanel"/>（<see cref="ElevatorPanel.Stairs"/>）を置く。
    /// 着いたときの向きは st_&lt;id&gt;_&lt;階段&gt;_&lt;階&gt;_f（扉から離れる向き）。&lt;階段&gt; に "_" は使わない。
    /// 別の建物の屋内へ続く階段（<see cref="Links"/>）は、屋内を全部置いたあとに <see cref="Link"/> で行き先をつなぐ。
    /// </summary>
    public static class InteriorStairStage
    {
        /// <summary>
        /// 屋内をまたいで続く階段。同じ名前の階段を、並べた建物の階を合わせた 1 本にする。
        /// 第2研究棟の西の端の階段は 1F・2F で食堂の中に出入口がある（docs/ref/interiors/research2/spec.md §1-8）。
        /// </summary>
        private static readonly (string Stair, string[] Buildings)[] Links =
        {
            ("west", new[] { "cafeteria", "research2" }),
        };

        /// <summary>扉の面から着いた所（Empty）まで（m）。blender/kcd_interior の STAIR_OUT と同じ。</summary>
        public const float StairOut = 1.0f;

        /// <summary>判定を Empty から扉の側へ寄せる距離（m）。</summary>
        public const float PanelSetback = 0.5f;

        /// <summary>判定の幅の半分（m）。扉 1 枚の前を覆う。</summary>
        public const float PanelHalfWidth = 0.6f;

        /// <summary>E に反応する距離（m）。扉の面の上の最も近い点から測る。</summary>
        public const float PanelRange = 2.0f;

        /// <summary>階段室の扉を置く。置いた扉の数を返す（無ければ 0）。</summary>
        public static int Build(Transform interior, string id)
        {
            string prefix = "st_" + id + "_";
            var stairs = new SortedSet<string>();
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith(prefix))
                {
                    continue;
                }

                int end = child.name.IndexOf('_', prefix.Length);
                if (end > prefix.Length)
                {
                    stairs.Add(child.name.Substring(prefix.Length, end - prefix.Length));
                }
            }

            if (stairs.Count == 0)
            {
                return 0;
            }

            var group = new GameObject("Stairs");
            group.transform.SetParent(interior, false);
            int placed = 0;
            var floors = new List<ElevatorPanel.Stop>();
            foreach (string stair in stairs)
            {
                List<ElevatorPanel.Stop> stops = InteriorElevatorStage.CollectStops(
                    interior, prefix + stair + "_", id, "StairLanding_" + stair + "_");
                if (stops.Count < 2)
                {
                    EditorPaths.Report("屋内 " + id + " の階段 " + stair + " は階が 1 つしかありません（行き先がない）。");
                    foreach (ElevatorPanel.Stop stop in stops)
                    {
                        Object.DestroyImmediate(stop.Landing.gameObject);
                    }

                    continue;
                }

                ElevatorPanel.Stop[] all = stops.ToArray();
                foreach (ElevatorPanel.Stop stop in all)
                {
                    stop.Landing.SetParent(group.transform, true);
                    AddPanel(group.transform, id, stair, stop, all);
                    placed++;
                }

                floors.AddRange(all);
            }

            AddFloorNames(interior, group.transform, id, floors);
            return placed;
        }

        /// <summary>
        /// 屋内をまたいで続く階段（<see cref="Links"/>）の行き先をつなぐ。InteriorStage が屋内を全部置いたあとに呼ぶ。
        /// 各建物の Stairs_&lt;階段&gt;_&lt;階&gt; の行き先を、つながる建物の階を合わせて下の階から並べたものにし、
        /// 階ごとに建物の id（<see cref="ElevatorPanel.Stop.BuildingId"/>）を付ける。屋内はどれも原点の高さが同じなので、
        /// 乗り場の高さで並べれば建物をまたいでも上下の順になる。つないだ階段の数を返す。
        /// </summary>
        public static int Link(Transform interiors)
        {
            int linked = 0;
            foreach ((string Stair, string[] Buildings) link in Links)
            {
                var panels = new List<ElevatorPanel>();
                var stops = new List<ElevatorPanel.Stop>();
                string missing = null;
                foreach (string id in link.Buildings)
                {
                    List<ElevatorPanel> own = PanelsOf(interiors, id, link.Stair);
                    if (own.Count == 0)
                    {
                        missing = id;
                        break;
                    }

                    panels.AddRange(own);
                    foreach (ElevatorPanel.Stop stop in own[0].Stops)
                    {
                        ElevatorPanel.Stop tagged = stop;
                        tagged.BuildingId = id;
                        stops.Add(tagged);
                    }
                }

                string names = string.Join("・", link.Buildings);
                if (missing != null)
                {
                    EditorPaths.Report("屋内 " + missing + " に階段 " + link.Stair + " が無いので、" + names + " をつなげません。");
                    continue;
                }

                stops.Sort((a, b) => a.Landing.position.y.CompareTo(b.Landing.position.y));
                ElevatorPanel.Stop[] all = stops.ToArray();
                foreach (ElevatorPanel panel in panels)
                {
                    panel.Stops = all;
                }

                EditorPaths.Report("階段 " + link.Stair + " で " + names + " をつなぎました（" + all.Length + " 階）。");
                linked++;
            }

            return linked;
        }

        /// <summary>屋内 id の階段室の扉のうち、階段 stair のもの。</summary>
        private static List<ElevatorPanel> PanelsOf(Transform interiors, string id, string stair)
        {
            var panels = new List<ElevatorPanel>();
            Transform interior = interiors.Find("Interior_" + id);
            if (interior == null)
            {
                return panels;
            }

            foreach (ElevatorPanel panel in interior.GetComponentsInChildren<ElevatorPanel>(true))
            {
                if (panel.Stairs && panel.name.StartsWith("Stairs_" + stair + "_"))
                {
                    panels.Add(panel);
                }
            }

            return panels;
        }

        /// <summary>
        /// エレベーターの無い屋内（食堂）でも、いる階をミニマップの見出しと InteriorLoader.CurrentFloor に出す。
        /// 階は階段の乗り場から取り、下端は乗り場の高さから <see cref="InteriorElevatorStage.FloorMargin"/> 下。
        /// 明かりは渡さないので点け消しは変えない。エレベーターが付けた InteriorFloorLights があれば何もしない。
        /// </summary>
        private static void AddFloorNames(Transform interior, Transform group, string id, List<ElevatorPanel.Stop> stops)
        {
            if (stops.Count == 0 || interior.GetComponentInChildren<InteriorFloorLights>(true) != null)
            {
                return;
            }

            var lowest = new Dictionary<string, float>();
            foreach (ElevatorPanel.Stop stop in stops)
            {
                float y = stop.Landing.position.y;
                lowest[stop.Floor] = lowest.TryGetValue(stop.Floor, out float seen) ? Mathf.Min(seen, y) : y;
            }

            var floors = new List<KeyValuePair<string, float>>(lowest);
            floors.Sort((a, b) => a.Value.CompareTo(b.Value));
            var names = new string[floors.Count];
            var bottoms = new float[floors.Count];
            for (int i = 0; i < floors.Count; i++)
            {
                names[i] = floors[i].Key;
                bottoms[i] = floors[i].Value - InteriorElevatorStage.FloorMargin;
            }

            InteriorFloorLights shown = group.gameObject.AddComponent<InteriorFloorLights>();
            shown.Configure(id, names, bottoms, new Light[0]);
        }

        private static void AddPanel(Transform group, string id, string stair, ElevatorPanel.Stop stop,
            ElevatorPanel.Stop[] all)
        {
            Transform landing = stop.Landing;
            var go = new GameObject("Stairs_" + stair + "_" + stop.Floor);
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(
                landing.position - landing.forward * PanelSetback + Vector3.up * 1.0f, landing.rotation);
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(PanelHalfWidth * 2f, 2f, 1.0f);

            ElevatorPanel panel = go.AddComponent<ElevatorPanel>();
            panel.BuildingId = id;
            panel.Floor = stop.Floor;
            panel.Stops = all;
            panel.Stairs = true;
            panel.InteractionRange = PanelRange;
            panel.SetDoor(StairOut - PanelSetback, PanelHalfWidth);
        }
    }
}
