using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャンパスの西へ延びる回廊（route.fbx）を置く (#41)。
    /// 道・沿道の建物・柵を並べ、西の壁に門を開け、増築した区画を壁で囲う。
    ///
    /// NavMesh は焼かない。CampusStage.BakeNavMesh のベイク範囲は x -217..243 なので
    /// この区画（x -490..-340）はそもそも入らないし、NPC も置かない。プレイヤーは
    /// CharacterController で歩くので NavMesh は要らない。
    /// 当たり判定の付け方（葉のサブメッシュを落とす）は CampusStage と同じ約束にそろえてある。
    /// </summary>
    public static class RouteStage
    {
        /// <summary>回廊のモデル。blender/build_route.py が書き出す。</summary>
        public const string RouteFbx = "Assets/Models/Campus/route.fbx";

        /// <summary>回廊の座標の sidecar。無ければ DormRoute の既定値で組む。</summary>
        public const string RouteJson = "Assets/Models/Campus/route.json";

        /// <summary>シーンに置くときの名前。</summary>
        public const string RootName = "Route";

        /// <summary>WorldBoundsStage が立てる壁のまとまりの名前。</summary>
        public const string WallsName = "WorldBoundsWalls";

        /// <summary>増築区画を囲う壁のまとまりの名前。</summary>
        public const string AnnexWallsName = "AnnexWalls";

        /// <summary>当たり判定メッシュのアセット名に使う接頭辞（CampusStage.ColliderAssetPath の stage）。</summary>
        public const string ColliderStage = "route";

        /// <summary>
        /// 回廊を置いて当たり判定とレイヤーを付ける。モデルが無ければ何もせず null。
        /// CampusStage.Build と WorldBoundsStage.Build のあと、NavMesh を焼く前に呼ぶ。
        /// </summary>
        public static GameObject Build(Transform root)
        {
            GameObject route = CampusStage.PlaceModel(RouteFbx, RootName, root);
            if (route == null)
            {
                // PlaceModel が「モデルが見つかりません」を出している。裏エンドが無いだけで本編は動くので止めない。
                return null;
            }

            Dress(route);

            // 西の区画は NavMesh に入れない (#41)。NavMeshModifier は子にも効く。
            CampusStage.Ignore(route);
            return route;
        }

        /// <summary>
        /// 道・沿道の建物・柵に当たり判定とレイヤーを付ける。種別は名前で決める。
        /// 振り分けは CampusStage.Dress と同じ約束にそろえてある（葉のサブメッシュを落とすのも同じ）。
        ///   …water…       水面。当たり判定なし
        ///   bg…           書き割り。当たり判定なし
        ///   …background…  背景ビル。中に入れないよう当たり判定は付ける（bld_route_background）
        ///   …road…        道の舗装。地面の帯の上に載っているだけなので当たり判定は付けない（二重の床を作らない）
        ///   …ground…      地面の帯。Ground レイヤー（route_ground）
        ///   bld…          沿道の建物・柵。Building レイヤー（bld_route_fence）
        ///   それ以外      街灯など。当たり判定だけ付けて Default のまま（route_props_lamp）
        ///
        /// 今の route.fbx に入っているのは bld_route_background / bld_dorm / bld_dorm_trim /
        /// route_ground / bld_route_fence / route_props_lamp の 6 つだけで、舗装は
        /// build_route.py が地面の帯と同じ route_ground に溶かし込んでいる。つまり
        /// 「…road…」の枝は現物では通らない（舗装は帯ごと Ground の当たり判定になる。
        /// 帯と舗装の高さの差は Z_ROAD = 0.012 m なので段差にはならない）。
        /// 舗装を別オブジェクトに分けたくなったときのために枝は残してある。
        /// </summary>
        private static void Dress(GameObject route)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int buildingLayer = LayerMask.NameToLayer("Building");
            int colliders = 0;
            int meshes = 0;

            foreach (MeshFilter filter in route.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                meshes++;
                GameObject go = filter.gameObject;
                string id = go.name.ToLowerInvariant();
                bool backdrop = id.StartsWith("bg");
                bool backgroundBuilding = !backdrop && id.Contains("background");
                bool road = id.Contains("road") || id.Contains("path");
                bool ground = id.Contains("ground") && !backgroundBuilding;

                if (CampusStage.IsWaterMesh(id) || backdrop)
                {
                    MeshCollider stale = go.GetComponent<MeshCollider>();
                    if (stale != null)
                    {
                        Object.DestroyImmediate(stale);
                    }
                }
                else if (road && !ground)
                {
                    // 舗装は地面の帯の数 cm 上に敷いた板。床は帯が持つので、当たり判定は付けない。
                    MeshCollider stale = go.GetComponent<MeshCollider>();
                    if (stale != null)
                    {
                        Object.DestroyImmediate(stale);
                    }
                }
                else if (backgroundBuilding)
                {
                    // 背景ビルはガラスなどの薄い面を当たり判定から落とす（CampusStage と同じ選り分け）。
                    if (CampusStage.AttachMeshCollider(go, filter.sharedMesh,
                            CampusStage.ColliderAssetPath(ColliderStage, go.name),
                            CampusStage.IsBackgroundNonSolidMaterial))
                    {
                        colliders++;
                    }
                }
                else if (CampusStage.AttachMeshCollider(go, filter.sharedMesh,
                             CampusStage.ColliderAssetPath(ColliderStage, go.name)))
                {
                    colliders++;
                }

                if ((ground || road) && groundLayer >= 0)
                {
                    go.layer = groundLayer;
                }
                else if (id.StartsWith("bld") && buildingLayer >= 0)
                {
                    go.layer = buildingLayer;
                }

                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            EditorPaths.Report("西の回廊に MeshCollider を " + colliders + " 個付けました（メッシュ " + meshes + " 個）。");
        }

        /// <summary>
        /// sidecar（route.json）を読む。無い・壊れている・値がありえないなら DormRoute の既定値。
        /// 毎回読み直すので、blender で出し直したあとにシーンを組み直せばそのまま反映される。
        /// </summary>
        public static DormRoute.Info ReadInfo()
        {
            string path = EditorPaths.ProjectRelative(RouteJson);
            if (!File.Exists(path))
            {
                return DormRoute.Default;
            }

            try
            {
                DormRoute.Info info = DormRoute.FromJson(File.ReadAllText(path));
                EditorPaths.Report("回廊の sidecar を読みました: " + RouteJson
                    + " 玄関 (" + info.Door.x.ToString("F2") + ", " + info.Door.y.ToString("F2") + ")"
                    + " 門 z=" + info.GateZ.ToString("F2"));
                return info;
            }
            catch (IOException error)
            {
                EditorPaths.Report("回廊の sidecar を読めません: " + error.Message + "（既定値で組みます）");
                return DormRoute.Default;
            }
        }

        /// <summary>
        /// 西の壁に門を開ける。Wall_West を消して南北 2 枚に立て直す。
        /// 門が壁の外へはみ出す sidecar のときは壁に手を触れない（通れないだけで、穴は開かない）。
        /// WorldBoundsStage.Build のあとに呼ぶ。
        /// </summary>
        public static bool OpenWestGate(Transform root)
        {
            Transform walls = root != null ? root.Find(WallsName) : null;
            if (walls == null)
            {
                EditorPaths.Report("範囲の壁（" + WallsName + "）が見つからないので門を開けられません。");
                return false;
            }

            WarnIfWallMetricsDiffer();

            DormRoute.Info info = ReadInfo();
            Rect campus = WorldBounds.DefaultArea;
            if (!DormRoute.SplitWestWall(campus, info.GateZ, info.GateHalfWidth,
                    WorldBoundsStage.WallThickness, WorldBoundsStage.WallBottom, WorldBoundsStage.WallTop,
                    out DormRoute.WallSlab south, out DormRoute.WallSlab north))
            {
                EditorPaths.Report("門 z=" + info.GateZ.ToString("F2") + " が西の壁に収まらないので、壁はそのままにします。");
                return false;
            }

            Transform west = walls.Find("Wall_West");
            if (west == null)
            {
                EditorPaths.Report("Wall_West が見つかりません（すでに門を開けたあと？）。");
                return false;
            }

            Object.DestroyImmediate(west.gameObject);
            AddWall(walls, south);
            AddWall(walls, north);

            EditorPaths.Report("西の壁に門を開けました: z "
                + (info.GateZ - info.GateHalfWidth).ToString("F2") + ".."
                + (info.GateZ + info.GateHalfWidth).ToString("F2"));
            return true;
        }

        /// <summary>
        /// 増築区画を壁で囲う。東はキャンパスの西の壁が兼ねるので、西・北・南の 3 枚。
        /// これが無いと門をくぐった先が open world になり、2000 m 四方の外周の地面をどこまでも歩ける。
        /// </summary>
        public static GameObject BuildAnnexWalls(Transform root)
        {
            DormRoute.Info info = ReadInfo();

            var group = new GameObject(AnnexWallsName);
            group.transform.SetParent(root, false);

            DormRoute.WallSlab[] slabs = DormRoute.AnnexWalls(info.Annex,
                WorldBoundsStage.WallThickness, WorldBoundsStage.WallBottom, WorldBoundsStage.WallTop);
            for (int i = 0; i < slabs.Length; i++)
            {
                AddWall(group.transform, slabs[i]);
            }

            CampusStage.Ignore(group);
            EditorPaths.Report("増築区画の壁を立てました: x " + info.Annex.xMin.ToString("F0") + ".."
                + info.Annex.xMax.ToString("F0") + " z " + info.Annex.yMin.ToString("F0") + ".."
                + info.Annex.yMax.ToString("F0"));
            return group;
        }

        /// <summary>
        /// WorldBounds の見張りの矩形を、キャンパス + 増築区画に広げる。
        /// 広げないと門をくぐった瞬間に「範囲外」とみなされて引き戻される。
        /// _area は [SerializeField] の private なので SerializedObject 経由で書く。
        /// SceneBuilder.PlaceSystems で WorldBounds を足したあとに呼ぶ。
        /// </summary>
        public static bool WidenWorldBounds()
        {
            var bounds = Object.FindAnyObjectByType<WorldBounds>();
            if (bounds == null)
            {
                EditorPaths.Report("WorldBounds が見つからないので見張りの範囲を広げられません。");
                return false;
            }

            Rect area = DormRoute.WatchArea(WorldBounds.DefaultArea, ReadInfo().Annex);
            var so = new SerializedObject(bounds);
            SerializedProperty property = so.FindProperty("_area");
            if (property == null)
            {
                EditorPaths.Report("WorldBounds._area が見つかりません（名前が変わった？）。");
                return false;
            }

            property.rectValue = area;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorPaths.Report("見張りの範囲を広げました: x " + area.xMin.ToString("F0") + ".."
                + area.xMax.ToString("F0") + " z " + area.yMin.ToString("F0") + ".." + area.yMax.ToString("F0"));
            return true;
        }

        /// <summary>
        /// DormRoute が写している壁の寸法が WorldBoundsStage とずれていないか見る。
        /// Runtime から Editor アセンブリは見えないので写すしかなく、片方だけ直すと門の位置がずれる。
        /// </summary>
        private static void WarnIfWallMetricsDiffer()
        {
            if (Mathf.Approximately(DormRoute.DefaultWallThickness, WorldBoundsStage.WallThickness)
                && Mathf.Approximately(DormRoute.DefaultWallBottom, WorldBoundsStage.WallBottom)
                && Mathf.Approximately(DormRoute.DefaultWallTop, WorldBoundsStage.WallTop))
            {
                return;
            }

            EditorPaths.Report("壁の寸法が WorldBoundsStage と DormRoute でずれています。"
                + " DormRoute.DefaultWall* を直してください。");
        }

        /// <summary>WorldBoundsStage.AddWall と同じ作り（private なので写し）。レンダラを持たない厚い箱。</summary>
        private static void AddWall(Transform parent, DormRoute.WallSlab slab)
        {
            var wall = new GameObject(slab.Name);
            wall.transform.SetParent(parent, false);
            wall.transform.position = slab.Center;
            wall.transform.rotation = Quaternion.identity;

            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = slab.Size;
        }
    }
}
