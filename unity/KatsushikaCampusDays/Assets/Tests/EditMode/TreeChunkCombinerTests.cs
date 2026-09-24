using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// TreeChunkCombiner を小さな作り物のメッシュで確かめる (#57)。キャンパスの木には無い置き方
    /// （鏡に映した向き・同じマスに影の設定の違う木・16 ビットに収まらない頂点数）をここで守る。
    /// </summary>
    public sealed class TreeChunkCombinerTests
    {
        private readonly List<Object> _created = new List<Object>();
        private GameObject _root;

        [SetUp]
        public void CreateRoot()
        {
            _root = new GameObject("trees");
            _created.Add(_root);
        }

        [TearDown]
        public void DestroyCreated()
        {
            TreeChunkCombiner combiner = _root != null ? _root.GetComponent<TreeChunkCombiner>() : null;
            if (combiner != null)
            {
                foreach (GameObject chunk in combiner.Chunks)
                {
                    if (chunk != null)
                    {
                        Object.DestroyImmediate(chunk.GetComponent<MeshFilter>().sharedMesh);
                    }
                }
            }

            foreach (Object target in _created)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            _created.Clear();
        }

        [Test]
        public void 鏡に映した向きの木も表を向いたまままとまる()
        {
            Mesh triangle = Triangles(1);
            AddTree("plain", triangle, new Vector3(0f, 0f, 0f), Vector3.one);
            AddTree("mirrored", triangle, new Vector3(4f, 0f, 0f), new Vector3(-1f, 1f, 1f));

            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            Assert.AreEqual(1, combiner.Combine());

            Mesh merged = combiner.Chunks[0].GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = merged.vertices;
            Vector3[] normals = merged.normals;
            int[] indices = merged.GetTriangles(0);
            Assert.AreEqual(6, indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
                Assert.Greater(Vector3.Dot(face, normals[indices[i]]), 0f, "三角形 " + (i / 3) + " が裏を向いた");
            }

            // 鏡に映した木の 2 つ目の頂点 (1, 0, 0) は、x を反転して (4 - 1, 0, 0) に来る。
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, 0f), vertices[4]), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(Vector3.forward, normals[4]), Is.LessThan(1e-5f), "法線は反転した面でも前を向く");
        }

        [Test]
        public void 同じマスで影の設定が違う木は名前を分けてまとめる()
        {
            Mesh triangle = Triangles(1);
            AddTree("lit", triangle, Vector3.zero, Vector3.one);
            MeshRenderer unlit = AddTree("no_shadow", triangle, new Vector3(2f, 0f, 0f), Vector3.one);
            unlit.shadowCastingMode = ShadowCastingMode.Off;

            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            Assert.AreEqual(2, combiner.Combine());
            Assert.AreNotEqual(combiner.Chunks[0].name, combiner.Chunks[1].name);
            Assert.AreEqual(ShadowCastingMode.Off, combiner.Chunks[1].GetComponent<MeshRenderer>().shadowCastingMode);
        }

        [Test]
        public void マテリアルの数が合わない木はまとめずに残して警告する()
        {
            MeshRenderer odd = AddTree("odd", Triangles(1), Vector3.zero, Vector3.one);
            odd.sharedMaterials = new Material[2];

            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            LogAssert.Expect(LogType.Warning, new Regex("TreeChunkCombiner: 1 個"));
            Assert.AreEqual(0, combiner.Combine());
            Assert.IsTrue(odd.enabled, "まとめなかった木は描き続ける");
        }

        [Test]
        public void 頂点が_16_ビットに収まらなければ_32_ビットの番号でつなぐ()
        {
            Mesh large = Triangles(12000);
            AddTree("a", large, Vector3.zero, Vector3.one);
            AddTree("b", large, new Vector3(2f, 0f, 0f), Vector3.one);

            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            Assert.AreEqual(1, combiner.Combine());

            Mesh merged = combiner.Chunks[0].GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(IndexFormat.UInt32, merged.indexFormat);
            Assert.AreEqual(72000, merged.vertexCount);
            int[] indices = merged.GetTriangles(0);
            Assert.AreEqual(71999, indices[indices.Length - 1], "2 本目の最後の三角形が 2 本目の頂点を指していない");
        }

        [Test]
        public void 二度呼んでも同じまとまりを返す()
        {
            AddTree("a", Triangles(1), Vector3.zero, Vector3.one);
            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            Assert.AreEqual(1, combiner.Combine());
            Assert.AreEqual(1, combiner.Combine());
            Assert.AreEqual(2, _root.transform.childCount, "まとめたメッシュが増えた（木 1 本とまとめたメッシュ 1 つのはず）");
        }

        [Test]
        public void まとめられる木が無くても二度目は警告を繰り返さない()
        {
            MeshRenderer odd = AddTree("odd", Triangles(1), Vector3.zero, Vector3.one);
            odd.sharedMaterials = new Material[2];

            TreeChunkCombiner combiner = _root.AddComponent<TreeChunkCombiner>();
            LogAssert.Expect(LogType.Warning, new Regex("TreeChunkCombiner: 1 個"));
            Assert.AreEqual(0, combiner.Combine());
            Assert.AreEqual(0, combiner.Combine());
            LogAssert.NoUnexpectedReceived();
        }

        private MeshRenderer AddTree(string name, Mesh mesh, Vector3 position, Vector3 scale)
        {
            var tree = new GameObject(name);
            tree.transform.SetParent(_root.transform, false);
            tree.transform.localPosition = position;
            tree.transform.localScale = scale;
            tree.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = tree.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new Material[1];
            return renderer;
        }

        /// <summary>xy 平面に置いた、前 (+z) を向く三角形を count 個並べたメッシュ。</summary>
        private Mesh Triangles(int count)
        {
            var vertices = new List<Vector3>(count * 3);
            var indices = new List<int>(count * 3);
            for (int i = 0; i < count; i++)
            {
                float z = i * 0.001f;
                vertices.Add(new Vector3(0f, 0f, z));
                vertices.Add(new Vector3(1f, 0f, z));
                vertices.Add(new Vector3(0f, 1f, z));
                indices.Add(i * 3);
                indices.Add(i * 3 + 1);
                indices.Add(i * 3 + 2);
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            var normals = new List<Vector3>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                normals.Add(Vector3.forward);
            }

            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            _created.Add(mesh);
            return mesh;
        }
    }
}
