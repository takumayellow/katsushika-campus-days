using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 穴のあいた平らな地面。屋内の下に敷く地面（キャンパスの OuterGround・屋内の並びの OutsideGround・遠景のドームの扇）は
    /// 床が地面より下にある部屋（講義棟の 101 の段床）の上には張らない。張ると部屋の上に地面が浮いて見え、
    /// OuterGround は当たり判定もあるので段を下りられない。
    /// 長方形の外枠から長方形の穴を抜いた残りを、重ならない長方形のタイルに分ける（Rect の x は世界の x、y は世界の z）。
    /// </summary>
    public static class GroundTiles
    {
        /// <summary>これより短い辺のタイルは捨てる（m）。穴どうしの継ぎ目にできる細い隙間を残さない。</summary>
        public const float MinTileSize = 0.01f;

        /// <summary>
        /// 外枠から穴を抜いた残りのタイル。x を穴の端で帯に切り、帯ごとに z の残りを出し、
        /// となりの帯と z の切れ目が同じならつなぐ。穴は外枠からはみ出してもよく、重なっていてもよい。
        /// </summary>
        public static List<Rect> Subtract(Rect outer, IList<Rect> holes)
        {
            var tiles = new List<Rect>();
            if (outer.width < MinTileSize || outer.height < MinTileSize)
            {
                return tiles;
            }

            var cuts = new List<float> { outer.xMin, outer.xMax };
            if (holes != null)
            {
                foreach (Rect hole in holes)
                {
                    AddCut(cuts, hole.xMin, outer);
                    AddCut(cuts, hole.xMax, outer);
                }
            }

            cuts.Sort();
            List<Vector2> previous = null;
            int previousStart = -1;
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                float x0 = cuts[i];
                float x1 = cuts[i + 1];
                if (x1 - x0 < MinTileSize)
                {
                    continue;
                }

                List<Vector2> spans = FreeSpans(outer, holes, x0, x1);
                if (previous != null && SameSpans(previous, spans) && Touches(tiles, previousStart, x0))
                {
                    // となりの帯と同じ切れ目なので、前の帯のタイルを x1 まで伸ばす。
                    for (int k = previousStart; k < tiles.Count; k++)
                    {
                        Rect t = tiles[k];
                        tiles[k] = Rect.MinMaxRect(t.xMin, t.yMin, x1, t.yMax);
                    }

                    continue;
                }

                previousStart = tiles.Count;
                previous = spans;
                foreach (Vector2 span in spans)
                {
                    tiles.Add(Rect.MinMaxRect(x0, span.x, x1, span.y));
                }
            }

            return tiles;
        }

        /// <summary>
        /// タイルを上向きの四角形にしたメッシュ。頂点は <paramref name="origin"/> から見た位置（m）で、高さは y。
        /// UV は世界の x・z（m）そのままなので、芝の画像は Plane に UseMetricUv したときと同じ実寸で並ぶ。
        /// </summary>
        public static Mesh BuildMesh(IList<Rect> tiles, float y, Vector3 origin, string name)
        {
            var vertices = new List<Vector3>(tiles.Count * 4);
            var uvs = new List<Vector2>(tiles.Count * 4);
            var triangles = new List<int>(tiles.Count * 6);
            foreach (Rect tile in tiles)
            {
                int a = vertices.Count;
                AddCorner(vertices, uvs, tile.xMin, tile.yMin, y, origin);
                AddCorner(vertices, uvs, tile.xMin, tile.yMax, y, origin);
                AddCorner(vertices, uvs, tile.xMax, tile.yMax, y, origin);
                AddCorner(vertices, uvs, tile.xMax, tile.yMin, y, origin);
                // 上から見て時計回り（Unity の表）。
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a);
                triangles.Add(a + 2);
                triangles.Add(a + 3);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCorner(List<Vector3> vertices, List<Vector2> uvs, float x, float z, float y, Vector3 origin)
        {
            vertices.Add(new Vector3(x - origin.x, y, z - origin.z));
            uvs.Add(new Vector2(x, z));
        }

        private static void AddCut(List<float> cuts, float x, Rect outer)
        {
            if (x > outer.xMin && x < outer.xMax)
            {
                cuts.Add(x);
            }
        }

        /// <summary>帯 x0..x1 を丸ごと覆う穴を z の区間として抜いた残り（z の小さい順）。</summary>
        private static List<Vector2> FreeSpans(Rect outer, IList<Rect> holes, float x0, float x1)
        {
            var blocked = new List<Vector2>();
            if (holes != null)
            {
                foreach (Rect hole in holes)
                {
                    if (hole.xMin <= x0 && hole.xMax >= x1 && hole.yMax > outer.yMin && hole.yMin < outer.yMax)
                    {
                        blocked.Add(new Vector2(Mathf.Max(hole.yMin, outer.yMin), Mathf.Min(hole.yMax, outer.yMax)));
                    }
                }
            }

            blocked.Sort((p, q) => p.x.CompareTo(q.x));
            var spans = new List<Vector2>();
            float z = outer.yMin;
            foreach (Vector2 b in blocked)
            {
                if (b.x - z >= MinTileSize)
                {
                    spans.Add(new Vector2(z, b.x));
                }

                z = Mathf.Max(z, b.y);
            }

            if (outer.yMax - z >= MinTileSize)
            {
                spans.Add(new Vector2(z, outer.yMax));
            }

            return spans;
        }

        private static bool SameSpans(List<Vector2> a, List<Vector2> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!Mathf.Approximately(a[i].x, b[i].x) || !Mathf.Approximately(a[i].y, b[i].y))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>前の帯のタイルの右端が x0 に接しているか（間の帯が捨てられていたらつながない）。</summary>
        private static bool Touches(List<Rect> tiles, int start, float x0)
        {
            return start < tiles.Count ? Mathf.Abs(tiles[start].xMax - x0) < MinTileSize : true;
        }
    }
}
