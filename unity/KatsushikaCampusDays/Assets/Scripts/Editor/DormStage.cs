using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// 葛飾コミュニティハウス（寮）の屋内と玄関 (#41)。
    /// 屋内は <see cref="InteriorStage"/> と同じ「遠くに置いてワープする」作りだが、置き場所も
    /// 入口のクラスも本編とは分けてある。<see cref="InteriorStage"/> の Ids に dorm を足さないのは、
    /// あちらが探索率と「入った建物」に数えられる本編の 9 棟だけを並べる場所だから。
    ///
    /// 屋内に spawn_dorm か exit_dorm が欠けているときは InteriorLoader に登録しない。
    /// 登録しなければ <see cref="DormEntrance"/> は「鍵が掛かっている」と言うだけで、
    /// 出口の無い部屋に閉じ込められることが起きない。
    /// </summary>
    public static class DormStage
    {
        /// <summary>寮の屋内モデル。blender/build_dorm.py が書き出す（kcd_route/dorm.py の間取り）。</summary>
        public const string InteriorFbx = "Assets/Models/Interiors/dorm.fbx";

        /// <summary>屋内を置く x。InteriorStage が並べる 9 棟（x 1200 以降）よりさらに遠く。</summary>
        public const float SlotX = 4000f;

        /// <summary>入った直後の立ち位置の Empty。</summary>
        public const string SpawnEmpty = "spawn_" + DormRoute.Id;

        /// <summary>出口の Empty。</summary>
        public const string ExitEmpty = "exit_" + DormRoute.Id;

        /// <summary>寮長を置く Empty。無ければ <see cref="NpcEmptyAliases"/> を順に探す。</summary>
        public const string NpcEmpty = "npc_" + DormRoute.DialogueId;

        /// <summary>
        /// 寮長の Empty の別名。blender/kcd_route/dorm.py は同じ位置に npc_dorm_head（Unity 用の名前）と
        /// npc_dorm_1 / poi_dorm_kanrinin（kcd_interior の通し番号の約束）を置く。
        /// どれかが消えても寮長が行方不明にならないよう、順に探す。
        /// </summary>
        public static readonly string[] NpcEmptyAliases =
        {
            NpcEmpty,
            "npc_" + DormRoute.Id + "_1",
            "poi_" + DormRoute.Id + "_kanrinin",
        };

        /// <summary>寮長が spawn から離れて立つ距離（m）。NpcEmpty が無いときに使う。</summary>
        public const float NpcStandoff = 2.6f;

        /// <summary>玄関の当たり判定を置く点を、扉の面からどれだけ外へずらして地面を探るか（m）。</summary>
        public const float GroundProbeOffset = 2f;

        /// <summary>
        /// 庇（route.json の玄関の CAN_D）が扉の前へ張り出す長さ（m）。
        /// <see cref="GroundProbeOffset"/> の 2 m はこれより内側なので、探る点はまだ庇の真下にある。
        /// </summary>
        public const float CanopyDepth = 3.8f;

        /// <summary>
        /// 控えの地面探り。庇の張り出しより外まで下がってから真下を見る（m）。
        /// Ground レイヤーが無いときにだけ使う。
        /// </summary>
        public const float CanopyClearProbeOffset = 6f;

        /// <summary>
        /// 寮の屋内と外の玄関を置く。InteriorStage.Build（InteriorLoader への登録）のあと、
        /// route.fbx が置けたときだけ呼ぶ。
        /// </summary>
        public static void Build(Transform root)
        {
            DormRoute.Info info = RouteStage.ReadInfo();
            GameObject entrance = BuildEntrance(root, info);
            BuildInterior(root, entrance);
        }

        /// <summary>
        /// 外の玄関。OSM から取った玄関の点に、外向き（facing_bearing）を正面にして置く。
        /// レイヤーとトリガー箱の作り方は CampusProps.PlaceEntrances と同じ。
        /// </summary>
        public static GameObject BuildEntrance(Transform root, DormRoute.Info info)
        {
            // 直前に置いた route.fbx の当たり判定を地面探りで拾えるようにする。
            Physics.SyncTransforms();

            Vector3 outward = DormRoute.OutwardFromBearing(info.DoorBearing);
            float groundY = ProbeGroundY(info.Door, outward);

            var door = new GameObject("Entrance_" + DormRoute.Id);
            door.transform.SetParent(root, false);
            door.transform.SetPositionAndRotation(
                DormRoute.DoorWorld(info, groundY), Quaternion.LookRotation(outward, Vector3.up));

            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                door.layer = layer;
            }

            BoxCollider box = door.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = DormEntrance.BoxCenter;
            box.size = DormEntrance.BoxSize;

            DormEntrance trigger = door.AddComponent<DormEntrance>();
            trigger.BuildingId = DormRoute.Id;
            trigger.DisplayName = DormRoute.DisplayName;
            trigger.InteractionRange = DormEntrance.DefaultRange;

            CampusStage.Ignore(door);
            // 庇を拾ってしまう昔のやり方の値も出す。差が大きいままなら、また屋根の上に載っている。
            float naive = CampusProps.Ground(
                info.Door + new Vector2(outward.x, outward.z) * GroundProbeOffset).y;
            EditorPaths.Report("寮の玄関を置きました: (" + info.Door.x.ToString("F2") + ", "
                + groundY.ToString("F2") + ", " + info.Door.y.ToString("F2") + ") 方位 "
                + info.DoorBearing.ToString("F1") + " 度"
                + "（全レイヤーを見ると " + naive.ToString("F2") + " m = 庇の上）");
            return door;
        }

        /// <summary>
        /// 玄関の足もとの高さ。
        ///
        /// 扉の前には庇が <see cref="CanopyDepth"/> m 張り出している。すべての当たり判定を相手にすると
        /// <see cref="CampusProps.Ground"/> は「いちばん高い当たり」を地面とみなすので、扉から 2 m 先で
        /// 真下を見ても拾えるのは庇の上面（地上 3.6 m）になり、判定箱ごと屋根の上に載ってしまう。
        /// そうなると扉の前に立っても届かず、いつまでも中へ入れない (#41)。
        ///
        /// 地面は route_ground が Ground レイヤーで持っている（RouteStage.Dress）ので、そこだけを見る。
        /// Ground レイヤーが無い／当たらないときだけ、庇より外へ下がって従来のやり方で探り直す。
        /// </summary>
        public static float ProbeGroundY(Vector2 door, Vector3 outward)
        {
            var step = new Vector2(outward.x, outward.z);
            Vector2 probe = door + step * GroundProbeOffset;
            int groundLayer = LayerMask.NameToLayer("Ground");

            if (groundLayer >= 0)
            {
                var from = new Vector3(probe.x, 60f, probe.y);
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 120f,
                        1 << groundLayer, QueryTriggerInteraction.Ignore))
                {
                    return hit.point.y;
                }

                EditorPaths.Report("寮の玄関の下に Ground の当たり判定がありません。庇の外で探り直します。");
            }

            return CampusProps.Ground(door + step * CanopyClearProbeOffset).y;
        }

        /// <summary>屋内を遠くに置いて、当たり判定・照明・出口・寮長を足し、InteriorLoader に登録する。</summary>
        public static GameObject BuildInterior(Transform root, GameObject entrance)
        {
            InteriorLoader loader = Object.FindAnyObjectByType<InteriorLoader>();
            if (loader == null)
            {
                EditorPaths.Report("InteriorLoader が無いので寮の屋内を置けません。");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(InteriorFbx) == null)
            {
                EditorPaths.Report("寮の屋内モデルがありません: " + InteriorFbx);
                return null;
            }

            GameObject interior = CampusStage.PlaceModel(InteriorFbx, "Interior_" + DormRoute.Id, root);
            if (interior == null)
            {
                return null;
            }

            Bounds bounds = Dress(interior);
            float shift = SlotX - bounds.min.x;
            interior.transform.position += new Vector3(shift, 0f, 0f);
            bounds.center += new Vector3(shift, 0f, 0f);
            Physics.SyncTransforms();

            AddSafetyFloor(interior.transform, bounds);
            AddLights(interior.transform, bounds);
            SeatFactory.PlaceInterior(interior.transform, DormRoute.Id);

            Transform spawn = Find(interior.transform, SpawnEmpty);
            Transform exit = Find(interior.transform, ExitEmpty);
            if (spawn == null || exit == null)
            {
                EditorPaths.Report("寮の屋内に " + (spawn == null ? SpawnEmpty : ExitEmpty)
                    + " がありません。閉じ込められないよう InteriorLoader には登録しません。");
                return interior;
            }

            AddExit(interior.transform, exit);
            PlaceDormHead(interior.transform, spawn);
            Register(loader, interior.transform, spawn, exit, entrance);

            EditorPaths.Report("寮の屋内を置きました: x " + bounds.min.x.ToString("F0") + ".."
                + bounds.max.x.ToString("F0") + " z " + bounds.min.z.ToString("F0") + ".."
                + bounds.max.z.ToString("F0"));
            return interior;
        }

        /// <summary>
        /// 当たり判定・レイヤー・静的フラグ。NavMesh からは外す（InteriorStage.Dress と同じ作り）。
        /// ワールドの AABB を返す。
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
                // 観葉植物の葉は当たり判定から外す（#30）。
                CampusStage.AttachMeshCollider(go, filter.sharedMesh,
                    CampusStage.ColliderAssetPath(interior.name, go.name));
                go.layer = id.StartsWith("floor") && groundLayer >= 0
                    ? groundLayer
                    : buildingLayer >= 0 ? buildingLayer : go.layer;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer == null)
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

        /// <summary>
        /// 屋内の明かり。部屋は 1 つだけなので Empty ごとには吊らず、spawn / exit / npc と中央に点光源を置く。
        /// 値は InteriorStage.AddLights と同じ（暖色・影なし・5 m 以内は間引き）。
        /// </summary>
        private static void AddLights(Transform interior, Bounds bounds)
        {
            var lights = new GameObject("Lights");
            lights.transform.SetParent(interior, false);
            var placed = new List<Vector3>();

            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                bool anchor = name == SpawnEmpty || name == ExitEmpty || name == NpcEmpty
                    || name.StartsWith("sign_" + DormRoute.Id + "_");
                if (!anchor || TooClose(placed, child.position, 5f))
                {
                    continue;
                }

                var go = new GameObject("Light_" + name);
                go.transform.SetParent(lights.transform, false);
                go.transform.position = child.position + Vector3.up * 3.2f;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 14f;
                light.intensity = 1.35f;
                light.color = new Color(1f, 0.96f, 0.88f);
                light.shadows = LightShadows.None;
                placed.Add(child.position);
            }

            var center = new GameObject("Light_center");
            center.transform.SetParent(lights.transform, false);
            center.transform.position = new Vector3(bounds.center.x, bounds.min.y + 3.5f, bounds.center.z);
            Light centerLight = center.AddComponent<Light>();
            centerLight.type = LightType.Point;
            centerLight.range = Mathf.Max(16f, bounds.size.magnitude * 0.5f);
            centerLight.intensity = 1.2f;
            centerLight.color = new Color(1f, 0.97f, 0.9f);
            centerLight.shadows = LightShadows.None;
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

        /// <summary>exit_dorm を踏んだら外へ。本編の屋内と同じ InteriorExit を使う。</summary>
        private static void AddExit(Transform interior, Transform exit)
        {
            var go = new GameObject("Exit_" + DormRoute.Id);
            go.transform.SetParent(interior, false);
            go.transform.position = exit.position + Vector3.up * 1.2f;
            go.transform.rotation = Quaternion.identity;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 2.6f, 3f);
            go.AddComponent<InteriorExit>().BuildingId = DormRoute.Id;
        }

        /// <summary>
        /// 寮長。専用のモデルは作らず、教授のリグ（prof）をそのまま借りる。
        /// 会話 id だけ dorm_head に差し替えるので、Assets/Data/Dialogue/dorm_head.json が読まれる。
        ///
        /// NavMeshAgent と NPCWander は外す。寮の屋内（x = 4000）は NavMesh の外なので、
        /// 乗る先を探しても見つからず、立ち止まるだけのために毎フレーム探し続けることになる。
        /// 寮長は一歩も動かないので、そもそも持たせない。
        /// </summary>
        private static GameObject PlaceDormHead(Transform interior, Transform spawn)
        {
            Transform anchor = null;
            for (int i = 0; i < NpcEmptyAliases.Length && anchor == null; i++)
            {
                anchor = Find(interior, NpcEmptyAliases[i]);
            }

            Vector3 position;
            if (anchor != null)
            {
                position = anchor.position;
            }
            else
            {
                // spawn は扉を背にして部屋の奥を向いている。その正面に立たせる。
                Vector3 ahead = spawn.forward;
                ahead.y = 0f;
                ahead = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
                position = spawn.position + ahead * NpcStandoff;
                EditorPaths.Report(NpcEmpty + " が無いので、寮長を spawn の " + NpcStandoff.ToString("F1")
                    + " m 先に立たせました。");
            }

            Vector3 toSpawn = spawn.position - position;
            toSpawn.y = 0f;
            float yaw = toSpawn.sqrMagnitude > 0.01f
                ? Mathf.Atan2(toSpawn.x, toSpawn.z) * Mathf.Rad2Deg
                : 0f;

            // リグは prof を借り、会話 id と表示名だけ寮長のものにする。
            GameObject npc = ActorFactory.CreateNpc(
                interior, DormRoute.NpcBodyId, DormRoute.NpcName, position, yaw, 0f);
            npc.name = "NPC_" + DormRoute.DialogueId;

            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = DormRoute.DialogueId;
                talker.DisplayName = DormRoute.NpcName;
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
            return npc;
        }

        /// <summary>
        /// 入った直後の立ち位置と、外の入口の位置を InteriorLoader に教える。
        /// 表示名は CampusProps.DisplayName（本編の 9 棟しか知らない）ではなく DormRoute のものを使う。
        /// </summary>
        private static void Register(
            InteriorLoader loader, Transform interior, Transform spawn, Transform exit, GameObject entrance)
        {
            // 立ち位置は扉から部屋の奥を向く（InteriorStage.Register と同じ求め方）。
            Vector3 inward = spawn.position - exit.position;
            inward.y = 0f;
            float yaw = inward.sqrMagnitude > 0.01f ? Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg : 0f;

            var point = new GameObject("Spawn_" + DormRoute.Id);
            point.transform.SetParent(interior, false);
            point.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));

            loader.Register(new InteriorLoader.Entry
            {
                Id = DormRoute.Id,
                DisplayName = DormRoute.DisplayName,
                Spawn = point.transform,
                EntranceWorld = entrance != null ? entrance.transform.position : Vector3.zero
            });
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
