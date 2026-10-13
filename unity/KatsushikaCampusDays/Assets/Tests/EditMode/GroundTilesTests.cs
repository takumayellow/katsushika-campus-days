using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>穴のあいた地面のタイル（床が地面より下にある部屋の上に外の地面を張らない）。</summary>
    public sealed class GroundTilesTests
    {
        private static readonly Rect Outer = Rect.MinMaxRect(-100f, -50f, 100f, 50f);

        [Test]
        public void 穴が無ければ外枠そのもの1枚()
        {
            List<Rect> tiles = GroundTiles.Subtract(Outer, new List<Rect>());
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(Outer, tiles[0]);
        }

        [Test]
        public void 真ん中の穴は4枚で囲む()
        {
            var holes = new List<Rect> { Rect.MinMaxRect(-10f, -5f, 20f, 15f) };
            List<Rect> tiles = GroundTiles.Subtract(Outer, holes);
            Assert.AreEqual(4, tiles.Count);
            AssertCovers(Outer, holes, tiles);
        }

        [Test]
        public void はみ出す穴と重なる穴と外の穴()
        {
            var holes = new List<Rect>
            {
                Rect.MinMaxRect(90f, -60f, 120f, 0f),     // 外枠の角からはみ出す
                Rect.MinMaxRect(-30f, -10f, 0f, 10f),
                Rect.MinMaxRect(-20f, 0f, 10f, 20f),     // ひとつ前と重なる
                Rect.MinMaxRect(300f, 300f, 310f, 310f), // 外枠の外
            };
            List<Rect> tiles = GroundTiles.Subtract(Outer, holes);
            AssertCovers(Outer, holes, tiles);
        }

        [Test]
        public void 外枠を横切る穴で2つに分かれる()
        {
            var holes = new List<Rect> { Rect.MinMaxRect(-10f, -80f, 10f, 80f) };
            List<Rect> tiles = GroundTiles.Subtract(Outer, holes);
            Assert.AreEqual(2, tiles.Count);
            AssertCovers(Outer, holes, tiles);
        }

        [Test]
        public void メッシュは上向きで世界のxzがUV()
        {
            var holes = new List<Rect> { Rect.MinMaxRect(-10f, -5f, 20f, 15f) };
            List<Rect> tiles = GroundTiles.Subtract(Outer, holes);
            var origin = new Vector3(5f, 0f, -3f);
            Mesh mesh = GroundTiles.BuildMesh(tiles, -0.05f, origin, "test");
            try
            {
                Assert.AreEqual(tiles.Count * 2, mesh.triangles.Length / 3);
                foreach (Vector3 normal in mesh.normals)
                {
                    Assert.Greater(normal.y, 0.99f, "地面が下を向いている");
                }

                Vector3[] vertices = mesh.vertices;
                Vector2[] uv = mesh.uv;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Assert.AreEqual(-0.05f, vertices[i].y, 1e-5f);
                    Assert.AreEqual(vertices[i].x + origin.x, uv[i].x, 1e-4f);
                    Assert.AreEqual(vertices[i].z + origin.z, uv[i].y, 1e-4f);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>タイルが重ならず、穴に掛からず、外枠の残りを全部覆う（格子点で確かめる）。</summary>
        private static void AssertCovers(Rect outer, List<Rect> holes, List<Rect> tiles)
        {
            for (float x = outer.xMin + 0.5f; x < outer.xMax; x += 1f)
            {
                for (float z = outer.yMin + 0.5f; z < outer.yMax; z += 1f)
                {
                    var p = new Vector2(x, z);
                    bool inHole = false;
                    foreach (Rect hole in holes)
                    {
                        inHole |= hole.Contains(p);
                    }

                    int covering = 0;
                    foreach (Rect tile in tiles)
                    {
                        if (tile.Contains(p))
                        {
                            covering++;
                        }
                    }

                    Assert.AreEqual(inHole ? 0 : 1, covering, "(" + x + ", " + z + ") を覆うタイルの数");
                }
            }
        }
    }
}
