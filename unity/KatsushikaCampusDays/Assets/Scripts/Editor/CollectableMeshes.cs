using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 拾い物の見た目（#56）。以前は球で、公園の「理科大グリーンの葉」が緑の玉に見えていた。
    ///
    /// 葉は中央の葉脈で少し折れた先の尖った 1 枚（両面）+ 葉柄、牛乳は切妻屋根の紙パック。
    /// どちらも実物より一回り大きくして、芝生の上でも見つけやすくしてある。
    /// メッシュは Assets/Generated/Meshes に保存し、作り直しても GUID を変えない。
    /// </summary>
    public static class CollectableMeshes
    {
        public const string MeshFolder = EditorPaths.GeneratedFolder + "/Meshes";

        /// <summary>葉の長さ（m、葉柄を除く）。実物の 2 倍ほど。</summary>
        public const float LeafLength = 0.34f;

        /// <summary>葉のいちばん広い所の幅（m）。</summary>
        public const float LeafWidth = 0.17f;

        /// <summary>中央の葉脈での折れ（度）。両側の葉身が葉脈から上へこの角度で立つ。</summary>
        public const float LeafFoldDeg = 12f;

        /// <summary>葉の傾き（度）。回りながら表と裏の両方が見えるように、水平から起こす。</summary>
        public const float LeafTiltDeg = 25f;

        /// <summary>見た目のメッシュとマテリアル（サブメッシュ順）。</summary>
        public struct Model
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        /// <summary>隠しアイテムの宝石の半径（m、赤道）。</summary>
        public const float GemRadius = 0.14f;

        /// <summary>隠しアイテムの宝石の中心から頂点までの高さ（m）。</summary>
        public const float GemHalfHeight = 0.22f;

        /// <summary>写真スポットの三脚の、カメラの高さ（m、足元から）。</summary>
        public const float TripodCameraHeight = 1.3f;

        /// <summary>
        /// itemId に合う見た目。牛乳は紙パック、隠しアイテム（c_*）は materialName の色の宝石、
        /// それ以外は materialName の色の葉（葉は理科大グリーン）。
        /// </summary>
        public static Model For(string itemId, string materialName)
        {
            if (itemId != null && itemId.StartsWith("c_"))
            {
                return new Model
                {
                    Mesh = Save(BuildGem(), "Gem"),
                    Materials = new[] { MaterialLibrary.EnsureCampus(materialName) },
                };
            }

            if (itemId == "milk")
            {
                return new Model
                {
                    Mesh = Save(BuildMilkCarton(), "MilkCarton"),
                    Materials = new[]
                    {
                        MaterialLibrary.EnsureCampus("white"),
                        MaterialLibrary.EnsureCampus("vending_blue"),
                    },
                };
            }

            return new Model
            {
                Mesh = Save(BuildLeaf(), "Leaf"),
                Materials = new[] { MaterialLibrary.EnsureCampus(itemId == "leaf" ? "tus_green" : materialName) },
            };
        }

        /// <summary>
        /// 葉。+Z が先端、原点が葉身の中心。葉身は 8 区間で、幅は付け根から 40% の所で最大になり
        /// 先端で 0 になる。両側の葉身を葉脈から LeafFoldDeg だけ起こし、先端をわずかに垂らす。
        /// 裏面は頂点を分けて法線を逆にした面を重ねる（片面シェーダでも裏から見える）。
        /// </summary>
        public static Mesh BuildLeaf()
        {
            const int segments = 8;
            float halfLength = LeafLength * 0.5f;
            float lift = Mathf.Tan(LeafFoldDeg * Mathf.Deg2Rad);

            var front = new List<Vector3>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                // 付け根は丸く、先は尖る。t = 0.4 付近で最大幅。
                float w = LeafWidth * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.72f));
                float z = -halfLength + LeafLength * t;
                float droop = -0.035f * t * t;
                front.Add(new Vector3(0f, droop, z));                  // 葉脈
                front.Add(new Vector3(-w, droop + w * lift, z));       // 左の縁
                front.Add(new Vector3(w, droop + w * lift, z));        // 右の縁
            }

            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                int a = i * 3;
                int b = a + 3;
                // 上から見て時計回り（Unity の表）。
                tris.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
                tris.AddRange(new[] { a, b, b + 2, a, b + 2, a + 2 });
            }

            var vertices = new List<Vector3>(front);
            var triangles = new List<int>(tris);
            int back = vertices.Count;
            vertices.AddRange(front);
            for (int i = 0; i < tris.Count; i += 3)
            {
                triangles.Add(back + tris[i]);
                triangles.Add(back + tris[i + 2]);
                triangles.Add(back + tris[i + 1]);
            }

            AddBox(vertices, triangles, new Vector3(0f, 0f, -halfLength - 0.03f), new Vector3(0.012f, 0.012f, 0.06f));

            Quaternion tilt = Quaternion.Euler(-LeafTiltDeg, 0f, 0f);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = tilt * vertices[i];
            }

            var mesh = new Mesh { name = "Leaf" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 200 ml の紙パック（実物 6.5 x 6.5 x 12 cm）を 2 倍にしたもの。原点は胴の中心。
        /// サブメッシュ 0 = 白い胴と屋根、1 = 青い帯。
        /// </summary>
        public static Mesh BuildMilkCarton()
        {
            const float side = 0.13f;
            const float body = 0.24f;
            const float gable = 0.06f;
            float h = side * 0.5f;
            float top = body * 0.5f;

            var vertices = new List<Vector3>();
            var white = new List<int>();
            var blue = new List<int>();

            AddBox(vertices, white, Vector3.zero, new Vector3(side, body, side));
            // 青い帯は胴の上から 1/3 に、胴よりわずかに太く巻く。
            AddBox(vertices, blue, new Vector3(0f, top * 0.25f, 0f), new Vector3(side + 0.004f, body * 0.28f, side + 0.004f));

            // 切妻屋根（X 方向に棟）と、棟の上の閉じ代。
            Vector3 ridge0 = new Vector3(-h, top + gable, 0f);
            Vector3 ridge1 = new Vector3(h, top + gable, 0f);
            Vector3 n0 = new Vector3(-h, top, h);
            Vector3 n1 = new Vector3(h, top, h);
            Vector3 s0 = new Vector3(-h, top, -h);
            Vector3 s1 = new Vector3(h, top, -h);
            AddQuad(vertices, white, n0, n1, ridge1, ridge0);
            AddQuad(vertices, white, s1, s0, ridge0, ridge1);
            AddTri(vertices, white, s0, n0, ridge0);
            AddTri(vertices, white, n1, s1, ridge1);
            AddBox(vertices, white, new Vector3(0f, top + gable + 0.012f, 0f), new Vector3(side, 0.024f, 0.006f));

            var mesh = new Mesh { name = "MilkCarton", subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(white, 0);
            mesh.SetTriangles(blue, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>写真スポットの三脚の見た目。サブメッシュ 0 = 足元の円盤、1 = 脚、2 = カメラ。</summary>
        public static Model Tripod()
        {
            return new Model
            {
                Mesh = Save(BuildTripod(), "Tripod"),
                Materials = new[]
                {
                    MaterialLibrary.EnsureCampus("tus_green"),
                    MaterialLibrary.EnsureCampus("metal_grey"),
                    MaterialLibrary.EnsureCampus("bike_frame"),
                },
            };
        }

        /// <summary>
        /// 隠しアイテムの宝石。上下に尖った正八面体（面ごとに頂点を分けて角を立てる）。原点が中心。
        /// 葉や牛乳と違う形にして、クエストの拾い物ではないことが遠目にも分かるようにする (#65)。
        /// </summary>
        public static Mesh BuildGem()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 top = new Vector3(0f, GemHalfHeight, 0f);
            Vector3 bottom = new Vector3(0f, -GemHalfHeight, 0f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 e0 = Equator(i);
                Vector3 e1 = Equator(i + 1);
                // 角度が増える向きに e0 → e1。外から見て時計回りになるよう並べる。
                AddTri(vertices, triangles, e1, e0, top);
                AddTri(vertices, triangles, bottom, e0, e1);
            }

            var mesh = new Mesh { name = "Gem" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Equator(int quarter)
        {
            float angle = quarter * 90f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * GemRadius, 0f, Mathf.Sin(angle) * GemRadius);
        }

        /// <summary>
        /// 写真スポットの三脚。原点が足元の中心で、+Z がカメラの向き（collectibles.json の look_dir）。
        /// 足元に理科大グリーンの円盤（半径 0.55 m）、3 本の脚、その上に黒いカメラとレンズ。
        /// </summary>
        public static Mesh BuildTripod()
        {
            var vertices = new List<Vector3>();
            var plate = new List<int>();
            var legs = new List<int>();
            var camera = new List<int>();

            const int sides = 16;
            const float plateRadius = 0.55f;
            const float plateHeight = 0.015f;
            Vector3 center = new Vector3(0f, plateHeight, 0f);
            for (int i = 0; i < sides; i++)
            {
                Vector3 top0 = Rim(i, sides, plateRadius, plateHeight);
                Vector3 top1 = Rim(i + 1, sides, plateRadius, plateHeight);
                Vector3 bottom0 = Rim(i, sides, plateRadius, 0f);
                Vector3 bottom1 = Rim(i + 1, sides, plateRadius, 0f);
                AddTri(vertices, plate, center, top1, top0);
                AddQuad(vertices, plate, bottom1, bottom0, top0, top1);
            }

            Vector3 head = new Vector3(0f, TripodCameraHeight - 0.1f, 0f);
            const float footRadius = 0.38f;
            // 1 本を後ろ（-Z）、2 本を前の左右へ開く。レンズの前に脚が来ないようにする。
            float[] footAngles = { 270f, 30f, 150f };
            foreach (float degrees in footAngles)
            {
                float a = degrees * Mathf.Deg2Rad;
                Vector3 foot = new Vector3(Mathf.Cos(a) * footRadius, plateHeight, Mathf.Sin(a) * footRadius);
                AddBeam(vertices, legs, foot, head, 0.03f);
            }

            AddBox(vertices, legs, new Vector3(0f, TripodCameraHeight - 0.09f, 0f), new Vector3(0.09f, 0.04f, 0.09f));
            AddBox(vertices, camera, new Vector3(0f, TripodCameraHeight, 0f), new Vector3(0.22f, 0.14f, 0.12f));
            AddBox(vertices, camera, new Vector3(0f, TripodCameraHeight, 0.1f), new Vector3(0.08f, 0.08f, 0.08f));

            var mesh = new Mesh { name = "Tripod", subMeshCount = 3 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(plate, 0);
            mesh.SetTriangles(legs, 1);
            mesh.SetTriangles(camera, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Rim(int index, int sides, float radius, float y)
        {
            float angle = index * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        /// <summary>from から to へ伸びる角柱（断面 thickness 角）。</summary>
        private static void AddBeam(List<Vector3> vertices, List<int> triangles, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 axis = to - from;
            Vector3 middle = (from + to) * 0.5f;
            int first = vertices.Count;
            AddBox(vertices, triangles, Vector3.zero, new Vector3(thickness, thickness, axis.magnitude));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, axis.normalized);
            for (int i = first; i < vertices.Count; i++)
            {
                vertices[i] = middle + rotation * vertices[i];
            }
        }

        /// <summary>面ごとに頂点を分けた箱（角を立てる）。</summary>
        private static void AddBox(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 size)
        {
            Vector3 e = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + new Vector3(x * e.x, y * e.y, z * e.z);
            AddQuad(vertices, triangles, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));     // +Z
            AddQuad(vertices, triangles, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)); // -Z
            AddQuad(vertices, triangles, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));     // +X
            AddQuad(vertices, triangles, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)); // -X
            AddQuad(vertices, triangles, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));     // +Y
            AddQuad(vertices, triangles, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // -Y
        }

        /// <summary>外から見て a → b → c → d が時計回り（Unity の表）の四角形。</summary>
        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        private static void AddTri(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c });
            triangles.AddRange(new[] { i, i + 1, i + 2 });
        }

        /// <summary>Assets/Generated/Meshes/&lt;name&gt;.asset に保存する。既にあれば中身だけ入れ替える。</summary>
        private static Mesh Save(Mesh built, string name)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                if (!AssetDatabase.IsValidFolder(MeshFolder))
                {
                    AssetDatabase.CreateFolder(EditorPaths.GeneratedFolder, "Meshes");
                }

                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            existing.Clear();
            existing.subMeshCount = built.subMeshCount;
            existing.SetVertices(built.vertices);
            for (int i = 0; i < built.subMeshCount; i++)
            {
                existing.SetTriangles(built.GetTriangles(i), i);
            }

            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            return existing;
        }
    }
}
