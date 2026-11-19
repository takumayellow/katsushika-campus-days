using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// クエストのステップに書いた置き場所（place）から、屋内に [E] で拾う物と話す人を置く (#410)。
    ///
    /// place は step の中に書く:
    /// <c>{"building":"library","at":"seat_library_04","toward":"seat_library_06","shift":-1.75,"name":"迷子の本"}</c>。
    /// at と toward は屋内（World/Interiors/Interior_&lt;building&gt;）の empty の名前で、at から toward の向きへ shift m
    /// ずらした所に置く（負なら反対へ）。collect のステップは target の拾い物を、その真下のいちばん上の面（机の天板か床）に
    /// 寝かせて置く。talk のステップは target を会話 id にした人を body の体で立たせ、toward の方を向かせる。range は E が
    /// 出る距離。屋内に NavMesh は無いので人は歩かせない（LectureTaStage の TA と同じ作り）。
    /// 屋内の当たり判定を使うので <see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class QuestPlaceStage
    {
        public const string GroupName = "QuestPlaces";

        /// <summary>人の体を指定しなかったときの体（男子学生）。</summary>
        public const string DefaultBody = "student_m";

        /// <summary>E が出る距離の既定（m）。拾い物は CreateCollectable の 2 m、人は CreateNpc の 2.4 m。</summary>
        private const float DefaultItemRange = 2f;

        private const float DefaultTalkRange = 2.4f;

        /// <summary>置き場所の上のどこから下へ面を探すか（m、at の高さから）。机の天板（0.7〜0.8 m）より上。</summary>
        private const float SurfaceProbeLift = 1.3f;

        /// <summary>at の高さより下の面は探さない（m）。吹き抜けの下の階へ落とさない。</summary>
        private const float SurfaceProbeDepth = 0.5f;

        /// <summary>上向きの面とみなす法線の y。</summary>
        private const float MinSurfaceNormalY = 0.7f;

        /// <summary>置き場所 1 つ。<see cref="Parse"/> がクエストの JSON から作る。</summary>
        public struct Place
        {
            public string QuestId;
            public string StepId;
            public string Kind;
            public string Target;
            public string Building;
            public string At;
            public string Toward;
            public float Shift;
            public string Name;
            public string Body;
            public float Range;

            public bool IsCollect => Kind == "collect";
            public bool IsTalk => Kind == "talk";
            public string InteriorName => QuestObjectiveLocator.InteriorPrefix + Building;
        }

        public static void Build(Transform root)
        {
            Transform interiors = root.Find("Interiors");
            if (interiors == null)
            {
                EditorPaths.Report("Interiors が無いので、クエストの置き場所の物と人は置きませんでした。");
                return;
            }

            List<Place> places = LoadAll();
            if (places.Count == 0)
            {
                return;
            }

            Physics.SyncTransforms();
            var missing = new List<string>();
            int placed = 0;
            foreach (Place place in places)
            {
                if (TryPlace(interiors, place))
                {
                    placed++;
                }
                else
                {
                    missing.Add(place.QuestId + "/" + place.StepId);
                }
            }

            EditorPaths.Report("クエストの置き場所に " + placed + "/" + places.Count + " 個置きました。");
            if (missing.Count > 0)
            {
                EditorPaths.Report("置き場所の empty が見つからないステップ: " + string.Join(", ", missing));
            }
        }

        /// <summary>Assets/Data/Quests の全クエストから place を集める。</summary>
        public static List<Place> LoadAll()
        {
            var places = new List<Place>();
            string folder = EditorPaths.Absolute(DataBundler.QuestSource);
            if (!Directory.Exists(folder))
            {
                return places;
            }

            string[] files = Directory.GetFiles(folder, "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (string file in files)
            {
                places.AddRange(Parse(File.ReadAllText(file)));
            }

            return places;
        }

        /// <summary>クエスト 1 つの JSON から、place のあるステップを読む。building・at・target の無いものは飛ばす。</summary>
        public static List<Place> Parse(string json)
        {
            var places = new List<Place>();
            if (!(MiniJson.Deserialize(json) is Dictionary<string, object> quest))
            {
                return places;
            }

            string questId = MiniJson.GetString(quest, "id");
            List<object> steps = MiniJson.GetArray(quest, "steps");
            if (steps == null)
            {
                return places;
            }

            foreach (object item in steps)
            {
                if (!(item is Dictionary<string, object> step))
                {
                    continue;
                }

                Dictionary<string, object> place = MiniJson.GetObject(step, "place");
                if (place == null)
                {
                    continue;
                }

                string kind = MiniJson.GetString(step, "type");
                var parsed = new Place
                {
                    QuestId = questId,
                    StepId = MiniJson.GetString(step, "id"),
                    Kind = kind,
                    Target = MiniJson.GetString(step, "target"),
                    Building = MiniJson.GetString(place, "building"),
                    At = MiniJson.GetString(place, "at"),
                    Toward = MiniJson.GetString(place, "toward"),
                    Shift = MiniJson.GetFloat(place, "shift"),
                    Name = MiniJson.GetString(place, "name"),
                    Body = MiniJson.GetString(place, "body", DefaultBody),
                    Range = MiniJson.GetFloat(place, "range", kind == "talk" ? DefaultTalkRange : DefaultItemRange),
                };

                if (string.IsNullOrEmpty(parsed.Building) || string.IsNullOrEmpty(parsed.At)
                    || string.IsNullOrEmpty(parsed.Target))
                {
                    continue;
                }

                places.Add(parsed);
            }

            return places;
        }

        /// <summary>
        /// at から toward の向きへ shift ずらした点と、置いた物の向き（水平、toward の方）。
        /// toward が無いか at と同じ所なら、ずらさず +Z を向く。
        /// </summary>
        public static Vector3 Offset(Vector3 at, Vector3? toward, float shift, out Vector3 facing)
        {
            facing = Vector3.forward;
            if (!toward.HasValue)
            {
                return at;
            }

            Vector3 flat = toward.Value - at;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
            {
                return at;
            }

            facing = flat.normalized;
            return at + facing * shift;
        }

        private static bool TryPlace(Transform interiors, Place place)
        {
            Transform interior = FindDeep(interiors, place.InteriorName);
            Transform at = interior != null ? FindDeep(interior, place.At) : null;
            if (at == null)
            {
                return false;
            }

            Transform toward = string.IsNullOrEmpty(place.Toward) ? null : FindDeep(interior, place.Toward);
            Vector3 spot = Offset(at.position, toward != null ? toward.position : (Vector3?)null, place.Shift,
                out Vector3 facing);
            Transform group = Group(interior);

            if (place.IsCollect)
            {
                PlaceItem(group, place, TopSurface(spot), facing);
                return true;
            }

            if (place.IsTalk)
            {
                PlacePerson(group, place, CollectibleStage.IndoorFloor(spot), facing);
                return true;
            }

            EditorPaths.Report(place.QuestId + "/" + place.StepId + " の place は collect と talk のステップにしか使えません（" + place.Kind + "）。");
            return false;
        }

        /// <summary>拾い物。机や床の上に寝かせて置き、浮かせない。</summary>
        private static void PlaceItem(Transform group, Place place, Vector3 surface, Vector3 facing)
        {
            float lift = CollectableMeshes.RestingHalfHeight(place.Target);
            GameObject item = ActorFactory.CreateCollectable(group, place.Target, place.Name,
                surface + Vector3.up * lift, "white");
            item.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);

            CollectableItem collectable = item.GetComponent<CollectableItem>();
            collectable.Floats = false;
            collectable.InteractionRange = place.Range;
            EditorPaths.Report(place.QuestId + "/" + place.StepId + ": " + place.Target + " を " + surface.ToString("F2") + " に置きました。");
        }

        /// <summary>話す人。toward の方（カウンターなど）に背を向け、来た人の方を向いて立つ。</summary>
        private static void PlacePerson(Transform group, Place place, Vector3 floor, Vector3 facing)
        {
            float yaw = Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg;
            string name = string.IsNullOrEmpty(place.Name) ? place.Target : place.Name;
            GameObject npc = ActorFactory.CreateNpc(group, place.Body, name, floor, yaw, 0f);
            npc.name = "NPC_" + place.Target;

            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = place.Target;
                talker.DisplayName = name;
                talker.InteractionRange = place.Range;
            }

            NPCWander wander = npc.GetComponent<NPCWander>();
            if (wander != null)
            {
                Object.DestroyImmediate(wander);
            }

            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                Object.DestroyImmediate(agent);
            }

            CampusStage.Ignore(npc);
            EditorPaths.Report(place.QuestId + "/" + place.StepId + ": " + place.Target + " を " + floor.ToString("F1") + " 向き " + yaw.ToString("F0") + "° に置きました。");
        }

        /// <summary>
        /// spot の真下のいちばん上の上向きの面（机の天板か床）。spot の高さ +<see cref="SurfaceProbeLift"/> から
        /// -<see cref="SurfaceProbeDepth"/> までを探し、無ければ <see cref="CollectibleStage.IndoorFloor"/>。
        /// </summary>
        public static Vector3 TopSurface(Vector3 spot)
        {
            RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * SurfaceProbeLift, Vector3.down,
                SurfaceProbeLift + SurfaceProbeDepth, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            Vector3 best = spot;
            foreach (RaycastHit hit in hits)
            {
                if (hit.normal.y < MinSurfaceNormalY || IsActor(hit.collider))
                {
                    continue;
                }

                if (!found || hit.point.y > best.y)
                {
                    best = hit.point;
                    found = true;
                }
            }

            return found ? best : CollectibleStage.IndoorFloor(spot);
        }

        /// <summary>人・拾い物・調べる判定（どれも Interactable）の当たり判定。面として数えない。</summary>
        private static bool IsActor(Collider collider)
        {
            return collider.GetComponentInParent<Interactable>() != null;
        }

        private static Transform Group(Transform interior)
        {
            Transform group = interior.Find(GroupName);
            if (group != null)
            {
                return group;
            }

            var go = new GameObject(GroupName);
            go.transform.SetParent(interior, false);
            return go.transform;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
