using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// キャンパスの木が、再生の始めにマスごとの少ないメッシュへまとめて描かれることを守る (#57, #70)。
    ///
    /// 木 553 本を 1 本ずつ描くと、WebGL で描画コールが約 4,700 回（木のない版は 459 回）になり、
    /// CPU 側で 35〜45 fps まで落ちた。TreeChunkCombiner がまとめた結果を、エディタで同じ関数を呼んで確かめる。
    /// </summary>
    public sealed class CampusTreeChunkTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string TreesFbx = "Assets/Models/Campus/trees.fbx";

        /// <summary>
        /// まとめたあと、木の描画に使うマテリアルの数（パス 1 つあたりの描画コールの上限）。
        /// 木は 128 m のマス 13 個にまたがり、3 種の原型は幹と葉のマテリアル 2 つを共有する（葉の明暗は頂点カラー, #51）。
        /// マス 1 つが 1 つのメッシュにまとまれば 13 × 2 = 26。影やマテリアルの違う木が混ざってマスが割れても、
        /// 木のない版の描画コール（459 回）に比べて小さく抑える。
        /// </summary>
        private const int MaxTreeDrawsPerPass = 80;

        private Scene _scene;
        private TreeChunkCombiner _combiner;
        private readonly List<MeshRenderer> _bodies = new List<MeshRenderer>();
        private readonly List<Bounds> _bodyBounds = new List<Bounds>();
        private readonly List<Vector3[]> _bodyVertices = new List<Vector3[]>();
        private readonly List<Vector3[]> _bodyNormals = new List<Vector3[]>();
        private readonly Dictionary<Material, int> _sourceTrianglesByMaterial = new Dictionary<Material, int>();
        private int _sourceVertices;
        private int _sourceTriangles;
        private float _sourceFacingRatio;

        [OneTimeSetUp]
        public void OpenAndCombine()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                TreeChunkCombiner combiner = go.GetComponentInChildren<TreeChunkCombiner>(true);
                if (combiner != null)
                {
                    _combiner = combiner;
                }
            }

            Assert.IsNotNull(_combiner, ScenePath + " の木に TreeChunkCombiner が付いていない");

            var facing = new FacingCount();
            foreach (MeshRenderer renderer in _combiner.GetComponentsInChildren<MeshRenderer>(false))
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (!renderer.enabled || mesh == null)
                {
                    continue;
                }

                Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
                _bodies.Add(renderer);
                _bodyBounds.Add(WorldBounds(mesh, localToWorld));
                _bodyVertices.Add(WorldPoints(mesh.vertices, localToWorld));
                _bodyNormals.Add(WorldNormals(mesh.normals, localToWorld));
                _sourceVertices += mesh.vertexCount;
                _sourceTriangles += TriangleCount(mesh);
                AddTrianglesByMaterial(_sourceTrianglesByMaterial, renderer, mesh);
                facing.Add(mesh);
            }

            _sourceFacingRatio = facing.Ratio;
            _combiner.Combine();
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_combiner != null)
            {
                foreach (GameObject chunk in _combiner.Chunks)
                {
                    if (chunk != null)
                    {
                        Object.DestroyImmediate(chunk.GetComponent<MeshFilter>().sharedMesh);
                    }
                }
            }

            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void trees_fbx_は実行時にまとめられるよう読み込める()
        {
            var importer = AssetImporter.GetAtPath(TreesFbx) as ModelImporter;
            Assert.IsNotNull(importer, TreesFbx + " が無い");
            // 読めないメッシュはビルドした版でまとめられず、1 本ずつ描かれたままになる。
            Assert.IsTrue(importer.isReadable, TreesFbx + " の Read/Write が無効");
        }

        [Test]
        public void 木はまとめたメッシュだけで描かれ_描画コールが少ない()
        {
            Assert.Greater(_bodies.Count, 500, "まとめる前の木が少なすぎる");
            Assert.Greater(_combiner.Chunks.Count, 0, "まとめたメッシュが無い");

            int draws = 0;
            var stray = new List<string>();
            foreach (MeshRenderer renderer in _combiner.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                draws += renderer.sharedMaterials.Length;
                if (!renderer.name.StartsWith(TreeChunkCombiner.ChunkPrefix, System.StringComparison.Ordinal))
                {
                    stray.Add(renderer.transform.parent != null ? renderer.transform.parent.name : renderer.name);
                }
            }

            Assert.IsEmpty(stray, stray.Count + " 本がまとめられずに 1 本ずつ描かれる: "
                + string.Join(", ", stray.GetRange(0, Mathf.Min(stray.Count, 10))));
            Assert.LessOrEqual(draws, MaxTreeDrawsPerPass, "木の描画コールが多すぎる（マス " + _combiner.Chunks.Count + " 個）");
        }

        [Test]
        public void まとめたメッシュは元の木の頂点と三角形をすべて持つ()
        {
            int vertices = 0;
            int triangles = 0;
            var byMaterial = new Dictionary<Material, int>();
            foreach (GameObject chunk in _combiner.Chunks)
            {
                Mesh mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
                vertices += mesh.vertexCount;
                triangles += TriangleCount(mesh);
                AddTrianglesByMaterial(byMaterial, chunk.GetComponent<MeshRenderer>(), mesh);
            }

            Assert.AreEqual(_sourceVertices, vertices, "頂点の数が合わない（抜けているか、余分に写している）");
            Assert.AreEqual(_sourceTriangles, triangles, "三角形の数が合わない");

            // 幹の三角形が葉のマテリアルで描かれるような、サブメッシュの取り違えが無いこと。
            var wrong = new List<string>();
            foreach (KeyValuePair<Material, int> source in _sourceTrianglesByMaterial)
            {
                byMaterial.TryGetValue(source.Key, out int merged);
                if (merged != source.Value)
                {
                    wrong.Add(string.Format("{0}: 元 {1} → まとめたあと {2}", source.Key != null ? source.Key.name : "null", source.Value, merged));
                }
            }

            Assert.IsEmpty(wrong, "マテリアルごとの三角形の数が合わない:\n" + string.Join("\n", wrong));
        }

        [Test]
        public void どの木も元の位置と向きのまままとめたメッシュに入っている()
        {
            // まとめたメッシュの頂点は、まとめた木の頂点を木の順に並べたもの。マスの中に収まっていても、
            // 1 本だけ回し方や置き場所を間違えると、ここで元の木とずれる。
            var chunkVertices = new List<Vector3[]>();
            var chunkNormals = new List<Vector3[]>();
            foreach (GameObject chunk in _combiner.Chunks)
            {
                Mesh mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 localToWorld = chunk.transform.localToWorldMatrix;
                chunkVertices.Add(WorldPoints(mesh.vertices, localToWorld));
                chunkNormals.Add(WorldNormals(mesh.normals, localToWorld));
            }

            var cursors = new int[chunkVertices.Count];
            var missing = new List<string>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                bool found = false;
                for (int c = 0; c < chunkVertices.Count && !found; c++)
                {
                    if (Matches(chunkVertices[c], chunkNormals[c], cursors[c], _bodyVertices[i], _bodyNormals[i]))
                    {
                        cursors[c] += _bodyVertices[i].Length;
                        found = true;
                    }
                }

                if (!found)
                {
                    missing.Add(_bodies[i].transform.parent.name + " " + _bodyBounds[i].center);
                }
            }

            Assert.IsEmpty(missing, missing.Count + " 本が元の位置と向きのまままとまっていない:\n"
                + string.Join("\n", missing.GetRange(0, Mathf.Min(missing.Count, 10))));
            for (int c = 0; c < chunkVertices.Count; c++)
            {
                Assert.AreEqual(chunkVertices[c].Length, cursors[c], _combiner.Chunks[c].name + " に元の木に無い頂点がある");
            }
        }

        [Test]
        public void どの木もまとめたメッシュの範囲に収まる()
        {
            var bounds = new List<Bounds>();
            foreach (GameObject chunk in _combiner.Chunks)
            {
                bounds.Add(chunk.GetComponent<MeshRenderer>().bounds);
            }

            var outside = new List<string>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                Bounds tree = _bodyBounds[i];
                tree.Expand(-0.02f);
                if (!bounds.Exists(b => b.Contains(tree.min) && b.Contains(tree.max)))
                {
                    outside.Add(_bodies[i].transform.parent.name + " " + _bodyBounds[i]);
                }
            }

            Assert.IsEmpty(outside, outside.Count + " 本がどのまとめたメッシュにも入っていない（位置がずれた）:\n"
                + string.Join("\n", outside.GetRange(0, Mathf.Min(outside.Count, 10))));
        }

        [Test]
        public void まとめても面の表と裏が入れ替わらない()
        {
            // 鏡に映した向きで置かれた木を、三角形の巡る向きを戻さずにまとめると、面が裏返って消える。
            // 「三角形の巡る向きから出した法線が頂点の法線と同じ側を向く割合」が、まとめる前と変わらないことを見る。
            var facing = new FacingCount();
            foreach (GameObject chunk in _combiner.Chunks)
            {
                facing.Add(chunk.GetComponent<MeshFilter>().sharedMesh);
            }

            Assert.Greater(facing.Total, 0, "三角形が無い");
            Assert.AreEqual(_sourceFacingRatio, facing.Ratio, 0.01f, "まとめる前と比べて面の向きが変わった");
        }

        /// <summary>まとめたメッシュの start からの頂点が、木の頂点と位置 1 mm・法線 2.5° 以内で一致するか。</summary>
        private static bool Matches(Vector3[] chunkVertices, Vector3[] chunkNormals, int start, Vector3[] vertices, Vector3[] normals)
        {
            if (start + vertices.Length > chunkVertices.Length)
            {
                return false;
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if ((chunkVertices[start + i] - vertices[i]).sqrMagnitude > 1e-6f)
                {
                    return false;
                }

                if (i < normals.Length && start + i < chunkNormals.Length
                    && Vector3.Dot(chunkNormals[start + i], normals[i]) < 0.999f)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector3[] WorldPoints(Vector3[] points, Matrix4x4 localToWorld)
        {
            var world = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                world[i] = localToWorld.MultiplyPoint3x4(points[i]);
            }

            return world;
        }

        private static Vector3[] WorldNormals(Vector3[] normals, Matrix4x4 localToWorld)
        {
            Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
            var world = new Vector3[normals.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                world[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }

            return world;
        }

        private static void AddTrianglesByMaterial(Dictionary<Material, int> counts, Renderer renderer, Mesh mesh)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                counts.TryGetValue(materials[s], out int count);
                counts[materials[s]] = count + (int)mesh.GetIndexCount(s) / 3;
            }
        }

        /// <summary>
        /// 頂点を置いた先のワールドの範囲。renderer.bounds は回した箱の外接箱なので、回した木では頂点より大きくなる。
        /// </summary>
        private static Bounds WorldBounds(Mesh mesh, Matrix4x4 localToWorld)
        {
            Vector3[] vertices = mesh.vertices;
            var bounds = new Bounds(localToWorld.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 v in vertices)
            {
                bounds.Encapsulate(localToWorld.MultiplyPoint3x4(v));
            }

            return bounds;
        }

        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                count += (int)mesh.GetIndexCount(s) / 3;
            }

            return count;
        }

        /// <summary>三角形の巡る向きから出した法線が、頂点の法線と同じ側を向く割合を数える。</summary>
        private sealed class FacingCount
        {
            private int _agree;

            public int Total { get; private set; }

            public float Ratio => Total > 0 ? (float)_agree / Total : 0f;

            public void Add(Mesh mesh)
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                if (normals.Length != vertices.Length)
                {
                    return;
                }

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] triangles = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i];
                        int b = triangles[i + 1];
                        int c = triangles[i + 2];
                        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        if (face.sqrMagnitude < 1e-12f)
                        {
                            continue;
                        }

                        Total++;
                        if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) > 0f)
                        {
                            _agree++;
                        }
                    }
                }
            }
        }
    }
}
