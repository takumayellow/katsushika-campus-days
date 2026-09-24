using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 建物の屋内。Assets/Models/Interiors/&lt;id&gt;.fbx をキャンパスの遠く（x = 1200 m 以降）に横一列に並べ、
    /// 当たり判定・照明・出口・到達判定を付けて InteriorLoader に登録する。
    /// FBX の Empty: spawn_&lt;id&gt;（入った直後の立ち位置）, exit_&lt;id&gt;（出口）, poi_&lt;id&gt;_*（クエストの visit 先）,
    /// seat_&lt;id&gt;_*（座れる家具。SeatFactory.PlaceInterior が読む）。
    /// </summary>
    public static class InteriorStage
    {
        public const string ModelsFolder = "Assets/Models/Interiors";

        private static readonly string[] Ids =
        {
            "greenhouse", "gym", "kyoso", "lab1", "lab2", "lecture", "library", "research1", "research2"
        };

        /// <summary>最初の屋内を置く x。NavMesh の範囲（x ≤ 243）から十分離す。</summary>
        private const float FirstSlotX = 1200f;

        /// <summary>隣の屋内との隙間。</summary>
        private const float Gap = 80f;

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

                AddSafetyFloor(interior.transform, bounds);
                AddLights(interior.transform, id);
                AddExit(interior.transform, id);
                int zones = AddPoiZones(interior.transform, id);
                SeatFactory.PlaceInterior(interior.transform, id);
                Register(loader, interior.transform, id);
                placed++;

                EditorPaths.Report("屋内 " + id + ": x " + bounds.min.x.ToString("F0") + ".." + bounds.max.x.ToString("F0")
                    + " z " + bounds.min.z.ToString("F0") + ".." + bounds.max.z.ToString("F0")
                    + " poi " + zones);
            }

            AddOutsideGround(group.transform, FirstSlotX - 200f, cursor + 200f);
            EditorPaths.Report("屋内を " + placed + " 棟置きました。");
        }

        /// <summary>窓の外に見える地面。無いと空の下半分がそのまま見える。</summary>
        private static void AddOutsideGround(Transform group, float x0, float x1)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "OutsideGround";
            ground.transform.SetParent(group, false);
            ground.transform.position = new Vector3((x0 + x1) * 0.5f, -0.06f, 30f);
            // Plane は 10 m 四方なので 1/10 で割る。
            ground.transform.localScale = new Vector3((x1 - x0) * 0.1f, 1f, 60f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.EnsureCampus("grass_dark");
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                ground.layer = groundLayer;
            }

            CampusStage.Ignore(ground);
        }

        /// <summary>当たり判定・レイヤー・静的フラグ。NavMesh からは外す。ワールドの AABB を返す。</summary>
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
        /// 屋内の明かり。太陽は屋内では消すので、spawn・poi・sign の Empty ごとに暖色の点光源を吊る。
        /// Forward+ なので 1 物体あたりの光源数は気にしなくてよい。
        /// </summary>
        private static void AddLights(Transform interior, string id)
        {
            var lights = new GameObject("Lights");
            lights.transform.SetParent(interior, false);
            int count = 0;
            var placed = new List<Vector3>();

            foreach (Transform child in interior.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                bool anchor = name == "spawn_" + id
                    || name.StartsWith("poi_" + id + "_")
                    || name.StartsWith("sign_" + id + "_");
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

        /// <summary>exit_&lt;id&gt; を踏んだら外へ。</summary>
        private static void AddExit(Transform interior, string id)
        {
            Transform exit = Find(interior, "exit_" + id);
            if (exit == null)
            {
                EditorPaths.Report("屋内 " + id + " に exit_" + id + " がありません。");
                return;
            }

            var go = new GameObject("Exit_" + id);
            go.transform.SetParent(interior, false);
            go.transform.position = exit.position + Vector3.up * 1.2f;
            go.transform.rotation = Quaternion.identity;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 2.6f, 3f);
            go.AddComponent<InteriorExit>().BuildingId = id;
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

        /// <summary>入った直後の立ち位置と、外の入口の位置を InteriorLoader に教える。</summary>
        private static void Register(InteriorLoader loader, Transform interior, string id)
        {
            Transform spawn = Find(interior, "spawn_" + id);
            Transform exit = Find(interior, "exit_" + id);
            if (spawn == null)
            {
                EditorPaths.Report("屋内 " + id + " に spawn_" + id + " がありません。");
                return;
            }

            // 立ち位置は扉から建物の奥を向く。
            Vector3 inward = exit != null ? spawn.position - exit.position : interior.forward;
            inward.y = 0f;
            float yaw = inward.sqrMagnitude > 0.01f ? Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg : 0f;

            var point = new GameObject("Spawn_" + id);
            point.transform.SetParent(interior, false);
            point.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));

            GameObject entrance = GameObject.Find("Entrance_" + id);
            loader.Register(new InteriorLoader.Entry
            {
                Id = id,
                DisplayName = CampusProps.DisplayName(id),
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
