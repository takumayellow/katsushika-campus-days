using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// Campus シーンの地形側。FBX の配置・当たり判定・光・NavMesh までを受け持つ。
    /// </summary>
    public static class CampusStage
    {
        public const string NavMeshAsset = "Assets/Generated/CampusNavMesh.asset";

        /// <summary>NavMesh を焼く範囲。campus.json の境界（x -173..199 / z -167..101）を包む。</summary>
        private static readonly Vector3 NavCenter = new Vector3(13f, 10f, -33f);
        private static readonly Vector3 NavSize = new Vector3(460f, 140f, 340f);

        private static GameObject _campus;

        /// <summary>配置済みのキャンパス本体。SceneBuilder が entrance を引くのに使う。</summary>
        public static GameObject Instance => _campus;

        /// <summary>地面・建物・樹木・光をシーンへ置く。</summary>
        public static void Build(Transform root)
        {
            BuildGround(root);
            _campus = PlaceModel(EditorPaths.CampusFbx, "Campus", root);
            if (_campus != null)
            {
                DressCampus(_campus);
                BuildBasinKeepout(root);
            }

            GameObject trees = PlaceModel(EditorPaths.TreesFbx, "Trees", root);
            if (trees != null)
            {
                DressTrees(trees);
            }

            BuildLighting(root);
        }

        /// <summary>FBX を 1 つシーンへ展開する。プレハブ参照は解いて素の GameObject にする。</summary>
        public static GameObject PlaceModel(string assetPath, string name, Transform root)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                EditorPaths.Report("モデルが見つかりません: " + assetPath);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            instance.transform.SetParent(root, false);
            instance.transform.localPosition = Vector3.zero;
            // Blender の FBX は取り込みで Y 軸まわりに 180 度回る（Blender (x, y) がワールド (-x, -z) になる）。
            // campus.json・minimap.json・CampusProps の座標は Blender の (x, y) をそのままワールド (x, z) と
            // 読む前提なので、root ごと戻して同じ座標系にそろえる。
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            return instance;
        }

        /// <summary>
        /// 建物と地面に当たり判定とレイヤーを付ける。遠景と背景ビルは NavMesh から外す。
        ///
        /// メッシュの種別は名前で決める（build_campus.py が作るのは site_ground / site_water /
        /// bld_&lt;id&gt; / bld_background / bld_entrances / site_furniture / site_props_* の 18 個）。
        ///   bg…            遠景の書き割り。当たり判定なし。今の FBX には無いが、足したときに備えて残す
        ///   …background…   背景ビル 230 棟をまとめた bld_background。当たり判定あり、NavMesh なし (#50)
        ///   …water…        水盤の水面 site_water。当たり判定なし、NavMesh なし (#46)
        /// </summary>
        private static void DressCampus(GameObject campus)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int buildingLayer = LayerMask.NameToLayer("Building");
            int colliders = 0;
            int backgroundTris = 0;

            foreach (MeshFilter filter in campus.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                GameObject go = filter.gameObject;
                string id = go.name.ToLowerInvariant();
                bool backdrop = id.StartsWith("bg");
                bool backgroundBuilding = !backdrop && id.Contains("background");

                if (IsWaterMesh(id))
                {
                    // 水面を床にしない。付いていたら消す（#46）。入れないようにするのは BuildBasinKeepout。
                    MeshCollider water = go.GetComponent<MeshCollider>();
                    if (water != null)
                    {
                        Object.DestroyImmediate(water);
                    }

                    Ignore(go);
                }
                else if (backdrop)
                {
                    Ignore(go);
                }
                else if (backgroundBuilding)
                {
                    // 背景ビルは中に入れないよう当たり判定を付けるが、NavMesh には入れない（#50）。
                    // 屋根の上を NPC が歩かないようにするため、これまでどおり Ignore する。
                    if (AttachMeshCollider(go, filter.sharedMesh, ColliderAssetPath("campus", go.name),
                            IsBackgroundNonSolidMaterial))
                    {
                        colliders++;
                        Mesh solid = go.GetComponent<MeshCollider>().sharedMesh;
                        backgroundTris += solid != null ? solid.triangles.Length / 3 : 0;
                    }

                    Ignore(go);
                }
                else if (AttachMeshCollider(go, filter.sharedMesh, ColliderAssetPath("campus", go.name)))
                {
                    colliders++;
                }

                if (id.Contains("ground") && groundLayer >= 0)
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

            EditorPaths.Report("キャンパスに MeshCollider を " + colliders + " 個付けました"
                + "（うち背景ビル " + backgroundTris + " 三角形、NavMesh からは除外）。");
        }

        /// <summary>
        /// 水面のメッシュか。build_campus.py が site_water という名前で出す（kcd_lib/site.py の build_water）。
        /// 水面には当たり判定を付けないし、NavMesh にも焼かない（#46）。
        /// </summary>
        public static bool IsWaterMesh(string objectName)
        {
            return !string.IsNullOrEmpty(objectName) && objectName.ToLowerInvariant().Contains("water");
        }

        /// <summary>
        /// 背景ビルの当たり判定から外すマテリアル。窓ガラス（glass_dark）は壁から 4 cm 外へ浮かせた
        /// ただの板で、当たり判定に入れても壁と二重になるだけ。三角形数の 9 割がこれなので落とす（#50）。
        /// </summary>
        public static bool IsBackgroundNonSolidMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            return material.name.ToLowerInvariant().StartsWith("glass") || IsFoliageMaterial(material);
        }

        /// <summary>
        /// 足を乗せてはいけない当たり判定か。水面と水盤の見えない壁。CampusProps.Ground が
        /// これを地面と間違えると、ベンチや小物が水の上・壁の天端に載ってしまう（#46）。
        /// </summary>
        public static bool IsNonGroundCollider(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            return objectName.StartsWith(KeepoutPrefix) || IsWaterMesh(objectName);
        }

        /// <summary>葉を落とした当たり判定メッシュの置き場。</summary>
        public const string ColliderFolder = "Assets/Models/Colliders";

        public static string ColliderAssetPath(string stage, string objectName)
        {
            return ColliderFolder + "/" + stage + "_" + objectName + ".asset";
        }

        /// <summary>
        /// 当たり判定から外すマテリアル。観葉植物やプランターの葉のかたまりは非凸メッシュなので、
        /// カプセルが入り込むと PhysX が押し出せず動けなくなる（#30）。鉢と幹は残す。
        /// </summary>
        public static bool IsFoliageMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            string name = material.name.ToLowerInvariant();
            return name.StartsWith("plant_green") || name.StartsWith("leaf");
        }

        /// <summary>
        /// MeshCollider を付ける。葉のサブメッシュがあればそれを落としたメッシュをアセットに保存して使う。
        /// 全部が葉なら当たり判定を付けない。付けたら true。
        /// </summary>
        public static bool AttachMeshCollider(GameObject go, Mesh source, string assetPath)
        {
            return AttachMeshCollider(go, source, assetPath, IsFoliageMaterial);
        }

        /// <summary>当たり判定から落とすマテリアルを指定できる版。</summary>
        public static bool AttachMeshCollider(GameObject go, Mesh source, string assetPath,
            System.Func<Material, bool> drop)
        {
            Mesh mesh = ColliderMesh(source, go.GetComponent<Renderer>(), assetPath, drop);
            MeshCollider collider = go.GetComponent<MeshCollider>();
            if (mesh == null)
            {
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                return false;
            }

            if (collider == null)
            {
                collider = go.AddComponent<MeshCollider>();
            }

            collider.sharedMesh = mesh;
            return true;
        }

        /// <summary>葉のサブメッシュを落とした当たり判定用メッシュ。落とすものが無ければ元のまま、全部葉なら null。</summary>
        public static Mesh ColliderMesh(Mesh source, Renderer renderer, string assetPath)
        {
            return ColliderMesh(source, renderer, assetPath, IsFoliageMaterial);
        }

        /// <summary>当たり判定から落とすマテリアルを指定できる版。使う頂点だけ詰めて保存する。</summary>
        public static Mesh ColliderMesh(Mesh source, Renderer renderer, string assetPath,
            System.Func<Material, bool> drop)
        {
            Material[] materials = renderer != null ? renderer.sharedMaterials : null;
            if (source == null || materials == null || drop == null)
            {
                return source;
            }

            var keep = new List<int>();
            for (int i = 0; i < source.subMeshCount; i++)
            {
                if (i >= materials.Length || !drop(materials[i]))
                {
                    keep.Add(i);
                }
            }

            if (keep.Count == source.subMeshCount)
            {
                return source;
            }

            if (keep.Count == 0)
            {
                return null;
            }

            var triangles = new List<int>();
            foreach (int index in keep)
            {
                triangles.AddRange(source.GetTriangles(index));
            }

            // 残した三角形が使う頂点だけ詰める。背景ビルはガラスを落とすと 62,430 頂点のうち
            // 2,782 頂点しか使わないので、元の頂点配列をそのまま持たせるとアセットが無駄に太る（#50）。
            Vector3[] sourceVertices = source.vertices;
            var remap = new int[sourceVertices.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var vertices = new List<Vector3>();
            for (int i = 0; i < triangles.Count; i++)
            {
                int old = triangles[i];
                if (remap[old] < 0)
                {
                    remap[old] = vertices.Count;
                    vertices.Add(sourceVertices[old]);
                }

                triangles[i] = remap[old];
            }

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            Mesh mesh = existing != null ? existing : new Mesh();
            mesh.Clear();
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            mesh.indexFormat = vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            if (existing == null)
            {
                if (!AssetDatabase.IsValidFolder(ColliderFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Models", "Colliders");
                }

                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        /// <summary>trees.fbx の原型メッシュの名前（原点にモデリングされた 1 本, build_campus.py）。</summary>
        public const string TreeMeshPrefix = "tree_mesh_";

        /// <summary>trees.fbx の種ごとのまとめ。子に位置だけの Empty（tree_&lt;n&gt;）が並ぶ。</summary>
        public const string TreeGroupPrefix = "trees_";

        /// <summary>Empty の下に作る、メッシュを描く子の名前。</summary>
        public const string TreeBodyName = "body";

        /// <summary>Empty の下に作る、幹の当たり判定の子の名前。</summary>
        public const string TreeTrunkName = "trunk";

        /// <summary>幹の当たり判定の半径と高さ（木の scale を掛ける前）。</summary>
        public const float TreeTrunkRadius = 0.3f;
        public const float TreeTrunkHeight = 6f;

        /// <summary>
        /// 幹の外側に空ける NavMesh の余白 [m]。NPC の NavMeshAgent の半径（0.32 m）と当たり判定の半径（0.34 m）の
        /// 大きい方を覆う。NavMeshModifierVolume の範囲はエージェントの半径ぶん削られないので、余白を箱の大きさに含める。
        /// </summary>
        public const float TreeTrunkNavClearance = 0.35f;

        /// <summary>
        /// trees.fbx の tree_&lt;n&gt; は位置・向き・大きさだけの Empty で、メッシュを持たない。
        /// 原点にある tree_mesh_&lt;種&gt; のメッシュを各 Empty に載せて植え、原型は隠す（#57）。
        /// 幹だけ当たり判定を持たせる。半径は幹の見た目に合わせて細く（葉に引っかからない, #30）。
        /// 木のメッシュは NavMesh のベイクから外し（樹冠の下を歩けなくしない）、幹の足もとだけを
        /// NavMeshModifierVolume（Not Walkable）で抜いて、NPC が幹をすり抜けないようにする。
        /// 描くときは TreeChunkCombiner が再生の始めにマスごとにまとめる（1 本ずつだと描画コールが多すぎる, #57）。
        /// </summary>
        private static void DressTrees(GameObject trees)
        {
            Ignore(trees);

            var prototypes = new Dictionary<string, Transform>();
            var groups = new List<Transform>();
            foreach (Transform child in trees.transform)
            {
                if (child.name.StartsWith(TreeMeshPrefix, System.StringComparison.Ordinal))
                {
                    prototypes[child.name.Substring(TreeMeshPrefix.Length)] = child;
                }
                else if (child.name.StartsWith(TreeGroupPrefix, System.StringComparison.Ordinal))
                {
                    groups.Add(child);
                }
            }

            int count = 0;
            foreach (Transform group in groups)
            {
                string species = group.name.Substring(TreeGroupPrefix.Length);
                if (!prototypes.TryGetValue(species, out Transform prototype)
                    || !prototype.TryGetComponent(out MeshFilter filter)
                    || !prototype.TryGetComponent(out MeshRenderer renderer))
                {
                    EditorPaths.Report("樹木の原型が見つかりません: " + TreeMeshPrefix + species);
                    continue;
                }

                // 原型も trees_<種> も Blender の原点に単位の変換で置かれている。
                // FBX の軸の変換がどう入っていても、Empty から見た本体の変換は「trees_<種> から見た原型」と同じになる。
                Matrix4x4 relative = group.worldToLocalMatrix * prototype.localToWorldMatrix;
                foreach (Transform spot in group)
                {
                    PlantTree(spot, relative, filter.sharedMesh, renderer.sharedMaterials);
                    count++;
                }
            }

            foreach (Transform prototype in prototypes.Values)
            {
                prototype.gameObject.SetActive(false);
            }

            trees.AddComponent<TreeChunkCombiner>();
            EditorPaths.Report("樹木を " + count + " 本植えました（原型 " + prototypes.Count + " 種）。");
        }

        /// <summary>Empty 1 つに本体のメッシュと幹の当たり判定を付ける。</summary>
        private static void PlantTree(Transform spot, Matrix4x4 relative, Mesh mesh, Material[] materials)
        {
            var body = new GameObject(TreeBodyName);
            body.transform.SetParent(spot, false);
            body.transform.localPosition = relative.GetPosition();
            body.transform.localRotation = relative.rotation;
            body.transform.localScale = relative.lossyScale;
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = materials;

            // 静的バッチにすると、ビルドした版では MeshFilter のメッシュが結合済みのメッシュに差し替わり、
            // TreeChunkCombiner が原型を読めなくなる。まとめるのは TreeChunkCombiner に任せる。
            GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.OccludeeStatic);

            // Empty の軸は Blender の Z-up のまま来ることがあるので、幹はワールドの上向きにそろえる。
            // 大きさは Empty の拡大（木の scale）をそのまま受け、太い木ほど幹も太くなる。
            var trunk = new GameObject(TreeTrunkName);
            trunk.transform.SetParent(spot, false);
            trunk.transform.rotation = Quaternion.identity;
            CapsuleCollider capsule = trunk.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = TreeTrunkRadius;
            capsule.height = TreeTrunkHeight;
            capsule.center = new Vector3(0f, TreeTrunkHeight * 0.5f, 0f);
            GameObjectUtility.SetStaticEditorFlags(trunk, StaticEditorFlags.BatchingStatic);

            // 余白は木の scale によらずワールドで一定にしたいので、ローカルでは scale で割る。
            float half = TreeTrunkRadius + TreeTrunkNavClearance / Mathf.Max(trunk.transform.lossyScale.x, 0.01f);
            NavMeshModifierVolume volume = trunk.AddComponent<NavMeshModifierVolume>();
            volume.center = new Vector3(0f, 1f, 0f);
            volume.size = new Vector3(half * 2f, 2f, half * 2f);
            volume.area = 1;   // 1 = Not Walkable
        }

        public static void Ignore(GameObject go)
        {
            NavMeshModifier modifier = go.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = go.AddComponent<NavMeshModifier>();
            }

            modifier.ignoreFromBuild = true;
            modifier.AffectsAgentType(0);
        }

        /// <summary>OuterGround の半径（m）。カメラの far clip（900 m）＋ 歩ける範囲（±340 m）より広く取る。</summary>
        public const float OuterGroundHalfExtent = 2000f;

        /// <summary>OuterGround の高さ（m）。site_ground の芝（Blender の Z_GROUND = 0.0）のすぐ下。</summary>
        public const float OuterGroundY = -0.05f;

        /// <summary>OuterGround の当たり判定の厚み（m）。</summary>
        public const float OuterGroundThickness = 0.5f;

        /// <summary>
        /// キャンパスの外側まで続く大きな地面。境界の外に落ちないようにし、遠くに地面の端（その先の
        /// 茶色い skybox の地面色）を見せない。以前は ±350 m で終わっていて、site_ground と同じ広さしか
        /// 無かったので、遠景に茶色い縁が出ていた（#40, #30）。
        /// 当たり判定は Plane の MeshCollider ではなく薄い BoxCollider にする。4 km の Plane を
        /// そのまま当たり判定にすると 1 枚 400 m の三角形になり、PhysX が「2 頂点の距離が 500 を超える」と
        /// 警告して接地も不安定になる。
        /// </summary>
        private static void BuildGround(Transform root)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "OuterGround";
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, OuterGroundY, 0f);
            // Plane の素の大きさは 10 m 四方。
            float scale = OuterGroundHalfExtent * 2f / 10f;
            ground.transform.localScale = new Vector3(scale, 1f, scale);

            Object.DestroyImmediate(ground.GetComponent<MeshCollider>());
            BoxCollider box = ground.AddComponent<BoxCollider>();
            // BoxCollider の size はローカル。x/z は localScale 倍されるので Plane のメッシュ（±5）に
            // 合わせて 10、y は localScale が 1 なので OuterGroundThickness m そのまま。天面は OuterGroundY。
            box.size = new Vector3(10f, OuterGroundThickness, 10f);
            box.center = new Vector3(0f, -OuterGroundThickness * 0.5f, 0f);

            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                ground.layer = groundLayer;
            }

            var renderer = ground.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialLibrary.EnsureCampus("grass_dark");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
        }

        // --- 図書館を囲む堀（水盤）の当たり判定 (#46, #56) ---
        // 数値は blender/kcd_lib/site.py と合わせること（BASINS / BASIN_RIM / Z_BASIN_RIM / Z_WATER）。
        // ずれていないかは EditMode テスト BasinKeepoutTests が site.py を読んで確かめる。

        /// <summary>
        /// 水面の矩形 (x = u0, y = v0, z = u1, w = v1)。site.py の BASINS。図書館の東と南を L 字に囲む。
        /// 1 枚目はモールの北、2・3 枚目はモールの南（東の帯と南の帯）。
        /// </summary>
        public static readonly Vector4[] Basins =
        {
            new Vector4(-59.5f, -16.4f, -50f, 24f),
            new Vector4(-59.5f, -78f, -50f, -31.6f),
            new Vector4(-100f, -78f, -59.5f, -66f),
        };

        /// <summary>縁石の幅（m）。site.py の BASIN_RIM。水際の外へこの幅で縁石を回す。</summary>
        public const float BasinRimWidth = 1.6f;

        /// <summary>縁石の天端の高さ（m）。site.py の Z_BASIN_RIM。腰かけられる段差なので残す。</summary>
        public const float BasinRimTopY = 0.36f;

        /// <summary>水面の高さ（m）。site.py の Z_WATER。</summary>
        public const float BasinWaterY = 0.06f;

        /// <summary>
        /// 見えない壁の天端（m）。PlayerMotor は stepOffset 0.40 + ジャンプ 1.10 = 1.50 m までしか
        /// 登れない。縁石の天端（0.36）から跳んでも 1.86 m なので、それより高くする。
        /// </summary>
        public const float BasinWallTopY = 2.6f;

        /// <summary>見えない壁の下端（m）。地面より下まで伸ばして隙間を作らない。</summary>
        public const float BasinWallBottomY = -0.5f;

        /// <summary>見えない壁の厚み（m）。半分が縁石側へ食い込む。</summary>
        public const float BasinWallThickness = 0.5f;

        /// <summary>NavMesh から外す余白（m）。縁石の幅 1.6 m より広く取り、縁石の上も歩かせない。</summary>
        public const float BasinNavMargin = 1.7f;

        /// <summary>NavMesh から外す箱の高さ（m）。下端は BasinWallBottomY。</summary>
        public const float BasinNavHeight = 2f;

        /// <summary>地面として拾ってはいけない当たり判定の名前の接頭辞（CampusProps.Ground が見る）。</summary>
        public const string KeepoutPrefix = "Keepout_";

        /// <summary>水際の 1 辺。Axis が 'u' なら u = C の辺（T は v）、'v' なら v = C の辺（T は u）。</summary>
        public struct BasinEdge
        {
            public char Axis;
            public float C;
            public float T0;
            public float T1;

            /// <summary>水の外へ向かう側（+1 / -1）。</summary>
            public int Out;
        }

        /// <summary>
        /// 水際の辺のうち、隣の水面と接していない部分（＝縁石と見えない壁を立てる所）。
        /// site.py の basin_edges と同じ計算（端を延ばすかどうかは縁石の形の話なので持たない）。
        /// </summary>
        public static List<BasinEdge> BasinEdges(IList<Vector4> rects)
        {
            var edges = new List<BasinEdge>();
            for (int i = 0; i < rects.Count; i++)
            {
                Vector4 r = rects[i];
                var sides = new[]
                {
                    new BasinEdge { Axis = 'v', C = r.y, T0 = r.x, T1 = r.z, Out = -1 },
                    new BasinEdge { Axis = 'v', C = r.w, T0 = r.x, T1 = r.z, Out = +1 },
                    new BasinEdge { Axis = 'u', C = r.x, T0 = r.y, T1 = r.w, Out = -1 },
                    new BasinEdge { Axis = 'u', C = r.z, T0 = r.y, T1 = r.w, Out = +1 },
                };
                foreach (BasinEdge side in sides)
                {
                    foreach (Vector2 span in UncoveredSpans(rects, i, side))
                    {
                        edges.Add(new BasinEdge { Axis = side.Axis, C = side.C, T0 = span.x, T1 = span.y, Out = side.Out });
                    }
                }
            }

            return edges;
        }

        /// <summary>辺 side のうち、外側に接する別の水面（rects[j], j != self）に覆われていない区間。</summary>
        private static List<Vector2> UncoveredSpans(IList<Vector4> rects, int self, BasinEdge side)
        {
            var spans = new List<Vector2> { new Vector2(side.T0, side.T1) };
            for (int j = 0; j < rects.Count; j++)
            {
                if (j == self)
                {
                    continue;
                }

                Vector4 o = rects[j];
                bool alongU = side.Axis == 'v';
                float face = alongU ? (side.Out > 0 ? o.y : o.w) : (side.Out > 0 ? o.x : o.z);
                float a = alongU ? o.x : o.y;
                float b = alongU ? o.z : o.w;
                if (Mathf.Abs(face - side.C) > 1e-4f)
                {
                    continue;
                }

                var next = new List<Vector2>();
                foreach (Vector2 sp in spans)
                {
                    if (b <= sp.x || a >= sp.y)
                    {
                        next.Add(sp);
                        continue;
                    }

                    if (a > sp.x)
                    {
                        next.Add(new Vector2(sp.x, a));
                    }

                    if (b < sp.y)
                    {
                        next.Add(new Vector2(b, sp.y));
                    }
                }

                spans = next;
            }

            spans.RemoveAll(sp => sp.y - sp.x <= 1e-4f);
            return spans;
        }

        /// <summary>
        /// 堀に入れないようにする（#46, #56）。水面そのものは残す（クエスト q_library / q_sunset と
        /// 撮影スポット ps_pond_library が使う）。
        ///
        /// 柵で囲うと不自然なので、当たり判定だけの見えない壁（BoxCollider）を水際に立てる。
        /// 隣の水面と接する辺（L 字の継ぎ目）には立てない。壁は Default レイヤーなので、カメラの遮蔽判定
        /// （Ground|Building しか見ない ThirdPersonCamera / CinemachineDeoccluder）には
        /// 引っかからず、見た目も操作感も今までどおり。
        ///
        /// 縁石（天端 0.36 m・幅 1.6 m）は腰かけたまま。壁の中心線を水際に置き、厚み 0.5 m の
        /// 半分 0.25 m だけ縁石側へ食い込ませるので、縁石は 1.35 m の幅が残る。壁は両端を
        /// 厚みの半分ずつ延ばし、角で隣の壁と重ねて隙間を塞ぐ。
        ///
        /// NPC は水面ごとの NavMeshModifierVolume（area = 1 = Not Walkable）で締め出す。NavMesh は
        /// RenderMeshes から焼くので、MeshRenderer を持たないこの見えない壁自体は NavMesh に
        /// 何も足さない（＝壁の上に歩ける面ができたりはしない）。
        /// </summary>
        private static void BuildBasinKeepout(Transform root)
        {
            var group = new GameObject("BasinKeepout");
            group.transform.SetParent(root, false);
            // 親をキャンパスの u/v 軸に向けておくと、子はローカル (u, y, v) をそのまま入れられる。
            group.transform.localRotation = CampusProps.LocalRotation;

            float y = (BasinWallTopY + BasinWallBottomY) * 0.5f;
            float h = BasinWallTopY - BasinWallBottomY;
            float t = BasinWallThickness;

            List<BasinEdge> edges = BasinEdges(Basins);
            for (int i = 0; i < edges.Count; i++)
            {
                BasinEdge e = edges[i];
                float mid = (e.T0 + e.T1) * 0.5f;
                float len = e.T1 - e.T0 + t;
                bool alongU = e.Axis == 'v';   // v = C の辺は u 方向に延びる
                Vector3 local = alongU ? new Vector3(mid, y, e.C) : new Vector3(e.C, y, mid);
                Vector3 size = alongU ? new Vector3(len, h, t) : new Vector3(t, h, len);
                AddKeepoutWall(group.transform, i.ToString("00"), local, size);
            }

            for (int i = 0; i < Basins.Length; i++)
            {
                Vector4 r = Basins[i];
                var nav = new GameObject(KeepoutPrefix + "BasinNav" + i);
                nav.transform.SetParent(group.transform, false);
                nav.transform.localPosition = new Vector3((r.x + r.z) * 0.5f,
                    BasinWallBottomY + BasinNavHeight * 0.5f, (r.y + r.w) * 0.5f);
                NavMeshModifierVolume volume = nav.AddComponent<NavMeshModifierVolume>();
                volume.center = Vector3.zero;
                volume.size = new Vector3(r.z - r.x + BasinNavMargin * 2f, BasinNavHeight,
                    r.w - r.y + BasinNavMargin * 2f);
                volume.area = 1;   // 1 = Not Walkable
            }

            EditorPaths.Report("堀に見えない壁 " + edges.Count + " 枚と NavMesh の除外領域 "
                + Basins.Length + " か所を置きました。");
        }

        private static void AddKeepoutWall(Transform parent, string name, Vector3 local, Vector3 size)
        {
            var go = new GameObject(KeepoutPrefix + "BasinWall" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>太陽と時間の流れ。色や強さは DayNightCycle が自前の既定値で埋める。</summary>
        private static void BuildLighting(Transform root)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(root, false);
            sun.transform.rotation = Quaternion.Euler(52f, 150f, 0f);

            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.72f;

            sun.AddComponent<DayNightCycle>();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 220f;
            RenderSettings.fogEndDistance = 620f;
            RenderSettings.fogColor = new Color(0.72f, 0.79f, 0.86f);
        }

        /// <summary>NavMesh を焼いてアセットとして保存する。焼けた面積を返す。</summary>
        public static bool BakeNavMesh(Transform root)
        {
            var go = new GameObject("NavMesh");
            go.transform.SetParent(root, false);

            NavMeshSurface surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = NavCenter;
            surface.size = NavSize;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = LayerMaskFor("Default", "Ground", "Building");
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.25f;
            surface.overrideTileSize = true;
            surface.tileSize = 512;
            surface.minRegionArea = 4f;

            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                EditorPaths.Report("NavMesh のベイクに失敗しました。");
                return false;
            }

            EditorPaths.EnsureFolder(EditorPaths.GeneratedFolder);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAsset);
            AssetDatabase.SaveAssets();
            EditorPaths.Report("NavMesh を焼きました: " + NavMeshAsset);
            return true;
        }

        private static int LayerMaskFor(params string[] names)
        {
            int mask = 0;
            foreach (string name in names)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    mask |= 1 << layer;
                }
            }

            return mask;
        }

        /// <summary>FBX に入っている entrance_&lt;id&gt; の Transform を返す。無ければ null。</summary>
        public static Transform FindChild(string name)
        {
            if (_campus == null)
            {
                return null;
            }

            foreach (Transform child in _campus.GetComponentsInChildren<Transform>(true))
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
