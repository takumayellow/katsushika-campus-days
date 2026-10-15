using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 建物の屋内。Assets/Models/Interiors/&lt;id&gt;.fbx をキャンパスの遠く（x = 1200 m 以降）に横一列に並べ、
    /// 当たり判定・照明・出口・到達判定を付けて InteriorLoader に登録する。
    /// FBX の Empty: spawn_&lt;id&gt;（入った直後の立ち位置）, exit_&lt;id&gt;（出口）, poi_&lt;id&gt;_*（クエストの visit 先）,
    /// 2 つ目からの扉（BuildingDoors）の spawn_&lt;扉の id&gt; と exit_&lt;扉の id&gt;,
    /// seat_&lt;id&gt;_*（座れる家具。SeatFactory.PlaceInterior が読む）,
    /// ev_&lt;id&gt;_&lt;階&gt;（エレベーターの乗り場）と lamp_&lt;id&gt;_&lt;階&gt;_*（その階の天井の明かり。InteriorElevatorStage が読む）,
    /// esc_&lt;id&gt;_&lt;区間&gt;_lo / _hi（エスカレーターの下の端と上の端。InteriorEscalatorStage が読む）。
    /// </summary>
    public static class InteriorStage
    {
        public const string ModelsFolder = "Assets/Models/Interiors";

        private static readonly string[] Ids =
        {
            "greenhouse", "gym", "kyoso", "lab1", "lab2", "lecture", "library", "research1", "research2", "cafeteria"
        };

        /// <summary>最初の屋内を置く x。NavMesh の範囲（x ≤ 243）から十分離す。</summary>
        private const float FirstSlotX = 1200f;

        /// <summary>隣の屋内との隙間。InteriorBackdropStage.Margin（遠景の帯）はこの半分より小さくする。</summary>
        private const float Gap = 80f;

        /// <summary>
        /// 屋内の並びの下に敷く地面の高さ（m）。窓の外の近景 ext_* の地面（床から -0.03〜-0.045, #84）と
        /// 遠景のドームの地面（InteriorBackdropStage.GroundY = -0.0475）とキャンパスの OuterGround（-0.05）より下に置き、
        /// どれも隠さない (#60)。
        /// </summary>
        public const float OutsideGroundY = -0.06f;

        /// <summary>
        /// 1F の床より下まで掘り下げた部屋（講義棟の 101 の段床）の床のメッシュ名に入る印。kcd_interior の
        /// Ctx.part("floor", "pit") が floor_&lt;id&gt;_pit という名前で出す。この範囲の上には屋内の下の地面を張らない。
        /// </summary>
        public const string PitMarker = "_pit";

        /// <summary>屋内の並びの下の地面の z の中心と半分の奥行き（m）。</summary>
        private const float OutsideGroundZ = 30f;
        private const float OutsideGroundHalfDepth = 300f;

        /// <summary>
        /// 窓のすぐ外の近景（舗装・芝・生垣・木）のメッシュ名の頭。kcd_interior が外周の壁の外にだけ置く (#45)。
        /// プレイヤーは届かないので当たり判定を付けず、描くだけにする (#60)。
        /// </summary>
        public const string ExteriorPrefix = InteriorBackdrop.ExteriorPrefix;

        /// <summary>
        /// 窓の外の近景のメッシュか（名前が ext_ で始まる。大文字小文字は見ない）。
        /// テスト（KCD.Runtime しか参照できない）からも同じ判定を使えるよう、本体はランタイム側に置く。
        /// </summary>
        public static bool IsExteriorDressing(string objectName)
        {
            return InteriorBackdrop.IsExteriorDressing(objectName);
        }

        /// <summary>屋内を全部置いて登録する。PlaceSystems（InteriorLoader）のあとに呼ぶ。</summary>
        public static void Build(Transform root)
        {
            InteriorLoader loader = Object.FindAnyObjectByType<InteriorLoader>();
            if (loader == null)
            {
                EditorPaths.Report("InteriorLoader が無いので屋内を置けません。");
                return;
            }

            var group = new GameObject("Interiors");
            group.transform.SetParent(root, false);

            float cursor = FirstSlotX;
            int placed = 0;
            var pits = new List<Rect>();
            foreach (string id in Ids)
            {
                string assetPath = ModelsFolder + "/" + id + ".fbx";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null)
                {
                    EditorPaths.Report("屋内モデルがありません: " + assetPath);
                    continue;
                }

                GameObject interior = CampusStage.PlaceModel(assetPath, "Interior_" + id, group.transform);
                if (interior == null)
                {
                    continue;
                }

                Bounds bounds = Dress(interior);
                // 左端をカーソルに合わせて並べる。
                float shift = cursor - bounds.min.x;
                interior.transform.position += new Vector3(shift, 0f, 0f);
                bounds.center += new Vector3(shift, 0f, 0f);
                cursor = bounds.max.x + Gap;
                Physics.SyncTransforms();
                List<Rect> holes = PitHoles(interior.transform, out float pitFloor);
                pits.AddRange(holes);

                AddSafetyFloor(interior.transform, bounds);
                AddLights(interior.transform, id);
                int stops = InteriorElevatorStage.Build(interior.transform, id);
                int escalators = InteriorEscalatorStage.Build(interior.transform, id);
                int stairs = InteriorStairStage.Build(interior.transform, id);
                foreach (string door in DoorsOf(id))
                {
                    AddExit(interior.transform, id, door);
                }

                int zones = AddPoiZones(interior.transform, id);
                SeatFactory.PlaceInterior(interior.transform, id);
                Register(loader, interior.transform, id);
                InteriorBackdropStage.Build(interior.transform, id, bounds, holes, pitFloor);
                placed++;

                EditorPaths.Report("屋内 " + id + ": x " + bounds.min.x.ToString("F0") + ".." + bounds.max.x.ToString("F0")
                    + " z " + bounds.min.z.ToString("F0") + ".." + bounds.max.z.ToString("F0")
                    + " poi " + zones + (stops > 0 ? " ev " + stops : string.Empty)
                    + (escalators > 0 ? " esc " + escalators : string.Empty)
                    + (stairs > 0 ? " stairs " + stairs : string.Empty));
            }

            AddOutsideGround(group.transform, FirstSlotX - 200f, cursor + 200f, pits);
            CampusStage.CutOuterGround(root, pits);
            EditorPaths.Report("屋内を " + placed + " 棟置きました。");
        }

        /// <summary>
        /// 1F の床より下まで掘り下げた部屋の床（名前に <see cref="PitMarker"/>）を上から見た範囲（Rect の x は世界の x、
        /// y は世界の z）。地面（OutsideGround・OuterGround・遠景のドーム）はこの範囲を抜いて張る。
        /// floor はいちばん深い床の世界の高さ（穴が無ければ屋内の原点の高さ）。
        /// </summary>
        public static List<Rect> PitHoles(Transform interior, out float floor)
        {
            var holes = new List<Rect>();
            floor = interior.position.y;
            foreach (MeshRenderer renderer in interior.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.gameObject.name.Contains(PitMarker))
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                if (b.min.y < interior.position.y + OutsideGroundY)
                {
                    holes.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
                    floor = Mathf.Min(floor, b.min.y);
                }
            }

            return holes;
        }

        /// <summary>
        /// 窓の外に見える地面。無いと空の下半分がそのまま見える。遠景のドームがある屋内ではその下に隠れ、
        /// サイドカーが無くてドームを置けなかった屋内でだけ見える。掘り下げた部屋の範囲（pits）には張らない。
        /// </summary>
        private static void AddOutsideGround(Transform group, float x0, float x1, IList<Rect> pits)
        {
            var ground = new GameObject("OutsideGround");
            ground.transform.SetParent(group, false);
            ground.transform.position = new Vector3((x0 + x1) * 0.5f, OutsideGroundY, OutsideGroundZ);
            List<Rect> tiles = GroundTiles.Subtract(
                Rect.MinMaxRect(x0, OutsideGroundZ - OutsideGroundHalfDepth, x1, OutsideGroundZ + OutsideGroundHalfDepth),
                pits);
            ground.AddComponent<MeshFilter>().sharedMesh =
                GroundTiles.BuildMesh(tiles, 0f, ground.transform.position, "outside_ground");
            ground.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.EnsureCampus("grass_dark");
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                ground.layer = groundLayer;
            }

            CampusStage.Ignore(ground);
        }

        /// <summary>
        /// 当たり判定・レイヤー・静的フラグ。NavMesh からは外す。ワールドの AABB を返す。
        /// AABB は建物だけを囲み、窓の外の近景 ext_*（外周から約 30 m）は入れない。入れるとスロットの並び・
        /// 中央の明かり・遠景の帯が近景の大きさで決まってしまう (#60, #84)。
        /// </summary>
        private static Bounds Dress(GameObject interior)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int buildingLayer = LayerMask.NameToLayer("Building");
            CampusStage.Ignore(interior);

            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in interior.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                GameObject go = filter.gameObject;
                string id = go.name.ToLowerInvariant();
                // 観葉植物の葉は当たり判定から外す（#30）。窓の外の近景 ext_* は描くだけ（#60）。
                // エスカレーターの段は動かすので、当たり判定（斜めの板が受け持つ）も静的フラグも付けない。
                bool moving = InteriorEscalatorStage.IsMovingSteps(go.name);
                if (!IsExteriorDressing(go.name) && !moving)
                {
                    CampusStage.AttachMeshCollider(go, filter.sharedMesh,
                        CampusStage.ColliderAssetPath(interior.name, go.name));
                }

                go.layer = id.StartsWith("floor") && groundLayer >= 0
                    ? groundLayer
                    : buildingLayer >= 0 ? buildingLayer : go.layer;
                GameObjectUtility.SetStaticEditorFlags(go, moving
                    ? 0
                    : StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                        | StaticEditorFlags.OccludeeStatic);

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer == null || !CountsAsBuilding(renderer))
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any ? bounds : new Bounds(interior.transform.position, Vector3.one);
        }

        /// <summary>
        /// 屋内の大きさ（AABB）に数えるか。窓の外の近景 ext_* と遠景のドームは建物の外なので数えない (#60)。
        /// DormStage からも同じ判定で使う。
        /// </summary>
        public static bool CountsAsBuilding(Renderer renderer)
        {
            return renderer != null
                && !IsExteriorDressing(renderer.gameObject.name)
                && renderer.GetComponent<InteriorBackdrop>() == null;
        }

        /// <summary>床メッシュの継ぎ目から落ちないよう、床の少し下に見えない板を敷く。</summary>
        private static void AddSafetyFloor(Transform interior, Bounds bounds)
        {
            var floor = new GameObject("SafetyFloor");
            floor.transform.SetParent(interior, false);
            floor.transform.position = new Vector3(bounds.center.x, bounds.min.y - 0.3f, bounds.center.z);
            floor.transform.rotation = Quaternion.identity;
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                floor.layer = groundLayer;
            }

            BoxCollider box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(bounds.size.x + 20f, 0.5f, bounds.size.z + 20f);
        }

        /// <summary>spawn・poi・sign の Empty から、その上に吊る明かりまでの高さ（m）。</summary>
        public const float AnchorLift = 3.2f;

        /// <summary>lamp_ の Empty の床からの高さ（m）。blender/kcd_interior の LAMP_DZ。</summary>
        public const float LampHeight = 2.6f;

        /// <summary>
        /// 屋内の明かり。太陽は屋内では消すので、spawn・poi・sign の Empty ごとに暖色の点光源を吊る。
        /// lamp_&lt;id&gt;_* は天井の明かりの位置そのもので、間引かずに 1 つずつ吊る（階のある屋内の上の階, #275）。
        /// lamp_ のある階（いちばん低い lamp_ の階の床から上）には、spawn・poi・sign の明かりを足さない。
        /// 足すと、その階の 1 つの物に届く光源が 4 を越える（#279）。
        /// </summary>
        private static void AddLights(Transform interior, string id)
        {
            var lights = new GameObject("Lights");
            lights.transform.SetParent(interior, false);
            int count = 0;
            var placed = new List<Vector3>();
            float lampFloor = LowestLampFloor(interior, id);

            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (name.StartsWith("lamp_" + id + "_"))
                {
                    AddLamp(lights.transform, child);
                    count++;
                    continue;
                }

                bool anchor = name == "spawn_" + id
                    || name.StartsWith("poi_" + id + "_")
                    || name.StartsWith("sign_" + id + "_");
                if (!anchor || child.position.y >= lampFloor || TooClose(placed, child.position, 5f))
                {
                    continue;
                }

                var go = new GameObject("Light_" + name);
                go.transform.SetParent(lights.transform, false);
                go.transform.position = child.position + Vector3.up * AnchorLift;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 14f;
                light.intensity = 1.35f;
                light.color = new Color(1f, 0.96f, 0.88f);
                light.shadows = LightShadows.None;
                placed.Add(child.position);
                count++;
            }

            // Empty が少ない建物でも真っ暗にならないよう、中央に 1 つ足す。
            if (count < 3)
            {
                var go = new GameObject("Light_center");
                go.transform.SetParent(lights.transform, false);
                Bounds bounds = InteriorBounds(interior);
                go.transform.position = new Vector3(bounds.center.x, bounds.min.y + 3.5f, bounds.center.z);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(16f, bounds.size.magnitude * 0.5f);
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.97f, 0.9f);
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>
        /// いちばん低い lamp_ の階の下端（床から <see cref="InteriorElevatorStage.FloorMargin"/> 下）。
        /// lamp_ が無ければ +∞。
        /// </summary>
        private static float LowestLampFloor(Transform interior, string id)
        {
            float lowest = float.PositiveInfinity;
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("lamp_" + id + "_"))
                {
                    lowest = Mathf.Min(lowest, child.position.y);
                }
            }

            return lowest - LampHeight - InteriorElevatorStage.FloorMargin;
        }

        /// <summary>天井の明かり 1 つ。届く範囲は中廊下の明かりの間隔（12 m）を少し越えるくらい。</summary>
        private static void AddLamp(Transform lights, Transform anchor)
        {
            var go = new GameObject("Light_" + anchor.name);
            go.transform.SetParent(lights, false);
            go.transform.position = anchor.position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
        }

        private static bool TooClose(List<Vector3> placed, Vector3 position, float distance)
        {
            foreach (Vector3 other in placed)
            {
                if ((other - position).sqrMagnitude < distance * distance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Bounds InteriorBounds(Transform interior)
        {
            var bounds = new Bounds(interior.position, Vector3.one);
            bool any = false;
            foreach (Renderer renderer in interior.GetComponentsInChildren<Renderer>(true))
            {
                if (!CountsAsBuilding(renderer))
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        /// <summary>建物の扉の id。玄関（建物の id と同じ）が先で、2 つ目からの扉が続く。</summary>
        private static List<string> DoorsOf(string buildingId)
        {
            var doors = new List<string> { buildingId };
            doors.AddRange(BuildingDoors.SecondDoorsOf(buildingId));
            return doors;
        }

        /// <summary>exit_&lt;扉の id&gt; を踏んだらその扉の外へ。</summary>
        private static void AddExit(Transform interior, string id, string doorId)
        {
            Transform exit = Find(interior, "exit_" + doorId);
            if (exit == null)
            {
                EditorPaths.Report("屋内 " + id + " に exit_" + doorId + " がありません。");
                return;
            }

            Transform spawn = Find(interior, "spawn_" + doorId);
            Vector3 center = exit.position + Vector3.up * 1.2f;
            float yaw = 0f;
            var size = new Vector3(InteriorExit.BoxWidth, InteriorExit.BoxHeight, InteriorExit.BoxWidth);
            if (spawn != null)
            {
                InteriorExit.BoxFor(exit.position, spawn.position, out center, out yaw, out size);
            }

            var go = new GameObject("Exit_" + doorId);
            go.transform.SetParent(interior, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            InteriorExit marker = go.AddComponent<InteriorExit>();
            marker.BuildingId = id;
            marker.DoorId = doorId;
        }

        /// <summary>poi_&lt;id&gt;_* にクエストの visit 判定を置く（半径 2 m 相当）。</summary>
        private static int AddPoiZones(Transform interior, string id)
        {
            int count = 0;
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("poi_" + id + "_"))
                {
                    continue;
                }

                var go = new GameObject("Zone_" + child.name);
                go.transform.SetParent(interior, false);
                go.transform.position = child.position + Vector3.up * 1.2f;
                go.transform.rotation = Quaternion.identity;
                BoxCollider box = go.AddComponent<BoxCollider>();
                VisitZone zone = go.AddComponent<VisitZone>();
                zone.PlaceId = child.name;
                zone.DisplayName = string.Empty;

                // 大きさは VisitZone を足したあとに入れる。AddComponent は Reset() を呼ぶので、
                // 先に入れると既定の 10x6x10 に巻き戻る（#54）。
                box.isTrigger = true;
                box.size = new Vector3(4f, 3f, 4f);
                count++;
            }

            return count;
        }

        /// <summary>
        /// 入った直後の立ち位置と、外の入口の位置を InteriorLoader に教える。扉が 2 つ以上ある建物では、
        /// 扉ごとの立ち位置と、その扉から出たときに立つ外の所（Entrance_&lt;扉の id&gt; の外 2 m）も教える。
        /// </summary>
        private static void Register(InteriorLoader loader, Transform interior, string id)
        {
            Transform main = SpawnPoint(interior, id);
            if (main == null)
            {
                EditorPaths.Report("屋内 " + id + " に spawn_" + id + " がありません。");
                return;
            }

            GameObject entrance = GameObject.Find("Entrance_" + id);
            var entry = new InteriorLoader.Entry
            {
                Id = id,
                DisplayName = CampusProps.DisplayName(id),
                Spawn = main,
                EntranceWorld = entrance != null ? entrance.transform.position : Vector3.zero
            };

            List<string> doors = DoorsOf(id);
            if (doors.Count > 1)
            {
                foreach (string doorId in doors)
                {
                    Transform spawn = doorId == id ? main : SpawnPoint(interior, doorId);
                    if (spawn == null)
                    {
                        EditorPaths.Report("屋内 " + id + " に spawn_" + doorId + " がありません。");
                        continue;
                    }

                    entry.Doors.Add(DoorOf(doorId, spawn));
                }
            }

            loader.Register(entry);
        }

        /// <summary>spawn_&lt;扉の id&gt; に、扉から建物の奥を向く立ち位置 Spawn_&lt;扉の id&gt; を置く。</summary>
        private static Transform SpawnPoint(Transform interior, string doorId)
        {
            Transform spawn = Find(interior, "spawn_" + doorId);
            if (spawn == null)
            {
                return null;
            }

            // 立ち位置は扉から建物の奥を向く。
            Transform exit = Find(interior, "exit_" + doorId);
            Vector3 inward = exit != null ? spawn.position - exit.position : interior.forward;
            inward.y = 0f;
            float yaw = inward.sqrMagnitude > 0.01f ? Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg : 0f;

            var point = new GameObject("Spawn_" + doorId);
            point.transform.SetParent(interior, false);
            point.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));
            return point.transform;
        }

        /// <summary>扉 1 つ。外の所はキャンパスの Entrance_&lt;扉の id&gt;（正面が建物の外）から 2 m 外。</summary>
        private static InteriorLoader.Door DoorOf(string doorId, Transform spawn)
        {
            var door = new InteriorLoader.Door { DoorId = doorId, Spawn = spawn };
            GameObject entrance = GameObject.Find("Entrance_" + doorId);
            if (entrance == null)
            {
                EditorPaths.Report("キャンパスに Entrance_" + doorId + " がありません。この扉から出ると入った扉の外へ戻ります。");
                return door;
            }

            Vector3 outward = entrance.transform.forward;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
            door.HasOutside = true;
            door.OutsidePosition = entrance.transform.position + outward * InteriorLoader.OutsideStep;
            door.OutsideYaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
            return door;
        }

        private static Transform Find(Transform interior, string name)
        {
            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
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
