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

        /// <summary>建物と地面に当たり判定とレイヤーを付ける。遠景は NavMesh から外す。</summary>
        private static void DressCampus(GameObject campus)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int buildingLayer = LayerMask.NameToLayer("Building");
            int colliders = 0;

            foreach (MeshFilter filter in campus.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                GameObject go = filter.gameObject;
                string id = go.name.ToLowerInvariant();
                bool background = id.StartsWith("bg") || id.Contains("background");

                if (!background)
                {
                    MeshCollider collider = go.GetComponent<MeshCollider>();
                    if (collider == null)
                    {
                        collider = go.AddComponent<MeshCollider>();
                    }

                    collider.sharedMesh = filter.sharedMesh;
                    colliders++;
                }
                else
                {
                    Ignore(go);
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

            EditorPaths.Report("キャンパスに MeshCollider を " + colliders + " 個付けました。");
        }

        /// <summary>樹木は幹だけ当たり判定を持たせ、NavMesh のベイクからは外す。</summary>
        private static void DressTrees(GameObject trees)
        {
            Ignore(trees);
            int count = 0;

            foreach (Transform child in trees.transform)
            {
                if (child.GetComponentInChildren<MeshFilter>(true) == null)
                {
                    continue;
                }

                CapsuleCollider capsule = child.gameObject.GetComponent<CapsuleCollider>();
                if (capsule == null)
                {
                    capsule = child.gameObject.AddComponent<CapsuleCollider>();
                }

                capsule.radius = 0.45f;
                capsule.height = 6f;
                capsule.center = new Vector3(0f, 3f, 0f);
                count++;

                GameObjectUtility.SetStaticEditorFlags(child.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }

            EnableInstancing(trees);
            EditorPaths.Report("樹木 " + count + " 本に幹の当たり判定を付けました。");
        }

        /// <summary>同じマテリアルの木を GPU インスタンシングでまとめて描く。</summary>
        private static void EnableInstancing(GameObject trees)
        {
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in trees.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && seen.Add(material) && !material.enableInstancing)
                    {
                        material.enableInstancing = true;
                        EditorUtility.SetDirty(material);
                    }
                }
            }
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

        /// <summary>キャンパスの外側まで続く大きな地面。境界の外に落ちないようにする。</summary>
        private static void BuildGround(Transform root)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "OuterGround";
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            ground.transform.localScale = new Vector3(70f, 1f, 70f);

            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                ground.layer = groundLayer;
            }

            var renderer = ground.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialLibrary.EnsureCampus("grass_dark");
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
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
