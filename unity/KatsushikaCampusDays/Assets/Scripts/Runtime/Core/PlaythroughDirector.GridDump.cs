using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 通しプレイの格子の書き出し。道が見つからないとき・ワープしたときに、直前に調べた格子を
    /// 真上から見た PGM 画像にして persistentDataPath/playthrough-grids に置く（詰まった場所を後から見るため）。
    /// 黒は歩けない升、灰色は歩ける升（明るいほど高い。出発点から段差で乗れない高さの床も描く）、白は出発点、中間の灰色の十字は目的地。
    /// </summary>
    public sealed partial class PlaythroughDirector
    {
        /// <summary>1 回の通しで書き出す枚数の上限。</summary>
        private const int GridDumpLimit = 60;

        private GridPath.Grid _lastGrid;
        private Vector3 _lastGridStart;
        private Vector3 _lastGridGoal;
        private bool _hasLastGrid;
        private int _gridDumps;

        /// <summary>道が無い格子を書き出した目的（同じ目的では 1 枚だけ）。</summary>
        private readonly HashSet<string> _noneDumped = new HashSet<string>();

        private void RememberGrid(GridPath.Grid grid, Vector3 start, Vector3 goal)
        {
            _lastGrid = grid;
            _lastGridStart = start;
            _lastGridGoal = goal;
            _hasLastGrid = true;
        }

        private void DumpLastGrid(string why)
        {
            if (!_hasLastGrid || _gridDumps >= GridDumpLimit)
            {
                return;
            }

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "playthrough-grids");
                Directory.CreateDirectory(dir);
                string name = string.Format(CultureInfo.InvariantCulture, "{0:D2}-{1}-t{2:F0}.pgm", _gridDumps, why, _clock);
                string path = Path.Combine(dir, name);
                File.WriteAllBytes(path, ToPgm(_lastGrid, _lastGridStart, _lastGridGoal));
                _gridDumps++;
                Debug.Log("[KCD] playthrough: 格子を書き出した " + name + " " + _lastGoalKey
                    + " 原点 (" + _lastGrid.Origin.x.ToString("F1", CultureInfo.InvariantCulture)
                    + "," + _lastGrid.Origin.y.ToString("F1", CultureInfo.InvariantCulture) + ")");
            }
            catch (IOException e)
            {
                Debug.LogWarning("[KCD] playthrough: 格子を書き出せない " + e.Message);
            }
        }

        /// <summary>出発点の高さから段差で乗れる層を優先して、柱ごとに 1 画素にする（上が北 = z の大きい方）。</summary>
        private static byte[] ToPgm(GridPath.Grid grid, Vector3 start, Vector3 goal)
        {
            int w = grid.Width;
            int d = grid.Depth;
            var pixels = new byte[w * d];
            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    int node = grid.StepTo(start.y, x, z);
                    if (node < 0 || !grid.Walkable(node))
                    {
                        // 段差では乗れない高さの床（階段教室の段、別の階）も、出発点の高さに近いものを描く。
                        node = NearestLayer(grid, start.y, x, z);
                    }

                    byte value = 0;
                    if (node >= 0 && grid.Walkable(node))
                    {
                        float rise = Mathf.Clamp(grid.Heights[node] - start.y, -2f, 2f);
                        value = (byte)Mathf.Clamp(150 + rise * 40f, 60f, 230f);
                    }

                    pixels[x + (d - 1 - z) * w] = value;
                }
            }

            Mark(grid, pixels, start, 255, 1);
            Mark(grid, pixels, goal, 100, 3);
            byte[] header = Encoding.ASCII.GetBytes("P5\n" + w + " " + d + "\n255\n");
            var bytes = new byte[header.Length + pixels.Length];
            header.CopyTo(bytes, 0);
            pixels.CopyTo(bytes, header.Length);
            return bytes;
        }

        private static int NearestLayer(GridPath.Grid grid, float y, int x, int z)
        {
            int best = -1;
            float bestGap = float.MaxValue;
            for (int layer = 0; layer < grid.Layers; layer++)
            {
                int node = grid.Node(x, z, layer);
                if (grid.Walkable(node) && Mathf.Abs(grid.Heights[node] - y) < bestGap)
                {
                    best = node;
                    bestGap = Mathf.Abs(grid.Heights[node] - y);
                }
            }

            return best;
        }

        private static void Mark(GridPath.Grid grid, byte[] pixels, Vector3 at, byte value, int arm)
        {
            Vector2Int c = grid.CellOf(at);
            for (int i = -arm; i <= arm; i++)
            {
                Put(grid, pixels, c.x + i, c.y, value);
                Put(grid, pixels, c.x, c.y + i, value);
            }
        }

        private static void Put(GridPath.Grid grid, byte[] pixels, int x, int z, byte value)
        {
            if (grid.Inside(x, z))
            {
                pixels[x + (grid.Depth - 1 - z) * grid.Width] = value;
            }
        }
    }
}
