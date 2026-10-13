using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Editor
{
    /// <summary>
    /// 夜に研究棟・実験棟の窓をぽつぽつ点ける (#166)。
    ///
    /// campus.fbx の窓ガラス（facade.py の add_facade が窓 1 枚ごとに貼る四角形、マテリアル glass_*）を
    /// 建物ごとに拾い、決まった割合の窓だけ、ガラスの外側に発光面を貼る。上の階（研究室）は
    /// <see cref="UpperFloorRatio"/>、1 階は <see cref="GroundFloorRatio"/>。どの窓を点けるかは窓の位置から
    /// 決まる値で選ぶので、シーンを組み直しても同じ窓が点く。発光面は 1 つのメッシュ
    /// （Assets/Generated/Meshes/WindowGlow.asset）にまとめ、街灯と同じ KCD/LampGlow の発光面（部品 1）で描く。
    /// </summary>
    public static class WindowLightStage
    {
        public const string ObjectName = "WindowLights";
        public const string MaterialPath = "Assets/Materials/Sky/KCD_WindowGlow.mat";
        public const string MeshPath = EditorPaths.GeneratedFolder + "/Meshes/WindowGlow.asset";

        /// <summary>窓を点ける建物（研究棟・実験棟）のメッシュ名。</summary>
        public static readonly string[] BuildingNames = { "bld_research1", "bld_research2", "bld_lab1", "bld_lab2" };

        /// <summary>2 階より上で点ける窓の割合（研究室に残っている人の部屋）。</summary>
        public const float UpperFloorRatio = 0.18f;

        /// <summary>1 階で点ける窓の割合（廊下や玄関まわりが多いので少なめ）。</summary>
        public const float GroundFloorRatio = 0.06f;

        /// <summary>窓の下端が建物の足もとからこれより低ければ 1 階とみなす（m）。1 階の腰壁は 1.1 m 前後。</summary>
        private const float GroundFloorTop = 2.5f;

        /// <summary>発光面をガラスから外へ浮かせる量（m）。ガラスと同じ面だと奥行きが競る。</summary>
        private const float EmitterLift = 0.02f;

        /// <summary>
        /// 窓の頂点の法線の水平成分が、平均してこれより短ければ外向きが定まらないとみなす
        /// （両面のガラスは表裏で打ち消し合い、天窓は上を向く）。
        /// </summary>
        private const float MinFacing = 0.5f;

        /// <summary>同じ頂点とみなす距離（m）。窓どうしは方立で 0.3 m ほど離れている。</summary>
        private const float WeldDistance = 0.001f;

        /// <summary>窓の明かりの色（蛍光灯の昼白色を少し暖かく）と強さ。強さ 1 を少し超えて Bloom が軽く掛かる。</summary>
        private static readonly Color WindowColor = new Color(1.0f, 0.88f, 0.66f, 1f);
        private const float EmitterStrength = 1.3f;

        /// <summary>窓ガラス 1 枚。</summary>
        public struct Pane
        {
            public Vector3[] Corners;
            public Vector3 Center;
            public Vector3 Normal;
            public bool GroundFloor;
        }

        /// <summary>
        /// campus を読んで窓の明かりを置く。対象の建物か窓ガラスが見つからなければ何も置かず null。
        /// </summary>
        public static WindowLights Build(Transform root, GameObject campus)
        {
            var panes = new List<Pane>();
            int buildings = 0;
            foreach (MeshRenderer renderer in Buildings(campus))
            {
                List<Pane> found = FindPanes(renderer);
                if (found.Count > 0)
                {
                    buildings++;
                    panes.AddRange(found);
                }
            }

            if (panes.Count == 0)
            {
                EditorPaths.Report("窓の明かり: 研究棟・実験棟の窓ガラスが見つからないので置きませんでした。");
                return null;
            }

            var lit = new List<Pane>();
            foreach (Pane pane in panes)
            {
                if (IsLit(pane))
                {
                    lit.Add(pane);
                }
            }

            Material material = SkyFactory.EnsureMaterial(MaterialPath, StreetLampStage.ShaderName);
            if (material == null)
            {
                return null;
            }

            ConfigureMaterial(material);

            var go = new GameObject(ObjectName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Mesh mesh = StreetLampStage.SaveGlowMesh(BuildMesh(go.transform, lit), MeshPath);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer glow = go.AddComponent<MeshRenderer>();
            glow.sharedMaterial = material;
            glow.shadowCastingMode = ShadowCastingMode.Off;
            glow.receiveShadows = false;
            glow.lightProbeUsage = LightProbeUsage.Off;
            glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
            glow.allowOcclusionWhenDynamic = false;
            // ゲームは朝に始まるので消しておく。灯すのは WindowLights。
            glow.enabled = false;

            var centers = new List<Vector3>(lit.Count);
            foreach (Pane pane in lit)
            {
                centers.Add(pane.Center);
            }

            WindowLights lights = go.AddComponent<WindowLights>();
            lights.SetWindows(centers, panes.Count);

            // 窓に貼る板なので、NavMesh を焼く材料に入れない。
            CampusStage.Ignore(go);

            EditorPaths.Report("窓の明かり: 建物 " + buildings + " 棟の窓 " + panes.Count + " 枚のうち " + lit.Count
                + " 枚を夜に点けます（実ライト 0 灯、描画は夜だけ 1 回）。");
            return lights;
        }

        /// <summary>その窓を夜に点けるか。窓の位置から決まる値で選ぶので、組み直しても変わらない。</summary>
        public static bool IsLit(Pane pane)
        {
            return Hash01(pane.Center) < (pane.GroundFloor ? GroundFloorRatio : UpperFloorRatio);
        }

        /// <summary>位置（cm に丸める）から決まる 0 以上 1 未満の値。</summary>
        public static float Hash01(Vector3 position)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)Mathf.RoundToInt(position.x * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(position.y * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(position.z * 100f)) * 16777619u;
                // 近い位置どうしで値が偏らないよう、下位ビットまでかき混ぜる。
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        private static IEnumerable<MeshRenderer> Buildings(GameObject campus)
        {
            if (campus == null)
            {
                yield break;
            }

            var names = new HashSet<string>(BuildingNames);
            foreach (MeshRenderer renderer in campus.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (names.Contains(renderer.name) && renderer.TryGetComponent(out MeshFilter filter)
                    && filter.sharedMesh != null)
                {
                    yield return renderer;
                }
            }
        }

        /// <summary>建物のメッシュから、マテリアル名が glass で始まるサブメッシュの窓を 1 枚ずつ拾う。</summary>
        public static List<Pane> FindPanes(MeshRenderer renderer)
        {
            var panes = new List<Pane>();
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals.Length != vertices.Length)
            {
                // 発光面を貼る側（ガラスの外）が法線でしか分からない。
                Debug.LogWarning("[KCD] 窓の明かり: " + renderer.name + " に法線が無いので窓を拾いません。");
                return panes;
            }

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            float floor = renderer.bounds.min.y;

            for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
            {
                if (materials[sub] == null || !materials[sub].name.StartsWith("glass"))
                {
                    continue;
                }

                int[] triangles = mesh.GetTriangles(sub);
                foreach (List<int> group in GroupByTouch(vertices, triangles))
                {
                    if (TryMakePane(group, vertices, normals, toWorld, floor, out Pane pane))
                    {
                        panes.Add(pane);
                    }
                }
            }

            return panes;
        }

        /// <summary>頂点を共有する三角形どうしを 1 枚の窓にまとめ、窓ごとの頂点の番号を返す。</summary>
        private static List<List<int>> GroupByTouch(Vector3[] vertices, int[] triangles)
        {
            int count = triangles.Length / 3;
            var parent = new int[count];
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
            }

            // 位置で頂点を溶接する（FBX は面ごとに頂点を分けて取り込むことがある）。
            var owner = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < count; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3Int key = Weld(vertices[triangles[t * 3 + k]]);
                    if (owner.TryGetValue(key, out int other))
                    {
                        Union(parent, t, other);
                    }
                    else
                    {
                        owner[key] = t;
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (int t = 0; t < count; t++)
            {
                int rootIndex = Find(parent, t);
                if (!groups.TryGetValue(rootIndex, out List<int> group))
                {
                    group = new List<int>();
                    groups[rootIndex] = group;
                }

                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[t * 3 + k];
                    if (!group.Contains(index))
                    {
                        group.Add(index);
                    }
                }
            }

            return new List<List<int>>(groups.Values);
        }

        private static Vector3Int Weld(Vector3 v)
        {
            return new Vector3Int(
                Mathf.RoundToInt(v.x / WeldDistance),
                Mathf.RoundToInt(v.y / WeldDistance),
                Mathf.RoundToInt(v.z / WeldDistance));
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                parent[ra] = rb;
            }
        }

        /// <summary>
        /// 窓の頂点から、ガラスの面に沿った四隅・中心・外向きの法線を求める。
        /// 窓は鉛直な長方形（add_facade）なので、法線に直交する水平軸と鉛直軸で囲む。
        /// ガラスの法線は壁と同じく外向き（add_facade は壁と同じ巻き順でガラスを貼る）。
        /// 天窓や両面のガラスのように水平な外向きが定まらないものは拾わない。
        /// </summary>
        private static bool TryMakePane(List<int> indices, Vector3[] vertices, Vector3[] normals,
            Matrix4x4 toWorld, float floor, out Pane pane)
        {
            pane = default;
            var points = new List<Vector3>(indices.Count);
            Vector3 normal = Vector3.zero;
            foreach (int i in indices)
            {
                points.Add(toWorld.MultiplyPoint3x4(vertices[i]));
                normal += toWorld.MultiplyVector(normals[i]).normalized;
            }

            normal.y = 0f;
            if (normal.magnitude < MinFacing * indices.Count)
            {
                return false;
            }

            normal.Normalize();
            Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;

            float minU = float.MaxValue;
            float maxU = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            float depth = 0f;
            Vector3 origin = points[0];
            foreach (Vector3 p in points)
            {
                Vector3 d = p - origin;
                float u = Vector3.Dot(d, along);
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
                depth += Vector3.Dot(d, normal);
            }

            depth /= points.Count;
            Vector3 face = origin + normal * (depth + EmitterLift);
            face.y = 0f;
            var corners = new[]
            {
                face + along * minU + Vector3.up * minY,
                face + along * maxU + Vector3.up * minY,
                face + along * maxU + Vector3.up * maxY,
                face + along * minU + Vector3.up * maxY,
            };

            pane = new Pane
            {
                Corners = corners,
                Center = (corners[0] + corners[2]) * 0.5f,
                Normal = normal,
                GroundFloor = minY - floor < GroundFloorTop,
            };
            return true;
        }

        private static void ConfigureMaterial(Material material)
        {
            material.SetColor("_LampColor", WindowColor);
            material.SetFloat("_EmitterStrength", EmitterStrength);
            EditorUtility.SetDirty(material);
        }

        /// <summary>点ける窓の発光面をまとめたメッシュ。頂点は owner のローカル座標、UV0 の z = 1 が発光面。</summary>
        private static Mesh BuildMesh(Transform owner, List<Pane> panes)
        {
            var vertices = new List<Vector3>(panes.Count * 4);
            var shapes = new List<Vector4>(panes.Count * 4);
            var triangles = new List<int>(panes.Count * 6);
            foreach (Pane pane in panes)
            {
                int i = vertices.Count;
                foreach (Vector3 corner in pane.Corners)
                {
                    vertices.Add(owner.InverseTransformPoint(corner));
                    shapes.Add(new Vector4(0f, 0f, 1f, 0f));
                }

                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            var mesh = new Mesh { name = "WindowGlow" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, shapes);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
