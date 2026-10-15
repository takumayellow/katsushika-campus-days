using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 階段室の扉で階を移る (#303)。FBX の st_&lt;id&gt;_&lt;階段&gt;_&lt;階&gt;（扉から <see cref="StairOut"/> 前の床）を
    /// 階段ごとに集め、扉の前に <see cref="ElevatorPanel"/>（<see cref="ElevatorPanel.Stairs"/>）を置く。
    /// 着いたときの向きは st_&lt;id&gt;_&lt;階段&gt;_&lt;階&gt;_f（扉から離れる向き）。&lt;階段&gt; に "_" は使わない。
    /// </summary>
    public static class InteriorStairStage
    {
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
            }

            return placed;
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
