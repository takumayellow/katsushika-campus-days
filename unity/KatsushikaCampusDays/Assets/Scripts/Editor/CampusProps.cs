using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャンパスに置く目印。入口トリガー・建物の名札・クエストの到達判定・拾い物。
    /// 座標は Blender 側と同じローカル軸 (u = 第1研究棟の長手, v = 直交) で書く。
    /// </summary>
    public static class CampusProps
    {
        /// <summary>第1研究棟の長辺から決めたキャンパスの向き。build_campus.make_frame と同じ値。</summary>
        private static readonly Vector2 AxisU = new Vector2(0.90958899f, -0.41550916f);

        /// <summary>モール側（+v）を指す向き。店舗サインの表を決めるのに使う。</summary>
        private static Vector3 AxisV => new Vector3(-AxisU.y, 0f, AxisU.x);

        private static readonly Dictionary<string, string> BuildingNames = new Dictionary<string, string>
        {
            { "research1", "第1研究棟" },
            { "research2", "第2研究棟" },
            { "lecture", "講義棟" },
            { "kyoso", "共創棟" },
            { "library", "図書館" },
            { "gym", "体育館" },
            { "lab1", "第1実験棟" },
            { "lab2", "第2実験棟" },
            { "greenhouse", "温室" },
            { "starbucks_green", "スターバックス" },
            { "familymart_green", "ファミリーマート" }
        };

        /// <summary>campus.json の footprint 重心。入口の外向きを決めるのに使う。</summary>
        private static readonly Dictionary<string, Vector2> Centroids = new Dictionary<string, Vector2>
        {
            { "research1", new Vector2(76.6f, -101.7f) },
            { "research2", new Vector2(-2.9f, 8.0f) },
            { "lecture", new Vector2(118.3f, -40.4f) },
            { "kyoso", new Vector2(112.3f, -96.5f) },
            { "library", new Vector2(-93.6f, 25.7f) },
            { "gym", new Vector2(28.1f, 48.7f) },
            { "lab1", new Vector2(64.9f, 25.4f) },
            { "lab2", new Vector2(84.9f, 43.5f) },
            { "greenhouse", new Vector2(111.5f, 22.8f) }
        };

        /// <summary>にいじゅくみらい公園に散らす葉の位置。建物の footprint を避けてある。</summary>
        private static readonly Vector2[] LeafSpots =
        {
            new Vector2(-95f, -80f),
            new Vector2(-72f, -96f),
            new Vector2(-50f, -105f),
            new Vector2(-84f, -112f),
            new Vector2(-108f, -98f)
        };

        /// <summary>建物の表示名。未登録なら id をそのまま返す。</summary>
        public static string DisplayName(string buildingId)
        {
            return BuildingNames.TryGetValue(buildingId, out string name) ? name : buildingId;
        }

        /// <summary>
        /// 正門前の広場（u 197..211 の石畳）の外側の端。理科大通りからモールへ向かって立つ。
        /// 正門ゾーン gate_main は u 183..207 なので, その中 (u=186) に置くと最初の目標が背中側になり
        /// カメラもゾーンの中から始まる (#37)。u=210 なら正門とモールが正面, 4.2 m 後ろのカメラ (u≈214) もゾーンの外。
        /// </summary>
        public static Vector3 PlayerSpawn => Ground(Local(PlayerSpawnU, -24f));

        /// <summary>スポーンの u 座標。gate_main ゾーンの端 (207) より外, 広場の端 (211) より内。</summary>
        public const float PlayerSpawnU = 210f;

        /// <summary>モールの反対側（図書館側）を向く角度。</summary>
        public static float PlayerYaw => Mathf.Atan2(-AxisU.x, -AxisU.y) * Mathf.Rad2Deg;

        /// <summary>入口・名札・到達判定・拾い物をまとめて置く。</summary>
        public static void Build(Transform root)
        {
            var props = new GameObject("Props");
            props.transform.SetParent(root, false);

            PlaceEntrances(props.transform);
            PlaceSigns(props.transform);
            PlaceZones(props.transform);
            PlaceCollectables(props.transform);
        }

        /// <summary>ローカル軸 (u, v) をワールドの xz に直す。</summary>
        public static Vector2 Local(float u, float v)
        {
            return new Vector2(AxisU.x * u - AxisU.y * v, AxisU.y * u + AxisU.x * v);
        }

        /// <summary>地面に降ろした位置。当たり判定が無ければ y = 0。</summary>
        public static Vector3 Ground(Vector2 xz)
        {
            var from = new Vector3(xz.x, 60f, xz.y);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return new Vector3(xz.x, 0f, xz.y);
        }

        /// <summary>入口の外側。建物の重心から入口へ伸ばした向きへ distance だけ離す。</summary>
        public static Vector3 Outward(string buildingId, float distance, Vector2 fallback)
        {
            Transform entrance = CampusStage.FindChild("entrance_" + buildingId);
            if (entrance == null)
            {
                return Ground(fallback);
            }

            Vector3 position = entrance.position;
            var flat = new Vector2(position.x, position.z);

            Vector2 direction = Centroids.TryGetValue(buildingId, out Vector2 centroid)
                ? (flat - centroid).normalized
                : Vector2.zero;

            if (direction.sqrMagnitude < 0.01f)
            {
                direction = -AxisU;
            }

            return Ground(flat + direction * distance);
        }

        /// <summary>FBX の entrance_&lt;id&gt; に「入った」判定の箱を付ける。</summary>
        private static void PlaceEntrances(Transform parent)
        {
            int count = 0;
            foreach (KeyValuePair<string, Vector2> pair in Centroids)
            {
                Transform entrance = CampusStage.FindChild("entrance_" + pair.Key);
                if (entrance == null)
                {
                    continue;
                }

                var go = new GameObject("Entrance_" + pair.Key);
                go.transform.SetParent(parent, false);
                go.transform.position = entrance.position + Vector3.up * 1.2f;

                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(6f, 3.4f, 6f);

                EntranceTrigger trigger = go.AddComponent<EntranceTrigger>();
                trigger.BuildingId = pair.Key;
                trigger.DisplayName = BuildingNames[pair.Key];
                count++;
            }

            EditorPaths.Report("入口トリガーを " + count + " 個置きました。");
        }

        /// <summary>FBX の sign_&lt;id&gt; に浮かぶ名札を付ける。</summary>
        private static void PlaceSigns(Transform parent)
        {
            TMP_FontAsset font = FontLibrary.Ensure();
            int count = 0;

            foreach (KeyValuePair<string, string> pair in BuildingNames)
            {
                if (!SignAnchor(pair.Key, out Vector3 anchor))
                {
                    continue;
                }

                var go = new GameObject("Label_" + pair.Key);
                go.transform.SetParent(parent, false);
                go.transform.position = anchor;

                TextMeshPro text = go.AddComponent<TextMeshPro>();
                text.text = pair.Value;
                text.fontSize = 3.2f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(1f, 1f, 1f, 0.92f);
                text.outlineWidth = 0.18f;
                text.outlineColor = new Color32(24, 30, 40, 220);
                if (font != null)
                {
                    text.font = font;
                }

                text.rectTransform.sizeDelta = new Vector2(12f, 3f);
                go.AddComponent<BuildingLabel>().Label = pair.Value;
                count++;
            }

            EditorPaths.Report("建物の名札を " + count + " 個置きました。");
        }

        /// <summary>
        /// 名札を吊るす位置。sign_&lt;id&gt; が無い建物は入口の上に、
        /// 店舗は色板（文字の無い看板）の手前に出す。
        /// </summary>
        private static bool SignAnchor(string buildingId, out Vector3 position)
        {
            Transform sign = CampusStage.FindChild("sign_" + buildingId);
            if (sign != null)
            {
                position = sign.position + Vector3.up * 2.6f;
                return true;
            }

            Transform entrance = CampusStage.FindChild("entrance_" + buildingId);
            if (entrance != null)
            {
                position = entrance.position + Vector3.up * 6f;
                return true;
            }

            if (PlateCenter("sign_" + buildingId, out Vector3 plate))
            {
                position = plate + AxisV * 0.9f;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>指定マテリアルが貼られたサブメッシュの中心をワールド座標で返す。</summary>
        private static bool PlateCenter(string materialName, out Vector3 center)
        {
            center = Vector3.zero;
            GameObject campus = CampusStage.Instance;
            if (campus == null)
            {
                return false;
            }

            foreach (MeshRenderer renderer in campus.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length && i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || materials[i].name != materialName)
                    {
                        continue;
                    }

                    center = renderer.transform.TransformPoint(mesh.GetSubMesh(i).bounds.center);
                    return true;
                }
            }

            return false;
        }

        /// <summary>クエストの visit ステップが見ている場所。</summary>
        private static void PlaceZones(Transform parent)
        {
            Zone(parent, "gate_main", "理科大通り 正門", Local(195f, -24f), new Vector3(24f, 8f, 18f));
            Zone(parent, "campus_mall", "キャンパスモール", Local(70f, -24f), new Vector3(60f, 8f, 14f));
            Zone(parent, "mall_bench", "モールのベンチ", Local(69f, -26.6f), new Vector3(10f, 6f, 10f));
            Zone(parent, "library_pond", "図書館の水盤", Local(-30f, -31.6f), new Vector3(58f, 8f, 16f));
        }

        private static void Zone(Transform parent, string placeId, string displayName, Vector2 xz, Vector3 size)
        {
            var go = new GameObject("Zone_" + placeId);
            go.transform.SetParent(parent, false);
            go.transform.position = Ground(xz) + Vector3.up * (size.y * 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(AxisU.x, AxisU.y) * Mathf.Rad2Deg, 0f);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;

            VisitZone zone = go.AddComponent<VisitZone>();
            zone.PlaceId = placeId;
            zone.DisplayName = displayName;
        }

        /// <summary>拾い物。牛乳は共創棟の売店前、葉は公園に 5 枚。</summary>
        private static void PlaceCollectables(Transform parent)
        {
            Vector3 shop = Outward("kyoso", 4.5f, new Vector2(112.3f, -78f));
            ActorFactory.CreateCollectable(parent, "milk", "牛乳", shop + Vector3.up * 0.4f, "white");

            foreach (Vector2 spot in LeafSpots)
            {
                Vector3 position = Ground(spot) + Vector3.up * 0.45f;
                ActorFactory.CreateCollectable(parent, "leaf", "理科大グリーンの葉", position, "leaf");
            }

            EditorPaths.Report("拾い物を " + (LeafSpots.Length + 1) + " 個置きました。");
        }
    }
}
