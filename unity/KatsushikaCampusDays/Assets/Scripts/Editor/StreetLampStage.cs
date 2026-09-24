using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Editor
{
    /// <summary>
    /// モールの照明柱に夜の明かりを付ける (#8)。
    ///
    /// campus.fbx の照明柱（site_furniture の中、site.py の build_street_furniture）は形だけで、夜も灯らなかった。
    /// 実ライトは足さない（StreetLights の説明）。灯具の位置を site_furniture の頂点から拾い
    /// （<see cref="StreetLampLayout"/>）、灯具ごとに
    ///   ・地面の光だまり（半径 <see cref="PoolRadius"/> m の円。頂点ごとに地面へ落として段差に沿わせる）
    ///   ・灯具の下面の発光面
    ///   ・カメラへ向く暈の板
    /// を作り、1 つのメッシュ（Assets/Generated/Meshes/StreetLampGlow.asset）にまとめて KCD/LampGlow で描く。
    /// </summary>
    public static class StreetLampStage
    {
        public const string ObjectName = "StreetLamps";
        public const string FurnitureName = "site_furniture";
        public const string MaterialPath = "Assets/Materials/Sky/KCD_LampGlow.mat";
        public const string ShaderName = "KCD/LampGlow";
        public const string MeshPath = EditorPaths.GeneratedFolder + "/Meshes/StreetLampGlow.asset";

        /// <summary>光だまりの半径（m）。灯具の高さ 4 m に対して少し広め。モールの幅は 12 m。</summary>
        public const float PoolRadius = 5.5f;

        /// <summary>光だまりの分割。16 角形 × 2 重の輪。</summary>
        private const int PoolSegments = 16;

        /// <summary>光だまりを地面から浮かせる高さ（m）。</summary>
        private const float PoolLift = 0.03f;

        /// <summary>灯具の下面の発光面（props.add_lamp の line_white の箱の底, 0.45 × 0.22 m）。</summary>
        private static readonly Vector2 EmitterSize = new Vector2(0.45f, 0.22f);

        /// <summary>発光面を下面から下げる量（m）。箱の底と同じ高さだと奥行きが競る。</summary>
        private const float EmitterDrop = 0.01f;

        /// <summary>暈の中心を発光面から下げる量（m）。</summary>
        private const float HaloDrop = 0.12f;

        /// <summary>地面を探すレイの長さ（m）。灯具の高さ 4 m より長く。</summary>
        private const float GroundProbeLength = 12f;

        /// <summary>
        /// campus を読んで街灯の明かりを置く。照明柱が見つからなければ何も置かず null。
        /// campus の当たり判定（DressCampus）を付けたあとに呼ぶ（光だまりを地面へ落とすのに使う）。
        /// </summary>
        public static StreetLights Build(Transform root, GameObject campus)
        {
            MeshFilter furniture = FindFurniture(campus);
            if (furniture == null)
            {
                EditorPaths.Report("街灯: " + FurnitureName + " が見つからないので置きませんでした。");
                return null;
            }

            List<Vector3> world = WorldVertices(furniture);
            List<Vector3> lamps = StreetLampLayout.FindLamps(world);
            if (lamps.Count == 0)
            {
                EditorPaths.Report("街灯: " + FurnitureName + " に照明柱の灯具が見つからないので置きませんでした。");
                return null;
            }

            Material material = SkyFactory.EnsureMaterial(MaterialPath, ShaderName);
            if (material == null)
            {
                return null;
            }

            float fallbackGround = LowestY(world);
            var go = new GameObject(ObjectName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Physics.SyncTransforms();
            Mesh mesh = SaveMesh(BuildMesh(go.transform, lamps, fallbackGround));

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            // ゲームは朝に始まるので消しておく。灯すのは StreetLights。
            renderer.enabled = false;

            StreetLights lights = go.AddComponent<StreetLights>();
            lights.SetLamps(lamps);

            // 光だまりは地面の上に貼る板なので、NavMesh を焼く材料に入れない。
            CampusStage.Ignore(go);

            EditorPaths.Report("街灯: 照明柱 " + lamps.Count + " 本に夜の明かりを付けました（実ライト 0 灯、"
                + mesh.vertexCount + " 頂点 / " + (mesh.triangles.Length / 3) + " 三角形、描画は夜だけ 1 回）。");
            return lights;
        }

        private static MeshFilter FindFurniture(GameObject campus)
        {
            if (campus == null)
            {
                return null;
            }

            foreach (MeshFilter filter in campus.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == FurnitureName && filter.sharedMesh != null)
                {
                    return filter;
                }
            }

            return null;
        }

        private static List<Vector3> WorldVertices(MeshFilter filter)
        {
            Vector3[] local = filter.sharedMesh.vertices;
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            var world = new List<Vector3>(local.Length);
            foreach (Vector3 v in local)
            {
                world.Add(toWorld.MultiplyPoint3x4(v));
            }

            return world;
        }

        private static float LowestY(List<Vector3> points)
        {
            float lowest = float.MaxValue;
            foreach (Vector3 p in points)
            {
                lowest = Mathf.Min(lowest, p.y);
            }

            return lowest;
        }

        /// <summary>
        /// 灯具ごとの光だまり・発光面・暈をまとめたメッシュ。頂点は owner のローカル座標。
        /// UV0 は (x, y, 部品, 0)。部品は 0 = 光だまり、1 = 発光面、2 = 暈（KCD_LampGlow.shader）。
        /// </summary>
        private static Mesh BuildMesh(Transform owner, List<Vector3> lamps, float fallbackGround)
        {
            var vertices = new List<Vector3>();
            var shapes = new List<Vector4>();
            var triangles = new List<int>();
            int groundMask = LayerMask.GetMask("Ground");

            foreach (Vector3 lamp in lamps)
            {
                AddPool(owner, lamp, groundMask, fallbackGround, vertices, shapes, triangles);
                AddEmitter(owner, lamp, vertices, shapes, triangles);
                AddHalo(owner, lamp, vertices, shapes, triangles);
            }

            var mesh = new Mesh { name = "StreetLampGlow" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, shapes);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // 暈は頂点シェーダで板を広げてカメラへ寄せるので、そのぶん境界を広げて視錐台カリングで欠けないようにする。
            Bounds bounds = mesh.bounds;
            bounds.Expand(4f);
            mesh.bounds = bounds;
            return mesh;
        }

        private static void AddPool(Transform owner, Vector3 lamp, int groundMask, float fallbackGround,
            List<Vector3> vertices, List<Vector4> shapes, List<int> triangles)
        {
            int center = vertices.Count;
            AddPoolVertex(owner, lamp, Vector2.zero, groundMask, fallbackGround, vertices, shapes);
            for (int ring = 1; ring <= 2; ring++)
            {
                float radius = ring * 0.5f;
                for (int s = 0; s < PoolSegments; s++)
                {
                    float angle = s * Mathf.PI * 2f / PoolSegments;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    AddPoolVertex(owner, lamp, offset, groundMask, fallbackGround, vertices, shapes);
                }
            }

            // 中心の扇と、内側の輪と外側の輪のあいだの帯。上から見て表（時計回り）。Cull Off なので向きは見た目に効かない。
            for (int s = 0; s < PoolSegments; s++)
            {
                int next = (s + 1) % PoolSegments;
                int inner = center + 1;
                int outer = center + 1 + PoolSegments;
                triangles.Add(center);
                triangles.Add(inner + next);
                triangles.Add(inner + s);

                triangles.Add(inner + s);
                triangles.Add(inner + next);
                triangles.Add(outer + next);
                triangles.Add(inner + s);
                triangles.Add(outer + next);
                triangles.Add(outer + s);
            }
        }

        private static void AddPoolVertex(Transform owner, Vector3 lamp, Vector2 offset, int groundMask,
            float fallbackGround, List<Vector3> vertices, List<Vector4> shapes)
        {
            float x = lamp.x + offset.x * PoolRadius;
            float z = lamp.z + offset.y * PoolRadius;
            float y = GroundHeight(new Vector3(x, lamp.y - 0.5f, z), groundMask, fallbackGround);
            vertices.Add(owner.InverseTransformPoint(new Vector3(x, y + PoolLift, z)));
            shapes.Add(new Vector4(offset.x, offset.y, 0f, 0f));
        }

        /// <summary>真下の地面（Ground レイヤー）の高さ。灯具の少し下からレイを落とす。当たらなければ fallback。</summary>
        private static float GroundHeight(Vector3 from, int groundMask, float fallback)
        {
            if (groundMask != 0 && Physics.Raycast(from, Vector3.down, out RaycastHit hit, GroundProbeLength,
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return fallback;
        }

        private static void AddEmitter(Transform owner, Vector3 lamp,
            List<Vector3> vertices, List<Vector4> shapes, List<int> triangles)
        {
            // 灯具の箱はワールドの x/z 軸にそろっている（add_box_c は Blender のワールド軸、FBX の取り込みと
            // PlaceModel の 180° 回転で向きは変わっても軸は x/z のまま）。
            float y = lamp.y - EmitterDrop;
            float hx = EmitterSize.x * 0.5f;
            float hz = EmitterSize.y * 0.5f;
            AddQuad(owner, vertices, shapes, triangles, 1f,
                new Vector3(lamp.x - hx, y, lamp.z - hz), new Vector3(lamp.x + hx, y, lamp.z - hz),
                new Vector3(lamp.x + hx, y, lamp.z + hz), new Vector3(lamp.x - hx, y, lamp.z + hz),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        private static void AddHalo(Transform owner, Vector3 lamp,
            List<Vector3> vertices, List<Vector4> shapes, List<int> triangles)
        {
            // 4 頂点とも灯具の位置に置き、四隅の向きだけを持たせる。広げるのは頂点シェーダ。
            Vector3 c = new Vector3(lamp.x, lamp.y - HaloDrop, lamp.z);
            AddQuad(owner, vertices, shapes, triangles, 2f, c, c, c, c,
                new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f));
        }

        private static void AddQuad(Transform owner, List<Vector3> vertices, List<Vector4> shapes, List<int> triangles,
            float kind, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 sa, Vector2 sb, Vector2 sc, Vector2 sd)
        {
            int i = vertices.Count;
            vertices.Add(owner.InverseTransformPoint(a));
            vertices.Add(owner.InverseTransformPoint(b));
            vertices.Add(owner.InverseTransformPoint(c));
            vertices.Add(owner.InverseTransformPoint(d));
            shapes.Add(new Vector4(sa.x, sa.y, kind, 0f));
            shapes.Add(new Vector4(sb.x, sb.y, kind, 0f));
            shapes.Add(new Vector4(sc.x, sc.y, kind, 0f));
            shapes.Add(new Vector4(sd.x, sd.y, kind, 0f));
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        /// <summary>Assets/Generated/Meshes/StreetLampGlow.asset に保存する。既にあれば中身だけ入れ替える。</summary>
        private static Mesh SaveMesh(Mesh built)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                EditorPaths.EnsureFolder(EditorPaths.GeneratedFolder + "/Meshes");
                AssetDatabase.CreateAsset(built, MeshPath);
                return built;
            }

            existing.Clear();
            var uvs = new List<Vector4>();
            built.GetUVs(0, uvs);
            existing.SetVertices(built.vertices);
            existing.SetUVs(0, uvs);
            existing.SetTriangles(built.triangles, 0);
            existing.bounds = built.bounds;
            existing.name = built.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            return existing;
        }
    }
}
