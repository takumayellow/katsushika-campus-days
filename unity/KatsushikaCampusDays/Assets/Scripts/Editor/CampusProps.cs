using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャンパスに置く目印。入口トリガー・入口の立て看板の名前・クエストの到達判定・拾い物。
    /// 座標は Blender 側と同じローカル軸 (u = 第1研究棟の長手, v = 直交) で書く。
    /// 看板の板に貼る文字は CampusProps.SignFaces.cs。
    /// </summary>
    public static partial class CampusProps
    {
        /// <summary>第1研究棟の長辺から決めたキャンパスの向き。build_campus.make_frame と同じ値。</summary>
        private static readonly Vector2 AxisU = new Vector2(0.90958899f, -0.41550916f);

        private static readonly Dictionary<string, string> BuildingNames = new Dictionary<string, string>
        {
            { "research1", "第1研究棟" },
            { "research2", "第2研究棟" },
            { "cafeteria", "食堂" },
            { "lecture", "講義棟" },
            { "kyoso", "共創棟" },
            { "library", "図書館" },
            { "gym", "体育館" },
            { "lab1", "第1実験棟" },
            { "lab2", "第2実験棟" },
            { "greenhouse", "温室" }
        };

        /// <summary>
        /// campus.json の footprint 重心。入口を置く建物の一覧を兼ねる。
        /// 外向きは FBX の door_ → entrance_ で決まるので、重心は door_ の無い古い FBX のときだけ使う。
        /// </summary>
        private static readonly Dictionary<string, Vector2> Centroids = new Dictionary<string, Vector2>
        {
            { "research1", new Vector2(76.6f, -101.7f) },
            { "research2", new Vector2(-2.9f, 8.0f) },
            { "cafeteria", new Vector2(-2.9f, 8.0f) },
            { "lecture", new Vector2(118.3f, -40.4f) },
            { "kyoso", new Vector2(-24.2f, -54.3f) },
            { "library", new Vector2(-93.6f, 25.7f) },
            { "gym", new Vector2(28.1f, 48.7f) },
            { "lab1", new Vector2(64.9f, 25.4f) },
            { "lab2", new Vector2(84.9f, 43.5f) },
            { "greenhouse", new Vector2(111.5f, 22.8f) }
        };

        /// <summary>にいじゅくみらい公園に散らす葉の位置。建物の footprint と池・せせらぎ（data/osm/park.json）を避けてある。</summary>
        private static readonly Vector2[] LeafSpots =
        {
            new Vector2(-95f, -80f),
            new Vector2(-75f, -97f),
            new Vector2(-50f, -105f),
            new Vector2(-84f, -112f),
            new Vector2(-104f, -97f)
        };

        /// <summary>建物の表示名。未登録なら id をそのまま返す。</summary>
        public static string DisplayName(string buildingId)
        {
            return BuildingNames.TryGetValue(buildingId, out string name) ? name : buildingId;
        }

        /// <summary>
        /// 正門前の広場（u 197..211 の石畳）の外側の端。理科大通りからメインストリートへ向かって立つ。
        /// 正門ゾーン gate_main は u 183..207 なので, その中 (u=186) に置くと最初の目標が背中側になり
        /// カメラもゾーンの中から始まる (#37)。u=210 なら正門とメインストリートが正面, 4.2 m 後ろのカメラ (u≈214) もゾーンの外。
        /// </summary>
        public static Vector3 PlayerSpawn => Ground(Local(PlayerSpawnU, -24f));

        /// <summary>スポーンの u 座標。gate_main ゾーンの端 (207) より外, 広場の端 (211) より内。</summary>
        public const float PlayerSpawnU = 210f;

        /// <summary>メインストリートの反対側（図書館側）を向く角度。</summary>
        public static float PlayerYaw => Mathf.Atan2(-AxisU.x, -AxisU.y) * Mathf.Rad2Deg;

        /// <summary>入口・看板の名前・到達判定・拾い物をまとめて置く。</summary>
        public static void Build(Transform root)
        {
            var props = new GameObject("Props");
            props.transform.SetParent(root, false);

            PlaceEntrances(props.transform);
            PlaceSignFaces(props.transform, FontLibrary.Ensure());
            PlaceZones(props.transform);
            PlaceCollectables(props.transform);
        }

        /// <summary>ローカル軸 (u, v) をワールドの xz に直す。</summary>
        public static Vector2 Local(float u, float v)
        {
            return new Vector2(AxisU.x * u - AxisU.y * v, AxisU.y * u + AxisU.x * v);
        }

        /// <summary>
        /// ローカル軸 (u, v) を向く回転。ローカル +X が u、+Z が v を向く。
        /// 親をこれで回しておけば、子の localPosition に (u, y, v) をそのまま入れられて
        /// <see cref="Local"/> と同じワールド座標になる。
        /// </summary>
        public static Quaternion LocalRotation =>
            Quaternion.Euler(0f, Mathf.Atan2(-AxisU.y, AxisU.x) * Mathf.Rad2Deg, 0f);

        /// <summary>
        /// 地面に降ろした位置。当たり判定が無ければ y = 0。
        ///
        /// 水面と水盤の見えない壁（CampusStage.IsNonGroundCollider）は地面として数えない。
        /// 数えてしまうと、池のそばのベンチや小物が水の上や壁の天端に載る（#46）。
        /// </summary>
        public static Vector3 Ground(Vector2 xz)
        {
            var from = new Vector3(xz.x, 60f, xz.y);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            var best = new Vector3(xz.x, 0f, xz.y);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || CampusStage.IsNonGroundCollider(hit.collider.gameObject.name))
                {
                    continue;
                }

                if (!found || hit.point.y > best.y)
                {
                    best = hit.point;
                    found = true;
                }
            }

            return best;
        }

        /// <summary>入口の人や物を扉の前の通り道から横へ逃がす距離（m）。石張りの半幅 3.4 m と看板より外。</summary>
        private const float EntranceSideStep = 5f;

        /// <summary>
        /// 入口の外側。door_ と entrance_ があれば、entrance_（扉の前の床）から扉の正面の向きへ distance だけ離し、
        /// 看板と反対の側（自販機の側）へ <see cref="EntranceSideStep"/> m ずらす。扉の前の通り道は空けておく (#39)。
        /// 無ければ建物の重心から入口へ伸ばした向きへ distance だけ離す。
        /// </summary>
        public static Vector3 Outward(string buildingId, float distance, Vector2 fallback)
        {
            Transform entrance = CampusStage.FindChild("entrance_" + buildingId);
            if (entrance == null)
            {
                return Ground(fallback);
            }

            if (CampusStage.FindChild("door_" + buildingId) != null && DoorFrame(buildingId, out Vector3 door, out Vector3 outward))
            {
                // 看板と反対の側。看板の Empty が無ければ扉から見て右。
                var right = new Vector3(outward.z, 0f, -outward.x);
                Transform sign = CampusStage.FindChild("sign_" + buildingId);
                float side = sign != null && Vector3.Dot(sign.position - door, right) > 0f ? -1f : 1f;

                Vector3 spot = entrance.position + outward * distance + right * (side * EntranceSideStep);
                return Ground(new Vector2(spot.x, spot.z));
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

        /// <summary>
        /// 入口の扉。door_&lt;id&gt;（扉の外面の中心, kcd_lib.entrances）と、そこから外を指す水平な単位ベクトル。
        /// 外向きは door_ → entrance_（扉の前の床）。door_ が無い古い FBX では entrance_ を扉とみなし、
        /// 外向きは建物の重心から推す。どちらも無ければ false。
        /// 扉の前には庇があるので Ground()（上からのレイ）は使わない。Empty の高さがそのまま石張りの天端。
        /// </summary>
        private static bool DoorFrame(string buildingId, out Vector3 door, out Vector3 outward)
        {
            Transform doorMark = CampusStage.FindChild("door_" + buildingId);
            Transform entrance = CampusStage.FindChild("entrance_" + buildingId);
            door = Vector3.zero;
            outward = Vector3.zero;
            if (doorMark == null && entrance == null)
            {
                return false;
            }

            if (doorMark != null && entrance != null)
            {
                outward = entrance.position - doorMark.position;
            }
            else if (entrance != null && Centroids.TryGetValue(buildingId, out Vector2 centroid))
            {
                outward = new Vector3(entrance.position.x - centroid.x, 0f, entrance.position.z - centroid.y);
            }

            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f)
            {
                outward = new Vector3(-AxisU.x, 0f, -AxisU.y);
            }

            outward.Normalize();
            door = doorMark != null ? doorMark.position : entrance.position;
            return true;
        }

        /// <summary>
        /// 入口トリガーを扉の外面（door_&lt;id&gt;）に置き、正面を建物の外へ向ける。Interactable レイヤーに入れるので
        /// 扉の前で「[E] ◯◯に入る」が出る。判定箱は扉の前 1.6 m（幅 4 m、温室は 2.2 m）で、扉へ向かって歩いて来ても入れる (#39)。
        /// </summary>
        private static void PlaceEntrances(Transform parent)
        {
            int layer = LayerMask.NameToLayer("Interactable");
            int count = 0;
            int legacy = 0;
            foreach (string id in Centroids.Keys)
            {
                if (!DoorFrame(id, out Vector3 door, out Vector3 outward))
                {
                    continue;
                }

                var go = new GameObject("Entrance_" + id);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(door, Quaternion.LookRotation(outward, Vector3.up));
                if (layer >= 0)
                {
                    go.layer = layer;
                }

                BoxCollider box = go.AddComponent<BoxCollider>();

                EntranceTrigger trigger = go.AddComponent<EntranceTrigger>();
                trigger.BuildingId = id;
                trigger.DisplayName = BuildingNames[id];
                trigger.InteractionRange = EntranceTrigger.DefaultRange;
                trigger.WalkInHalfWidth = EntranceTrigger.WalkInHalfWidthFor(id);
                trigger.RefreshLabel();

                // 箱は EntranceTrigger を足したあとに入れる。AddComponent は Reset() を呼ぶので、
                // 先に入れると既定の 4x3x2 に巻き戻る（#54）。
                // 温室の入口は開口 1.8 m と狭いので、箱と歩き入りの幅を入口ごとに変える（ふつうは幅 4 m / 半幅 1.2 m）。
                box.isTrigger = true;
                box.center = EntranceTrigger.BoxCenter;
                box.size = EntranceTrigger.BoxSizeFor(id);
                count++;
                if (CampusStage.FindChild("door_" + id) == null)
                {
                    legacy++;
                }
            }

            EditorPaths.Report("入口トリガーを " + count + " 個置きました。");
            if (legacy > 0)
            {
                EditorPaths.Report("door_<id> の無い入口が " + legacy + " 個あります。campus.fbx を作り直してください（blender/build_campus.py）。");
            }
        }

        /// <summary>クエストの visit ステップが見ている場所。</summary>
        private static void PlaceZones(Transform parent)
        {
            Zone(parent, "gate_main", "理科大通り 正門", Local(195f, -24f), new Vector3(24f, 8f, 18f));
            Zone(parent, "campus_mall", "メインストリート", Local(70f, -24f), new Vector3(60f, 8f, 14f));
            Zone(parent, "mall_bench", "メインストリートのベンチ", Local(69f, -26.6f), new Vector3(10f, 6f, 10f));
            // 堀の東の縁石と芝生広場（u -54..-40, v -56..-28）。水面には入れないので岸側に取る。
            Zone(parent, "library_pond", "図書館の水盤", Local(-47f, -42f), new Vector3(14f, 8f, 28f));
        }

        private static void Zone(Transform parent, string placeId, string displayName, Vector2 xz, Vector3 size)
        {
            var go = new GameObject("Zone_" + placeId);
            go.transform.SetParent(parent, false);
            go.transform.position = Ground(xz) + Vector3.up * (size.y * 0.5f);
            // size.x を u 方向、size.z を v 方向に取る（以前は Atan2(Ux, Uy) で x が v 方向を向いていた）。
            go.transform.rotation = LocalRotation;

            BoxCollider box = go.AddComponent<BoxCollider>();

            VisitZone zone = go.AddComponent<VisitZone>();
            zone.PlaceId = placeId;
            zone.DisplayName = displayName;

            // 大きさは VisitZone を足したあとに入れる。AddComponent は Reset() を呼ぶので、
            // 先に入れると既定値に巻き戻る（#54）。
            box.isTrigger = true;
            box.size = size;
        }

        /// <summary>売店前のワゴンの天板の高さ [m]。</summary>
        private const float MilkStandHeight = 0.75f;

        /// <summary>ワゴンの天板の大きさ [m]（u 方向の幅, 厚さ, v 方向の奥行き）。</summary>
        private static readonly Vector3 MilkStandTop = new Vector3(0.7f, 0.04f, 0.45f);

        /// <summary>
        /// 拾い物。牛乳は共創棟の売店前のワゴンの上、葉は公園に 5 枚。
        /// 牛乳は売り物なので、宝石や葉のように宙で揺らさず天板に立てておく (#186)。
        /// </summary>
        private static void PlaceCollectables(Transform parent)
        {
            Vector3 shop = Outward("kyoso", 4.5f, new Vector2(-12.5f, -32.0f));
            PlaceMilkStand(parent, shop);
            GameObject milk = ActorFactory.CreateCollectable(parent, "milk", "牛乳",
                shop + Vector3.up * (MilkStandHeight + CollectableMeshes.MilkCartonHalfHeight), "white");
            milk.GetComponent<CollectableItem>().Floats = false;

            foreach (Vector2 spot in LeafSpots)
            {
                Vector3 position = Ground(spot) + Vector3.up * 0.45f;
                ActorFactory.CreateCollectable(parent, "leaf", "理科大グリーンの葉", position, "leaf");
            }

            EditorPaths.Report("拾い物を " + (LeafSpots.Length + 1) + " 個置きました。");
        }

        /// <summary>売店前のワゴン。白い天板と灰色の脚 4 本。当たり判定は全体を包む箱 1 つ。</summary>
        private static void PlaceMilkStand(Transform parent, Vector3 ground)
        {
            var stand = new GameObject("MilkStand");
            stand.transform.SetParent(parent, false);
            stand.transform.SetPositionAndRotation(ground, LocalRotation);

            Material board = MaterialLibrary.EnsureCampus("white");
            Material frame = MaterialLibrary.EnsureCampus("metal_grey");
            StandPart(stand.transform, "top", new Vector3(0f, MilkStandHeight - MilkStandTop.y * 0.5f, 0f), MilkStandTop, board);

            const float leg = 0.04f;
            float legHeight = MilkStandHeight - MilkStandTop.y;
            float x = (MilkStandTop.x - leg) * 0.5f;
            float z = (MilkStandTop.z - leg) * 0.5f;
            // i の下位ビットで左右、上位ビットで手前と奥を選んで 4 隅に立てる。
            for (int i = 0; i < 4; i++)
            {
                var at = new Vector3(i % 2 == 0 ? -x : x, legHeight * 0.5f, i < 2 ? -z : z);
                StandPart(stand.transform, "leg" + i, at, new Vector3(leg, legHeight, leg), frame);
            }

            BoxCollider box = stand.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, MilkStandHeight * 0.5f, 0f);
            box.size = new Vector3(MilkStandTop.x, MilkStandHeight, MilkStandTop.z);
        }

        /// <summary>当たり判定を外した箱。ワゴンの判定は根元の BoxCollider がまとめて持つ。</summary>
        private static void StandPart(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
