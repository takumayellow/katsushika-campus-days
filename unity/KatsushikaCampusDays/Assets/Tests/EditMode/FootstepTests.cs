using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>足音の床の分類と、当たった三角形からサブメッシュを引く計算（#28）。</summary>
    public sealed class FootstepTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            _meshes.Clear();
        }

        /// <summary>
        /// サブメッシュごとの三角形の数だけを決めて、その通りのメッシュをその場で組み立てる。
        /// 三角形 t は頂点 3t, 3t+1, 3t+2 を使うので、後でメッシュ自身のインデックスと突き合わせられる。
        /// FBX は読まない（屋内の FBX は作り直されるたびに三角形の数が変わるので、数を当てにしない）。
        /// </summary>
        private Mesh BuildMesh(IReadOnlyList<int> triangleCounts)
        {
            int total = 0;
            foreach (int count in triangleCounts)
            {
                total += count;
            }

            var vertices = new Vector3[total * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(i % 8, 0f, i / 8);
            }

            var mesh = new Mesh { name = "synthetic", subMeshCount = triangleCounts.Count };
            mesh.SetVertices(vertices);

            int next = 0;
            for (int s = 0; s < triangleCounts.Count; s++)
            {
                var indices = new int[triangleCounts[s] * 3];
                for (int k = 0; k < indices.Length; k++)
                {
                    indices[k] = next + k;
                }

                next += indices.Length;
                mesh.SetTriangles(indices, s, false);
            }

            _meshes.Add(mesh);
            return mesh;
        }

        [TestCase("floor_carpet_blue", "wall_kyoso")]
        [TestCase("floor_carpet_blue", "floor_library")]
        [TestCase("floor_carpet_grey", "floor_library")]
        [TestCase("floor_carpet_grey", "furn_library_01_entry")]   // 図書館の入口マット
        [TestCase("floor_carpet_grey", "wall_library")]            // 図書館 2F の回廊 (壁と同じメッシュ)
        [TestCase("floor_carpet_red", "furn_lecture_03_plate")]
        public void Classify_CarpetMaterial_IsCarpet(string material, string objectName)
        {
            Assert.AreEqual("carpet", FootstepEmitter.Classify(material, objectName));
        }

        [TestCase("floor_tile_grey", "floor_kyoso", "tile")]
        [TestCase("floor_tile_grey", "wall_library", "tile")]      // 図書館の階段の踏み板
        [TestCase("floor_tile_white", "floor_kyoso", "tile")]
        [TestCase("", "floor_kyoso", "tile")]
        [TestCase("lawn_green", "lawn_01", "grass")]
        [TestCase("floor_wood", "floor_gym", "wood")]
        [TestCase("concrete_light", "wall_kyoso", "concrete")]
        public void Classify_OtherFloors_Unchanged(string material, string objectName, string expected)
        {
            Assert.AreEqual(expected, FootstepEmitter.Classify(material, objectName));
        }

        [Test]
        public void SubMeshOfTriangle_WalksSubMeshesInOrder()
        {
            // サブメッシュ 0: 三角形 2 個, 1: 3 個, 2: 1 個
            int[] indexCounts = { 6, 9, 3 };

            Assert.AreEqual(0, FootstepEmitter.SubMeshOfTriangle(0, indexCounts));
            Assert.AreEqual(0, FootstepEmitter.SubMeshOfTriangle(1, indexCounts));
            Assert.AreEqual(1, FootstepEmitter.SubMeshOfTriangle(2, indexCounts));
            Assert.AreEqual(1, FootstepEmitter.SubMeshOfTriangle(4, indexCounts));
            Assert.AreEqual(2, FootstepEmitter.SubMeshOfTriangle(5, indexCounts));
        }

        [Test]
        public void SubMeshOfTriangle_SkipsEmptySubMesh()
        {
            int[] indexCounts = { 3, 0, 6 };

            Assert.AreEqual(0, FootstepEmitter.SubMeshOfTriangle(0, indexCounts));
            Assert.AreEqual(2, FootstepEmitter.SubMeshOfTriangle(1, indexCounts));
            Assert.AreEqual(2, FootstepEmitter.SubMeshOfTriangle(2, indexCounts));
        }

        [Test]
        public void SubMeshOfTriangle_OutOfRange_IsMinusOne()
        {
            int[] indexCounts = { 6, 9 };

            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(5, indexCounts));
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(-1, indexCounts));
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(0, null));
        }

        /// <summary>
        /// 組み立てたメッシュの全ての三角形について、引いたサブメッシュが
        /// メッシュ自身の持つインデックス（GetIndices）と一致すること。
        /// 期待値はテスト側で数えずメッシュから読むので、三角形の数を書かなくて済む。
        /// </summary>
        [Test]
        public void SubMeshOfTriangle_Mesh_MatchesMeshIndices()
        {
            int[] counts = { 4, 1, 6, 2, 1, 1, 3, 6, 1, 1, 2, 5, 2, 2, 3, 1, 4 };
            Mesh mesh = BuildMesh(counts);

            int total = 0;
            foreach (int count in counts)
            {
                total += count;
            }

            var indices = new int[counts.Length][];
            for (int s = 0; s < counts.Length; s++)
            {
                indices[s] = mesh.GetIndices(s);
            }

            for (int triangle = 0; triangle < total; triangle++)
            {
                int subMesh = FootstepEmitter.SubMeshOfTriangle(mesh, counts.Length, triangle);
                Assert.GreaterOrEqual(subMesh, 0, $"三角形 {triangle} のサブメッシュが決まらなかった");
                // 組み立て方から、三角形 t の 1 つ目の頂点は 3t。そのサブメッシュのインデックスに入っているはず。
                Assert.GreaterOrEqual(System.Array.IndexOf(indices[subMesh], triangle * 3), 0,
                    $"三角形 {triangle} はサブメッシュ {subMesh} のものではない");
            }

            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(mesh, counts.Length, total),
                "最後の三角形の次は範囲外");
        }

        /// <summary>
        /// 図書館の壁のメッシュ (`wall_library`) のように、1 つのメッシュの後ろの方のサブメッシュに
        /// 床のマテリアル (2F 回廊のカーペット・階段の踏み板) が入っている場合。先頭のマテリアルは壁なので、
        /// 当たった三角形からサブメッシュを引かないと床を取り違える
        /// (#28 で図書館じゅうが tile の足音になっていた原因)。
        ///
        /// マテリアルの並びだけを実物に似せた作り物で、三角形の数は library.fbx とは関係ない。
        /// 実物の数は blender/kcd_interior/plan_library.py を変えるたびに変わるので、ここには書かない。
        /// </summary>
        [Test]
        public void SurfaceOfTriangle_FloorInLateSubMesh_IsNotMistakenForWall()
        {
            string[] materials =
            {
                "wall_white", "glass_clear", "metal_white", "metal_gray", "sign_exit_green",
                "ceiling_white", "ceiling_grid", "light_panel", "desk_wood", "fabric_beige",
                "paper_white", "light_strip", "concrete_light", "concrete_grey",
                "floor_carpet_grey", "glass_partition", "floor_tile_grey",
            };
            int[] counts = { 4, 1, 6, 2, 1, 1, 3, 6, 1, 1, 2, 5, 2, 2, 3, 1, 4 };
            Assert.AreEqual(materials.Length, counts.Length, "マテリアルとサブメッシュは 1 対 1");

            Mesh mesh = BuildMesh(counts);
            int carpet = System.Array.IndexOf(materials, "floor_carpet_grey");
            int tile = System.Array.IndexOf(materials, "floor_tile_grey");

            // 各サブメッシュの最初と最後の三角形の通し番号をメッシュの並びから数える
            var first = new int[counts.Length];
            int running = 0;
            for (int s = 0; s < counts.Length; s++)
            {
                first[s] = running;
                running += counts[s];
            }

            foreach (int triangle in new[] { first[carpet], first[carpet] + counts[carpet] - 1 })
            {
                Assert.AreEqual(carpet, FootstepEmitter.SubMeshOfTriangle(mesh, materials.Length, triangle));
                Assert.AreEqual("carpet",
                    FootstepEmitter.SurfaceOfTriangle(mesh, materials, triangle, "wall_library"));
            }

            foreach (int triangle in new[] { first[tile], first[tile] + counts[tile] - 1 })
            {
                Assert.AreEqual(tile, FootstepEmitter.SubMeshOfTriangle(mesh, materials.Length, triangle));
                Assert.AreEqual("tile",
                    FootstepEmitter.SurfaceOfTriangle(mesh, materials, triangle, "wall_library"));
            }

            // 先頭のマテリアルで代えると、2F 回廊でも階段でも壁と同じ concrete になってしまう
            Assert.AreEqual("concrete", FootstepEmitter.Classify(materials[0], "wall_library"));
        }

        /// <summary>
        /// 葉を落とした当たり判定メッシュ (#30) はサブメッシュが 1 つにまとめられていて
        /// マテリアルの数と合わない。この場合はサブメッシュを引かず、先頭のマテリアルで代える。
        /// </summary>
        [Test]
        public void SubMeshOfTriangle_StrippedCollider_FallsBackToFirstMaterial()
        {
            string[] materials = { "bark_brown", "plant_green", "leaf_autumn" };
            Mesh stripped = BuildMesh(new[] { 12 });   // マテリアルは 3 つだがサブメッシュは 1 つ

            Assert.AreEqual(1, stripped.subMeshCount);
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(stripped, materials.Length, 0));
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(stripped, materials.Length, 11));
            // 先頭の bark_brown で代えるので concrete
            Assert.AreEqual("concrete", FootstepEmitter.SurfaceOfTriangle(stripped, materials, 5, "tree_01"));
        }

        [Test]
        public void SubMeshOfTriangle_NoMeshOrSingleMaterial_IsMinusOne()
        {
            Mesh mesh = BuildMesh(new[] { 2, 3 });

            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(null, 2, 0), "メッシュでない当たり判定");
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(mesh, 1, 0), "マテリアルが 1 つ");
            Assert.AreEqual(-1, FootstepEmitter.SubMeshOfTriangle(mesh, 2, -1), "当たった三角形が不明");
        }

        /// <summary>サブメッシュが決まらなくても、先頭のマテリアル名で床を分類して鳴らす。</summary>
        [Test]
        public void SurfaceOfTriangle_WithoutMesh_UsesFirstMaterial()
        {
            string[] materials = { "floor_carpet_blue", "wall_white" };

            Assert.AreEqual("carpet", FootstepEmitter.SurfaceOfTriangle(null, materials, -1, "floor_library"));
            Assert.AreEqual("tile", FootstepEmitter.SurfaceOfTriangle(null, null, -1, "floor_kyoso"));
            Assert.AreEqual("concrete", FootstepEmitter.SurfaceOfTriangle(null, null, -1, "wall_kyoso"));
        }

        [Test]
        public void FootstepClipId_MatchesGeneratedFileNames()
        {
            Assert.AreEqual("step_carpet_1", AudioManager.FootstepClipId("carpet", 1));
            Assert.AreEqual("step_tile_4", AudioManager.FootstepClipId("tile", 4));
        }
    }
}
