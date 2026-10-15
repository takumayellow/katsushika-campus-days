using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 階のある屋内のエレベーター (#275)。FBX の ev_&lt;id&gt;_&lt;階&gt;（扉から 1.4 m 前の床）に乗り場を置き、
    /// ev_&lt;id&gt;_&lt;階&gt;_f（乗り場から 1 m 先）の向きを着いたときの向きにする。
    /// あわせて、屋内の明かりをいる階のものだけ点ける InteriorFloorLights を付ける。
    /// </summary>
    public static class InteriorElevatorStage
    {
        /// <summary>乗り場の判定を扉の側へ寄せる距離（m）。扉の前に立って E を押せる位置。</summary>
        public const float PanelSetback = 0.7f;

        /// <summary>乗り場の判定の幅の半分（m）。EV 2 基の扉の前を覆う。</summary>
        public const float PanelHalfWidth = 1.2f;

        /// <summary>判定の中心から扉の面までの距離（m）。乗り場は扉から 1.4 m 前、判定はそこから PanelSetback 扉の側。</summary>
        public const float DoorOffset = 1.4f - PanelSetback;

        /// <summary>E に反応する距離（m）。扉の面の上の最も近い点から測る。</summary>
        public const float PanelRange = 2.4f;

        /// <summary>
        /// 階の下端を床からどれだけ下にとるか（m）。床の板の厚みより大きく、天井の高さより小さく。
        /// 階高 4.0 m なら次の階の下端は床 +3.5 m で、天井の明かり（床 +2.6〜3.0 m）はその下に収まる。
        /// </summary>
        public const float FloorMargin = 0.5f;

        /// <summary>
        /// 階ごとの用途。行き先の画面で階の名前の横に出す。研究棟 2 号館の 3F〜6F は各階の EV ホールの看板
        /// （blender/kcd_interior/plan_research2_dims.py の UPPER_USE）から階の名前を除いたもの。
        /// 講義棟は spec（docs/ref/interiors/lecture/spec.md §1-12）の表のとおり。7F は研究室の階（推定）。
        /// </summary>
        private static readonly Dictionary<string, string[]> Uses = new Dictionary<string, string[]>
        {
            { "research2_1F", new[] { "ロビー", "Lobby" } },
            { "research2_3F", new[] { "事務室", "Office" } },
            { "research2_4F", new[] { "研究室", "Labs" } },
            { "research2_5F", new[] { "研究室", "Labs" } },
            { "research2_6F", new[] { "会議室", "Meeting Rooms" } },
            { "lecture_1F", new[] { "講義室・生協", "Lecture Halls & Co-op" } },
            { "lecture_2F", new[] { "講義室・ラウンジ", "Classrooms & Lounge" } },
            { "lecture_3F", new[] { "講義室・PC ルーム・ラウンジ", "Classrooms, PC Room & Lounges" } },
            { "lecture_4F", new[] { "講義室・会議室", "Classrooms & Meeting Room" } },
            { "lecture_5F", new[] { "講義室・会議室", "Classrooms & Meeting Room" } },
            { "lecture_6F", new[] { "講義室・会議室", "Classrooms & Meeting Room" } },
            { "lecture_7F", new[] { "研究室", "Labs" } },
        };

        /// <summary>乗り場を置き、階ごとの明かりを付ける。置いた乗り場の数を返す（無ければ 0）。</summary>
        public static int Build(Transform interior, string id)
        {
            List<ElevatorPanel.Stop> stops = CollectStops(interior, "ev_" + id + "_", id, "Landing_");
            if (stops.Count == 0)
            {
                return 0;
            }

            if (stops.Count == 1)
            {
                EditorPaths.Report("屋内 " + id + " の ev_ が 1 つしかありません（行き先がない）。");
                return 0;
            }

            var group = new GameObject("Elevators");
            group.transform.SetParent(interior, false);
            ElevatorPanel.Stop[] all = stops.ToArray();
            foreach (ElevatorPanel.Stop stop in all)
            {
                stop.Landing.SetParent(group.transform, true);
                AddPanel(group.transform, id, stop, all);
            }

            AddFloorLights(interior, id, all);
            return all.Length;
        }

        /// <summary>
        /// prefix&lt;階&gt; の Empty を下の階から順に集め、prefix&lt;階&gt;_f の向きで着いたときの立ち位置
        /// （landingName&lt;階&gt;）を作る。階段室の扉（InteriorStairStage）もこれで集め、行き先の階の用途（<see cref="Uses"/>）は階段の画面にも出す。
        /// </summary>
        internal static List<ElevatorPanel.Stop> CollectStops(Transform interior, string prefix, string id,
            string landingName)
        {
            var points = new Dictionary<string, Transform>();
            var fronts = new Dictionary<string, Transform>();
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith(prefix))
                {
                    continue;
                }

                string floor = child.name.Substring(prefix.Length);
                if (floor.EndsWith("_f"))
                {
                    fronts[floor.Substring(0, floor.Length - 2)] = child;
                }
                else
                {
                    points[floor] = child;
                }
            }

            var stops = new List<ElevatorPanel.Stop>();
            foreach (KeyValuePair<string, Transform> pair in points)
            {
                Vector3 ahead = fronts.TryGetValue(pair.Key, out Transform front)
                    ? front.position - pair.Value.position
                    : interior.forward;
                ahead.y = 0f;
                float yaw = ahead.sqrMagnitude > 1e-4f ? Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg : 0f;

                var landing = new GameObject(landingName + pair.Key);
                landing.transform.SetPositionAndRotation(pair.Value.position, Quaternion.Euler(0f, yaw, 0f));
                Uses.TryGetValue(id + "_" + pair.Key, out string[] use);
                stops.Add(new ElevatorPanel.Stop
                {
                    Floor = pair.Key,
                    CaptionJa = use != null ? use[0] : string.Empty,
                    CaptionEn = use != null ? use[1] : string.Empty,
                    Landing = landing.transform
                });
            }

            stops.Sort((a, b) => a.Landing.position.y.CompareTo(b.Landing.position.y));
            return stops;
        }

        /// <summary>乗り場の判定。扉の前（着いたときの向きの逆へ <see cref="PanelSetback"/>）に置く。</summary>
        private static void AddPanel(Transform group, string id, ElevatorPanel.Stop stop, ElevatorPanel.Stop[] all)
        {
            Transform landing = stop.Landing;
            var go = new GameObject("Elevator_" + stop.Floor);
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
            box.size = new Vector3(PanelHalfWidth * 2f, 2f, 1.2f);

            ElevatorPanel panel = go.AddComponent<ElevatorPanel>();
            panel.BuildingId = id;
            panel.Floor = stop.Floor;
            panel.Stops = all;
            panel.InteractionRange = PanelRange;
            panel.SetDoor(DoorOffset, PanelHalfWidth);
        }

        /// <summary>屋内の Lights の下の明かりを、乗り場の高さで階に分けて InteriorFloorLights に渡す。</summary>
        private static void AddFloorLights(Transform interior, string id, ElevatorPanel.Stop[] stops)
        {
            Transform group = interior.Find("Lights");
            if (group == null)
            {
                return;
            }

            var names = new string[stops.Length];
            var bottoms = new float[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                names[i] = stops[i].Floor;
                bottoms[i] = stops[i].Landing.position.y - FloorMargin;
            }

            Light[] lights = group.GetComponentsInChildren<Light>(true);
            var levels = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                levels[i] = LevelOf(lights[i], id);
            }

            InteriorFloorLights floors = group.gameObject.AddComponent<InteriorFloorLights>();
            floors.Configure(id, names, bottoms, lights, levels);
        }

        /// <summary>
        /// 明かりの階を決める高さ。lamp_ の明かりはその高さ、spawn・poi・sign の上に吊った明かりは吊り元の高さ
        /// （<see cref="InteriorStage.AnchorLift"/> 下）。1F の看板の上の明かりが 2F に数えられないように。
        /// </summary>
        public static float LevelOf(Light light, string id)
        {
            float y = light.transform.position.y;
            return light.name.StartsWith("Light_lamp_" + id + "_") ? y : y - InteriorStage.AnchorLift;
        }
    }
}
