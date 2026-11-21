using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// NavMesh の無い所（建物の中・西の回廊）で歩ける道を探す。<see cref="PlaythroughDirector"/> だけが使う。
    ///
    /// 建物の中と回廊は、モブが入り込まないようにわざと NavMesh を焼いていない。メッシュは Read/Write を
    /// 切ってあるので、実行中に NavMesh を焼き直すこともできない。そこで当たり判定を格子状に調べ、
    /// 歩ける升だけを A* でつなぐ。
    ///
    /// 1 本の柱に床が何枚も重なることがある（図書館の 1 階と吹き抜けの 2 階、階段の下と上）。升は
    /// 柱 (x, z) と、その柱の下から数えた床の番号（層）で決まる。隣の柱へは、段差が越えられる層どうしだけがつながる。
    ///
    /// 升を調べるのが <see cref="Sample"/>（Physics を使う）、道を探すのが <see cref="Solve"/>（純関数）。
    /// Solve は Physics に触れないので EditMode テストで確かめられる。
    /// </summary>
    public static class GridPath
    {
        /// <summary>歩けない升（床が無い・体が何かに当たる）の高さ。</summary>
        public const float Blocked = float.NaN;

        /// <summary>1 本の柱に持つ床の数の上限。</summary>
        public const int MaxLayers = 4;

        /// <summary>from と goal を囲む矩形の外へ足す既定の幅（m）。</summary>
        public const float DefaultMargin = 14f;

        /// <summary>
        /// 格子。Heights[x + z * Width + layer * Width * Depth] は、柱 (x, z) の下から layer 枚目の床の高さ。
        /// 歩けない升・床の無い層は NaN。層の数は Heights の長さから決まる（1 層なら Width * Depth）。
        /// 升は、この添字（node）で表す。
        /// </summary>
        public struct Grid
        {
            public Vector2 Origin;
            public float Cell;
            public int Width;
            public int Depth;
            public float[] Heights;
            public float MaxStep;

            public int Layers => Heights.Length / (Width * Depth);

            public int Node(int x, int z, int layer) => x + z * Width + layer * Width * Depth;

            public int X(int node) => node % Width;

            public int Z(int node) => node / Width % Depth;

            public bool Inside(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

            public bool Walkable(int node) => !float.IsNaN(Heights[node]);

            /// <summary>柱 (x, z) のどれかの層が歩けるか。</summary>
            public bool Walkable(int x, int z)
            {
                if (!Inside(x, z))
                {
                    return false;
                }

                for (int layer = 0; layer < Layers; layer++)
                {
                    if (Walkable(Node(x, z, layer)))
                    {
                        return true;
                    }
                }

                return false;
            }

            public Vector2Int CellOf(Vector3 world)
            {
                return new Vector2Int(Mathf.FloorToInt((world.x - Origin.x) / Cell), Mathf.FloorToInt((world.z - Origin.y) / Cell));
            }

            public Vector3 World(int node)
            {
                return new Vector3(Origin.x + (X(node) + 0.5f) * Cell, Heights[node], Origin.y + (Z(node) + 0.5f) * Cell);
            }

            /// <summary>
            /// 柱 (x, z) の層のうち、高さ height から段差を越えて乗れて、高さが一番近いもの。無ければ -1。
            /// </summary>
            public int StepTo(float height, int x, int z)
            {
                if (!Inside(x, z))
                {
                    return -1;
                }

                int best = -1;
                float bestGap = MaxStep + StepSlack;
                for (int layer = 0; layer < Layers; layer++)
                {
                    int node = Node(x, z, layer);
                    float gap = Mathf.Abs(Heights[node] - height);
                    if (gap <= bestGap)
                    {
                        best = node;
                        bestGap = gap;
                    }
                }

                return best;
            }
        }

        /// <summary>段差の比較のゆとり（m）。0.4 m ちょうどの段が浮動小数の誤差で越えられなくならないように。</summary>
        private const float StepSlack = 0.001f;

        /// <summary>段差（maxStep）より高い物を体の足元で探すときの、段差からのゆとり（m）。</summary>
        private const float StepClearance = 0.01f;

        /// <summary>足元の板の半幅を体の半径の何倍にするか。四角の角が体の円から大きくはみ出さないように少し狭める。</summary>
        private const float FootSlabWidth = 0.85f;

        /// <summary>当たった点からこれだけ下げて撃ち直す（m）。床の厚みより薄く、同じ面に当たり直さない。</summary>
        private const float RecastGap = 0.01f;

        private static readonly RaycastHit[] Hits = new RaycastHit[24];

        private static readonly List<float> Floors = new List<float>();

        /// <summary>
        /// from と goal を囲む範囲の升を調べる。床は真上から当てた線のうち、上を向いた面で、頭の上が
        /// body の高さぶん空いているもの。柱ごとに出発点か目的地の高さに近い <see cref="MaxLayers"/> 枚まで、
        /// 下から順に持つ。体（カプセル）が何かに当たる升は歩けない。
        /// 範囲は from と goal を囲む矩形に、minMargin m（遠いときは距離の 35%）を足したもの。
        /// 別の階へ上がる階段が離れた所にあるときは、minMargin を広げて階段まで入れる。
        /// </summary>
        public static Grid Sample(Vector3 from, Vector3 goal, float radius, float height, float maxStep, int mask,
            float minMargin = DefaultMargin)
        {
            float span = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(goal.x, goal.z));
            float cell = Mathf.Clamp(span / 200f, 0.4f, 1.2f);
            float margin = Mathf.Max(minMargin, span * 0.35f);
            float minX = Mathf.Min(from.x, goal.x) - margin;
            float minZ = Mathf.Min(from.z, goal.z) - margin;
            int width = Mathf.CeilToInt((Mathf.Max(from.x, goal.x) + margin - minX) / cell);
            int depth = Mathf.CeilToInt((Mathf.Max(from.z, goal.z) + margin - minZ) / cell);
            float top = Mathf.Max(from.y, goal.y) + 6f;
            float length = top - (Mathf.Min(from.y, goal.y) - 6f);

            var grid = new Grid
            {
                Origin = new Vector2(minX, minZ),
                Cell = cell,
                Width = width,
                Depth = depth,
                Heights = new float[width * depth * MaxLayers],
                MaxStep = Mathf.Max(maxStep, cell * 0.6f),
            };

            // 体の下の球は段差ぶん浮かせる。床そのものや小さな段には当てない。
            float lift = radius + maxStep + 0.05f;

            // 球の下は丸いので、段差より少し高いだけの細い縁（花壇の縁石など）には体の中心の近くでしか当たらない。
            // 縁を見落とすと、縁の外の地面から縁の内側の土へ段差 1 つで上がれる扱いになる。
            // 段差のすぐ上から球の中心までを、平たい箱（足元の板）でも調べる。
            float slabBottom = maxStep + StepClearance;
            float slabHalf = (lift - slabBottom) * 0.5f;
            var slabExtents = new Vector3(radius * FootSlabWidth, slabHalf, radius * FootSlabWidth);
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    var origin = new Vector3(minX + (x + 0.5f) * cell, top, minZ + (z + 0.5f) * cell);
                    CollectFloors(origin, length, height, mask, from.y, goal.y);
                    for (int layer = 0; layer < MaxLayers; layer++)
                    {
                        float floor = layer < Floors.Count ? Floors[layer] : Blocked;
                        if (!float.IsNaN(floor))
                        {
                            Vector3 foot = new Vector3(origin.x, floor, origin.z);
                            if (Physics.CheckCapsule(foot + Vector3.up * lift, foot + Vector3.up * Mathf.Max(lift, height - radius),
                                    radius, mask, QueryTriggerInteraction.Ignore)
                                || Physics.CheckBox(foot + Vector3.up * (slabBottom + slabHalf), slabExtents, Quaternion.identity,
                                    mask, QueryTriggerInteraction.Ignore))
                            {
                                floor = Blocked;
                            }
                        }

                        grid.Heights[grid.Node(x, z, layer)] = floor;
                    }
                }
            }

            return grid;
        }

        /// <summary>
        /// 柱の床を下から順に <see cref="Floors"/> へ入れる。頭がつかえる床（机の下の床など）は入れない。
        /// 1 つのコライダーが上下に何枚も床を持つこと（2 階建ての建物の床が 1 つのメッシュ）があり、
        /// RaycastNonAlloc はコライダーごとに 1 回しか当たらないので、当たった点の少し下から撃ち直す。
        /// 床が <see cref="MaxLayers"/> 枚より多ければ、nearA か nearB の高さに近いものを残す（何階もある建物の
        /// 上の階で、下の階の床だけが残って今いる階や目的の階が抜けないように）。
        /// </summary>
        private static void CollectFloors(Vector3 origin, float length, float height, int mask, float nearA, float nearB)
        {
            Floors.Clear();
            int count = 0;
            Vector3 from = origin;
            float left = length;
            while (count < Hits.Length && left > 0f
                && Physics.Raycast(from, Vector3.down, out RaycastHit hit, left, mask, QueryTriggerInteraction.Ignore))
            {
                Hits[count++] = hit;
                float next = origin.y - hit.point.y + RecastGap;
                from = new Vector3(origin.x, origin.y - next, origin.z);
                left = length - next;
            }

            for (int i = 0; i < count; i++)
            {
                if (Hits[i].normal.y < 0.7f)
                {
                    continue;
                }

                float y = Hits[i].point.y;
                bool roof = false;
                for (int j = 0; j < count && !roof; j++)
                {
                    float above = Hits[j].point.y - y;
                    roof = j != i && above > 0.05f && above < height;
                }

                if (!roof)
                {
                    Floors.Add(y);
                }
            }

            Floors.Sort();
            for (int i = Floors.Count - 1; i > 0; i--)
            {
                if (Floors[i] - Floors[i - 1] < 0.05f)
                {
                    Floors.RemoveAt(i);
                }
            }

            while (Floors.Count > MaxLayers)
            {
                int farthest = 0;
                for (int i = 1; i < Floors.Count; i++)
                {
                    if (FloorGapTo(Floors[i], nearA, nearB) > FloorGapTo(Floors[farthest], nearA, nearB))
                    {
                        farthest = i;
                    }
                }

                Floors.RemoveAt(farthest);
            }
        }

        private static float FloorGapTo(float y, float nearA, float nearB)
        {
            return Mathf.Min(Mathf.Abs(y - nearA), Mathf.Abs(y - nearB));
        }

        /// <summary>
        /// from から goal まで歩ける升をたどる道。曲がり角だけを返す（from は含まず、最後は goal に一番近い歩ける升）。
        /// from と goal はそれぞれ snap m 以内の歩ける升に寄せる。道が無ければ false。
        /// </summary>
        public static bool Solve(Grid grid, Vector3 from, Vector3 goal, float snap, out Vector3[] corners)
        {
            return Solve(grid, from, goal, snap, snap, out corners);
        }

        /// <summary>
        /// <see cref="Solve(Grid, Vector3, Vector3, float, out Vector3[])"/> の、from を寄せる距離（startSnap）と
        /// goal を寄せる距離（goalSnap）を分けたもの。机と椅子に囲まれた所から遠くの升へ寄せると、
        /// 家具の向こうから道を引いてしまう。from は近くにだけ寄せる。
        /// </summary>
        public static bool Solve(Grid grid, Vector3 from, Vector3 goal, float startSnap, float goalSnap, out Vector3[] corners)
        {
            return Solve(grid, from, goal, startSnap, goalSnap, null, out corners);
        }

        /// <summary>
        /// <see cref="Solve(Grid, Vector3, Vector3, float, float, out Vector3[])"/> の、from を寄せてよい升を
        /// startSeen で絞るもの（升の中心を渡して true なら寄せてよい。null なら絞らない）。壁と机の隅に押し付けられて
        /// 自分の側の升がどれも塞がっていると、一番近い升は壁の向こうになり、壁を突き抜ける道を引いてしまう。
        /// </summary>
        public static bool Solve(Grid grid, Vector3 from, Vector3 goal, float startSnap, float goalSnap,
            Func<Vector3, bool> startSeen, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            if (!TryNearest(grid, from, startSnap, startSeen, out int start) || !TryNearest(grid, goal, goalSnap, out int end))
            {
                return false;
            }

            List<int> nodes = AStar(grid, start, ref end, goalSnap / grid.Cell);
            if (nodes == null || nodes.Count <= 1)
            {
                // 1 升だけなら今いる升より先へ進めない。
                return false;
            }

            var result = new List<Vector3>();
            int anchor = 0;
            for (int i = 2; i < nodes.Count; i++)
            {
                if (!Visible(grid, nodes[anchor], nodes[i]))
                {
                    anchor = i - 1;
                    result.Add(grid.World(nodes[anchor]));
                }
            }

            result.Add(grid.World(end));
            corners = result.ToArray();
            return true;
        }

        /// <summary>
        /// world に一番近い歩ける升（横に radius m 以内）。高さは、world より下の床を上の床より選びやすくする
        /// （目的地の印は床から浮いているので、印のすぐ下の床がその階）。
        /// </summary>
        public static bool TryNearest(Grid grid, Vector3 world, float radius, out int found)
        {
            return TryNearest(grid, world, radius, null, out found);
        }

        /// <summary>
        /// <see cref="TryNearest(Grid, Vector3, float, out int)"/> の、seen が true を返す升（升の中心で問う）を先に選ぶもの。
        /// そういう升が無ければ seen を見ずに一番近い升を返す。seen が null なら絞らない。
        /// </summary>
        public static bool TryNearest(Grid grid, Vector3 world, float radius, Func<Vector3, bool> seen, out int found)
        {
            if (seen != null && TryNearestCore(grid, world, radius, seen, out found))
            {
                return true;
            }

            return TryNearestCore(grid, world, radius, null, out found);
        }

        private static bool TryNearestCore(Grid grid, Vector3 world, float radius, Func<Vector3, bool> seen, out int found)
        {
            Vector2Int center = grid.CellOf(world);
            int reach = Mathf.CeilToInt(radius / grid.Cell);
            found = -1;
            float best = float.MaxValue;
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = center.x + dx;
                    int z = center.y + dz;
                    if (dx * dx + dz * dz > reach * reach || !grid.Inside(x, z))
                    {
                        continue;
                    }

                    for (int layer = 0; layer < grid.Layers; layer++)
                    {
                        int node = grid.Node(x, z, layer);
                        if (!grid.Walkable(node))
                        {
                            continue;
                        }

                        float rise = grid.Heights[node] - world.y;
                        float vertical = rise <= 0.3f ? -rise : rise * 4f;
                        float flat = (dx * dx + dz * dz) * grid.Cell * grid.Cell;
                        float d = flat + vertical * vertical;
                        if (d < best && (seen == null || seen(grid.World(node))))
                        {
                            best = d;
                            found = node;
                        }
                    }
                }
            }

            return found >= 0;
        }

        /// <summary>
        /// start から end への升の列。end に届かない（台の向こう側など、囲まれた所に寄せた）ときは、
        /// 届いた升のうち end に一番近いもの（near 升以内）までの列を返し、end をその升に書き換える。
        /// </summary>
        private static List<int> AStar(Grid grid, int start, ref int end, float near)
        {
            int count = grid.Heights.Length;
            var cost = new float[count];
            var parent = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                cost[i] = float.MaxValue;
                parent[i] = -1;
            }

            cost[start] = 0f;
            var open = new Heap();
            open.Push(start, Heuristic(grid, start, end));
            int closest = start;
            float closestLeft = Left(grid, start, end);
            while (open.Count > 0)
            {
                int node = open.Pop();
                if (closed[node])
                {
                    continue;
                }

                if (node == end)
                {
                    return Trace(parent, end);
                }

                closed[node] = true;
                float left = Left(grid, node, end);
                if (left < closestLeft)
                {
                    closestLeft = left;
                    closest = node;
                }

                int x = grid.X(node);
                int z = grid.Z(node);
                float h = grid.Heights[node];
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }

                        int next = grid.StepTo(h, x + dx, z + dz);
                        if (next < 0 || closed[next])
                        {
                            continue;
                        }

                        // 斜めは、両脇の升も歩けるときだけ（角をかすめて壁に当たらないように）。
                        if (dx != 0 && dz != 0 && (grid.StepTo(h, x + dx, z) < 0 || grid.StepTo(h, x, z + dz) < 0))
                        {
                            continue;
                        }

                        float g = cost[node] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        if (g < cost[next])
                        {
                            cost[next] = g;
                            parent[next] = node;
                            open.Push(next, g + Heuristic(grid, next, end));
                        }
                    }
                }
            }

            if (closestLeft > near)
            {
                return null;
            }

            end = closest;
            return Trace(parent, closest);
        }

        /// <summary>横の升数で測った下限（斜めは 1.414）。</summary>
        private static float Heuristic(Grid grid, int a, int b)
        {
            int dx = Mathf.Abs(grid.X(a) - grid.X(b));
            int dz = Mathf.Abs(grid.Z(a) - grid.Z(b));
            return Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz);
        }

        /// <summary>end までの残り（升数）。別の階の真下・真上を「近い」としないよう、高さの差も足す。</summary>
        private static float Left(Grid grid, int a, int b)
        {
            return Heuristic(grid, a, b) + Mathf.Abs(grid.Heights[a] - grid.Heights[b]) / grid.Cell;
        }

        private static List<int> Trace(int[] parent, int end)
        {
            var nodes = new List<int>();
            for (int i = end; i >= 0; i = parent[i])
            {
                nodes.Add(i);
            }

            nodes.Reverse();
            return nodes;
        }

        /// <summary>角や区間を今の高さの道とみなす高さの差（m）。階をまたいで真上・真下の道を拾わない。</summary>
        public const float LevelGap = 1.5f;

        /// <summary>
        /// 道の途中にいるとき、次に向かう角。いちばん近い区間（角 i-1 → 角 i）の先の角 i を返す。
        /// 角 0 そのものがどの区間より近いときは 0（まだ道に乗っていない）。
        /// 今の高さから <see cref="LevelGap"/> より離れた区間（別の階の道）は数えない。
        /// </summary>
        public static int NextCorner(Vector3 at, Vector3[] corners)
        {
            if (corners == null || corners.Length == 0)
            {
                return 0;
            }

            Vector2 p = new Vector2(at.x, at.z);
            int best = 0;
            float bestDistance = OnLevel(at.y, corners[0], corners[0])
                ? Vector2.Distance(p, new Vector2(corners[0].x, corners[0].z))
                : float.PositiveInfinity;
            for (int i = 1; i < corners.Length; i++)
            {
                if (!OnLevel(at.y, corners[i - 1], corners[i]))
                {
                    continue;
                }

                float d = DistanceToSegment(p, corners[i - 1], corners[i]);
                if (d < bestDistance - 0.01f)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 道（角を順に結んだ折れ線）から within m 以内にいるか。角が 1 つなら、その角から within m 以内か。
        /// 別の階の区間は数えない（<see cref="NextCorner"/> と同じ）。
        /// </summary>
        public static bool NearPath(Vector3 at, Vector3[] corners, float within)
        {
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            Vector2 p = new Vector2(at.x, at.z);
            if (OnLevel(at.y, corners[0], corners[0]) && Vector2.Distance(p, new Vector2(corners[0].x, corners[0].z)) < within)
            {
                return true;
            }

            for (int i = 1; i < corners.Length; i++)
            {
                if (OnLevel(at.y, corners[i - 1], corners[i]) && DistanceToSegment(p, corners[i - 1], corners[i]) < within)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>高さ y が、区間 from → to の高さの幅から <see cref="LevelGap"/> 以内か（階段の区間は上り下りの全体）。</summary>
        private static bool OnLevel(float y, Vector3 from, Vector3 to)
        {
            return y >= Mathf.Min(from.y, to.y) - LevelGap && y <= Mathf.Max(from.y, to.y) + LevelGap;
        }

        private static float DistanceToSegment(Vector2 p, Vector3 from, Vector3 to)
        {
            Vector2 a = new Vector2(from.x, from.z);
            Vector2 ab = new Vector2(to.x, to.z) - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(a + ab * t, p);
        }

        /// <summary>
        /// 升 a から升 b へまっすぐ歩けるか。線を升の半分ずつたどり、今いる床から段差を越えて乗れる層が
        /// 線の上にも両脇にもあるか見る（坂や階段は、たどりながら高さを上げ下げする）。
        /// </summary>
        public static bool Visible(Grid grid, int a, int b)
        {
            Vector2 from = new Vector2(grid.X(a) + 0.5f, grid.Z(a) + 0.5f);
            Vector2 to = new Vector2(grid.X(b) + 0.5f, grid.Z(b) + 0.5f);
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 2f);
            // 体の幅ぶん、線の両脇も見る。
            Vector2 side = new Vector2(-(to - from).y, (to - from).x).normalized * 0.45f;
            float height = grid.Heights[a];
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                int here = grid.StepTo(height, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                if (here < 0)
                {
                    return false;
                }

                height = grid.Heights[here];
                foreach (Vector2 q in new[] { p + side, p - side })
                {
                    if (grid.StepTo(height, Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y)) < 0)
                    {
                        return false;
                    }
                }
            }

            return Mathf.Abs(height - grid.Heights[b]) <= grid.MaxStep + StepSlack;
        }

        /// <summary>A* の開いた升の山（小さい順）。</summary>
        private sealed class Heap
        {
            private readonly List<(int index, float priority)> _items = new List<(int, float)>();

            public int Count => _items.Count;

            public void Push(int index, float priority)
            {
                _items.Add((index, priority));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) / 2;
                    if (_items[up].priority <= _items[i].priority)
                    {
                        break;
                    }

                    (_items[up], _items[i]) = (_items[i], _items[up]);
                    i = up;
                }
            }

            public int Pop()
            {
                int top = _items[0].index;
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < _items.Count && _items[left].priority < _items[smallest].priority)
                    {
                        smallest = left;
                    }

                    if (right < _items.Count && _items[right].priority < _items[smallest].priority)
                    {
                        smallest = right;
                    }

                    if (smallest == i)
                    {
                        break;
                    }

                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }

                return top;
            }
        }
    }
}
