using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// collectibles.json の隠しアイテム（source = hidden の 20 個）と写真スポット（6 箇所）をシーンに置く (#65)。
    ///
    /// これまでは一覧だけがあってシーンに何も無く、結果画面の「隠しアイテム」と「写真」は 0 から動かず、
    /// サブクエ「写真散歩」（q_sq_photo_walk）の visit 先 ps_* と「実験ノート」（q_sq_lab_notebook）の
    /// ゴーグル c_lab_goggles も見つからなかった。
    ///
    /// 置き方:
    ///   隠しアイテム  レア度の色の宝石（CollectableMeshes の Gem）。E で拾うと DayStats が数える。
    ///   写真スポット  三脚（PhotoSpot、E で look_dir へ向いて撮る）と、同じ場所の VisitZone（PlaceId = ps_*）。
    /// 屋外は campus.json と同じ (x, z) の地面、屋内は Interior_&lt;building&gt; の poi_ の床。
    /// 屋内の poi を使うので InteriorStage.Build のあとに呼ぶ。
    /// </summary>
    public static class CollectibleStage
    {
        public const string CatalogJson = "Assets/Data/Collectibles/collectibles.json";

        /// <summary>屋外の隠しアイテムを入れる入れ物の名前。屋内の物は Interior_&lt;building&gt; の下に入れる。</summary>
        public const string ItemsGroup = "Collectibles";

        /// <summary>屋外の写真スポットを入れる入れ物の名前。</summary>
        public const string PhotoSpotsGroup = "PhotoSpots";

        /// <summary>三脚の名前の接頭辞（PhotoSpot_ps_mall_view など）。</summary>
        public const string PhotoSpotPrefix = "PhotoSpot_";

        /// <summary>宝石の中心を床からどれだけ浮かせるか（m）。上下に 0.12 m 揺れても床に潜らない。</summary>
        public const float ItemLift = 0.45f;

        /// <summary>写真スポットの visit 判定の箱（m）。人が 1 人立てば必ず入る広さ。</summary>
        public static readonly Vector3 PhotoZoneSize = new Vector3(5f, 3f, 5f);

        /// <summary>visit 判定の箱の中心の高さ（m、床から）。下端が床より 0.3 m 下に来る。</summary>
        private const float PhotoZoneLift = 1.2f;

        /// <summary>三脚に近づいて E が出る距離（m、水平）。</summary>
        private const float TripodRange = 2.5f;

        /// <summary>
        /// 屋外で、地面（Ground レイヤー）から何 m 上までの面に載せてよいか。ベンチの座面や植え込みの天端には
        /// 載せるが、門の庇や建物の軒の上には載せない（手が届かず、下から見えない）。
        /// </summary>
        private const float OutdoorStepUp = 1.2f;

        /// <summary>屋内で、poi の高さから上下にどれだけ離れた面までを床とみなすか（m）。机の天板は拾わない。</summary>
        private const float IndoorFloorTolerance = 0.5f;

        /// <summary>写真スポットと同じ所にある隠しアイテムを横へずらす距離（m）。三脚の円盤（半径 0.55）の外。</summary>
        private const float SideStep = 0.9f;

        /// <summary>写真スポットからこれより近い隠しアイテムは横へずらす（m、水平）。</summary>
        private const float SpotClearance = 1f;

        /// <summary>上向きの面とみなす法線の y。壁や柱の側面を床にしない。</summary>
        private const float MinFloorNormalY = 0.5f;

        private struct Anchor
        {
            public Vector3 Floor;
            public Vector2 Look;
            public Transform Interior;
        }

        /// <summary>全部置いて、置けなかった id を報告する。</summary>
        public static void Build(Transform root)
        {
            string path = EditorPaths.Absolute(CatalogJson);
            if (!File.Exists(path))
            {
                EditorPaths.Report("収集物の一覧がありません: " + CatalogJson);
                return;
            }

            CollectibleCatalog catalog = CollectibleCatalog.Parse(File.ReadAllText(path));
            Physics.SyncTransforms();

            Transform interiors = root.Find("Interiors");
            Transform spotsGroup = NewGroup(root, PhotoSpotsGroup);
            Transform itemsGroup = NewGroup(root, ItemsGroup);

            var anchors = new List<Anchor>();
            var missing = new List<string>();
            foreach (CatalogPhotoSpot spot in catalog.PhotoSpots)
            {
                if (!TryResolve(spot.Place, interiors, spotsGroup, out Transform parent, out Transform interior,
                        out Vector3 floor))
                {
                    missing.Add(spot.Id);
                    continue;
                }

                AddPhotoZone(parent, spot, floor);
                AddTripod(parent, spot, floor);
                anchors.Add(new Anchor { Floor = floor, Look = spot.LookDir, Interior = interior });
            }

            int items = 0;
            foreach (CatalogItem item in catalog.Items)
            {
                if (!item.IsHidden)
                {
                    continue;
                }

                if (!TryResolve(item.Place, interiors, itemsGroup, out Transform parent, out Transform interior,
                        out Vector3 floor))
                {
                    missing.Add(item.Id);
                    continue;
                }

                floor = AwayFromSpots(floor, interior, anchors);
                ActorFactory.CreateCollectable(parent, item.Id, item.NameJa, floor + Vector3.up * ItemLift,
                    "gem_" + item.Rarity);
                items++;
            }

            EditorPaths.Report("隠しアイテムを " + items + "/" + catalog.HiddenCount + " 個、写真スポットを "
                + anchors.Count + "/" + catalog.PhotoSpotCount + " 箇所置きました。");
            if (missing.Count > 0)
            {
                EditorPaths.Report("置き場所が見つからない収集物: " + string.Join(", ", missing));
            }
        }

        private static Transform NewGroup(Transform root, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(root, false);
            return group.transform;
        }

        /// <summary>
        /// 置き場所をシーンの床の位置にする。屋外は outdoorParent の下、屋内は Interior_&lt;building&gt; の下。
        /// interior は屋内ならその入れ物、屋外なら null。
        /// </summary>
        private static bool TryResolve(CatalogPlace place, Transform interiors, Transform outdoorParent,
            out Transform parent, out Transform interior, out Vector3 floor)
        {
            parent = null;
            interior = null;
            floor = Vector3.zero;

            if (place.IsOutdoor)
            {
                parent = outdoorParent;
                floor = OutdoorFloor(place.XZ);
                return true;
            }

            if (!place.IsIndoor || interiors == null)
            {
                return false;
            }

            interior = interiors.Find(QuestObjectiveLocator.InteriorPrefix + place.Building);
            Transform poi = interior != null ? FindDeep(interior, place.Poi) : null;
            if (poi == null)
            {
                interior = null;
                return false;
            }

            parent = interior;
            floor = IndoorFloor(poi.position);
            return true;
        }

        /// <summary>
        /// 屋外の床。まず地面（Ground レイヤーのいちばん上）を探し、そこから <see cref="OutdoorStepUp"/> m
        /// までの上向きの面があればその上に載せる。地面が無ければ CampusProps.Ground と同じ。
        /// </summary>
        public static Vector3 OutdoorFloor(Vector2 xz)
        {
            var from = new Vector3(xz.x, 60f, xz.y);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
            int groundLayer = LayerMask.NameToLayer("Ground");

            bool terrainFound = false;
            float terrain = 0f;
            foreach (RaycastHit hit in hits)
            {
                if (IsFloorCandidate(hit) && hit.collider.gameObject.layer == groundLayer
                    && (!terrainFound || hit.point.y > terrain))
                {
                    terrain = hit.point.y;
                    terrainFound = true;
                }
            }

            if (!terrainFound)
            {
                return CampusProps.Ground(xz);
            }

            var best = new Vector3(xz.x, terrain, xz.y);
            foreach (RaycastHit hit in hits)
            {
                if (IsFloorCandidate(hit) && hit.normal.y >= MinFloorNormalY
                    && hit.point.y > best.y && hit.point.y <= terrain + OutdoorStepUp)
                {
                    best = hit.point;
                }
            }

            return best;
        }

        /// <summary>
        /// 屋内の床。poi の少し上から下へ探し、poi の高さから ±<see cref="IndoorFloorTolerance"/> m の
        /// 上向きの面のうちいちばん上。2 階の回廊や観覧席の最上段の poi はその床になる。無ければ poi そのもの。
        /// </summary>
        public static Vector3 IndoorFloor(Vector3 poi)
        {
            return TryIndoorFloor(poi, out Vector3 floor) ? floor : poi;
        }

        /// <summary><see cref="IndoorFloor"/> の本体。床が見つからなければ false（吹き抜けの上など）。</summary>
        private static bool TryIndoorFloor(Vector3 poi, out Vector3 floor)
        {
            const float lift = 1.2f;
            RaycastHit[] hits = Physics.RaycastAll(poi + Vector3.up * lift, Vector3.down, lift + 3f,
                ~0, QueryTriggerInteraction.Ignore);

            floor = poi;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (!IsFloorCandidate(hit) || hit.normal.y < MinFloorNormalY
                    || Mathf.Abs(hit.point.y - poi.y) > IndoorFloorTolerance)
                {
                    continue;
                }

                if (!found || hit.point.y > floor.y)
                {
                    floor = hit.point;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 床として数えてよい当たり判定か。水面・水盤の見えない壁・木の幹・人・拾い物は数えない。
        /// </summary>
        private static bool IsFloorCandidate(RaycastHit hit)
        {
            Collider collider = hit.collider;
            if (collider == null || collider.isTrigger)
            {
                return false;
            }

            GameObject go = collider.gameObject;
            if (CampusStage.IsNonGroundCollider(go.name) || go.name == CampusStage.TreeTrunkName)
            {
                return false;
            }

            return go.layer != LayerMask.NameToLayer("Interactable")
                && go.layer != LayerMask.NameToLayer("NPC")
                && go.layer != LayerMask.NameToLayer("Player");
        }

        /// <summary>
        /// 写真スポットと同じ所の隠しアイテム（モールのピンと ps_mall_view、ドームの鍵と ps_library_gallery）を、
        /// 撮る向きに対して横へ <see cref="SideStep"/> m ずらす。三脚とアイテムが重なると E の対象が取り合いになる。
        /// ずらした先に床が無い・床の高さが変わる（段差・吹き抜けの縁）・壁の向こう、のどれかなら反対側を試し、
        /// どちらもだめならそのまま。
        /// </summary>
        private static Vector3 AwayFromSpots(Vector3 floor, Transform interior, List<Anchor> anchors)
        {
            foreach (Anchor anchor in anchors)
            {
                if (anchor.Interior != interior)
                {
                    continue;
                }

                var delta = new Vector2(floor.x - anchor.Floor.x, floor.z - anchor.Floor.z);
                if (delta.sqrMagnitude >= SpotClearance * SpotClearance)
                {
                    continue;
                }

                Vector2 side = anchor.Look.sqrMagnitude > 1e-6f
                    ? new Vector2(-anchor.Look.y, anchor.Look.x).normalized
                    : Vector2.right;
                var origin = new Vector2(anchor.Floor.x, anchor.Floor.z);
                foreach (float sign in new[] { 1f, -1f })
                {
                    Vector2 xz = origin + side * (SideStep * sign);
                    if (TrySideFloor(xz, floor, interior, out Vector3 candidate)
                        && !Physics.Linecast(anchor.Floor + Vector3.up * ItemLift, candidate + Vector3.up * ItemLift,
                            ~0, QueryTriggerInteraction.Ignore))
                    {
                        return candidate;
                    }
                }

                return floor;
            }

            return floor;
        }

        /// <summary>横へずらした先の床。床が無いか、元の床から <see cref="IndoorFloorTolerance"/> m より高さが違えば false。</summary>
        private static bool TrySideFloor(Vector2 xz, Vector3 floor, Transform interior, out Vector3 candidate)
        {
            if (interior != null)
            {
                if (!TryIndoorFloor(new Vector3(xz.x, floor.y, xz.y), out candidate))
                {
                    return false;
                }
            }
            else
            {
                candidate = OutdoorFloor(xz);
            }

            return Mathf.Abs(candidate.y - floor.y) <= IndoorFloorTolerance;
        }

        /// <summary>入ると q_sq_photo_walk の visit が進み、「写真スポット: ◯◯」が出る箱。</summary>
        private static void AddPhotoZone(Transform parent, CatalogPhotoSpot spot, Vector3 floor)
        {
            var go = new GameObject("Zone_" + spot.Id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(floor + Vector3.up * PhotoZoneLift, Quaternion.identity);
            BoxCollider box = go.AddComponent<BoxCollider>();
            VisitZone zone = go.AddComponent<VisitZone>();
            zone.PlaceId = spot.Id;
            zone.DisplayName = spot.NameJa;

            // 大きさは VisitZone を足したあとに入れる。AddComponent は Reset() を呼ぶので、
            // 先に入れると既定の 10x6x10 に巻き戻る（#54）。
            box.isTrigger = true;
            box.size = PhotoZoneSize;
        }

        /// <summary>E で撮る三脚。+Z（カメラのレンズ）を look_dir へ向ける。</summary>
        private static void AddTripod(Transform parent, CatalogPhotoSpot spot, Vector3 floor)
        {
            var go = new GameObject(PhotoSpotPrefix + spot.Id);
            SetLayer(go, "Interactable");
            go.transform.SetParent(parent, false);
            float yaw = PhotoSpot.TryYaw(spot.LookDir, out float degrees) ? degrees : 0f;
            go.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));

            CollectableMeshes.Model model = CollectableMeshes.Tripod();
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;

            // InteractionPrompt は Interactable と NPC のレイヤーの当たり判定を球で探す。脚の高さの見えない球。
            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.6f;
            collider.center = new Vector3(0f, 0.9f, 0f);

            PhotoSpot photo = go.AddComponent<PhotoSpot>();
            photo.SpotId = spot.Id;
            photo.LookDir = spot.LookDir;
            photo.PromptLabel = "撮影する";
            photo.InteractionRange = TripodRange;
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

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
        }
    }
}
