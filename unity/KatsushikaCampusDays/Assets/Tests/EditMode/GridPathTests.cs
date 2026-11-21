using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 通しプレイの格子経路（<see cref="GridPath.Solve"/>）。当たり判定を調べる Sample は使わず、升を手で並べて確かめる。
    /// </summary>
    public sealed class GridPathTests
    {
        private static GridPath.Grid Open(int width, int depth)
        {
            var grid = new GridPath.Grid
            {
                Origin = Vector2.zero,
                Cell = 1f,
                Width = width,
                Depth = depth,
                Heights = new float[width * depth],
                MaxStep = 0.4f,
            };
            return grid;
        }

        private static int NodeAt(GridPath.Grid grid, Vector3 world)
        {
            Vector2Int cell = grid.CellOf(world);
            return grid.Node(cell.x, cell.y, 0);
        }

        /// <summary>
        /// 1 階（高さ 0）の上、x = 10..19 に 2 階（高さ 4）がかぶさる。階段は z = 18, 19 の x = 0..9 で、
        /// 1 段 0.4 m ずつ上がって x = 9 で 4 m になる。
        /// </summary>
        private static GridPath.Grid TwoFloors()
        {
            const int size = 20;
            var grid = new GridPath.Grid
            {
                Origin = Vector2.zero,
                Cell = 1f,
                Width = size,
                Depth = size,
                Heights = new float[size * size * 2],
                MaxStep = 0.4f,
            };
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool stair = z >= 18 && x < 10;
                    grid.Heights[grid.Node(x, z, 0)] = stair ? 0.4f * (x + 1) : 0f;
                    grid.Heights[grid.Node(x, z, 1)] = x >= 10 ? 4f : GridPath.Blocked;
                }
            }

            return grid;
        }

        private static void Block(GridPath.Grid grid, int x, int z)
        {
            grid.Heights[x + z * grid.Width] = GridPath.Blocked;
        }

        [Test]
        public void OpenGroundGoesStraightToTheGoal()
        {
            GridPath.Grid grid = Open(20, 20);
            Assert.IsTrue(GridPath.Solve(grid, new Vector3(1.5f, 0f, 1.5f), new Vector3(18.5f, 0f, 15.5f), 2f, out Vector3[] corners));
            Assert.AreEqual(1, corners.Length);
            Assert.AreEqual(18.5f, corners[0].x, 0.01f);
            Assert.AreEqual(15.5f, corners[0].z, 0.01f);
        }

        [Test]
        public void WallWithGapIsPassedThroughTheGap()
        {
            GridPath.Grid grid = Open(20, 20);
            for (int z = 0; z < 20; z++)
            {
                if (z != 16)
                {
                    Block(grid, 10, z);
                }
            }

            Assert.IsTrue(GridPath.Solve(grid, new Vector3(2.5f, 0f, 2.5f), new Vector3(17.5f, 0f, 2.5f), 2f, out Vector3[] corners));
            Assert.Greater(corners.Length, 1);

            // 角から角へまっすぐ歩いても壁に入らない。
            int previous = NodeAt(grid, new Vector3(2.5f, 0f, 2.5f));
            foreach (Vector3 corner in corners)
            {
                int next = NodeAt(grid, corner);
                Assert.IsTrue(GridPath.Visible(grid, previous, next), "角 " + previous + " → " + next + " が壁を抜ける");
                previous = next;
            }

            Assert.AreEqual(NodeAt(grid, new Vector3(17.5f, 0f, 2.5f)), previous);
        }

        [Test]
        public void ClosedWallHasNoPath()
        {
            GridPath.Grid grid = Open(20, 20);
            for (int z = 0; z < 20; z++)
            {
                Block(grid, 10, z);
            }

            Assert.IsFalse(GridPath.Solve(grid, new Vector3(2.5f, 0f, 2.5f), new Vector3(17.5f, 0f, 2.5f), 2f, out _));
        }

        [Test]
        public void StepHigherThanMaxStepIsAWall()
        {
            GridPath.Grid grid = Open(20, 20);
            for (int x = 10; x < 20; x++)
            {
                for (int z = 0; z < 20; z++)
                {
                    grid.Heights[x + z * grid.Width] = 1f;
                }
            }

            Assert.IsFalse(GridPath.Solve(grid, new Vector3(2.5f, 0f, 2.5f), new Vector3(17.5f, 1f, 2.5f), 2f, out _));
        }

        [Test]
        public void StartBoxedInByFurnitureIsNotSnappedFarAway()
        {
            // 出発点 (5, 5) の周り 3 升四方が塞がっている（椅子と机に挟まれた）。
            GridPath.Grid grid = Open(20, 20);
            for (int z = 3; z <= 7; z++)
            {
                for (int x = 3; x <= 7; x++)
                {
                    Block(grid, x, z);
                }
            }

            Vector3 from = new Vector3(5.5f, 0f, 5.5f);
            Vector3 goal = new Vector3(15.5f, 0f, 15.5f);
            Assert.IsFalse(GridPath.Solve(grid, from, goal, 1f, 4f, out _));
            Assert.IsTrue(GridPath.Solve(grid, from, goal, 4f, 4f, out _));
        }

        [Test]
        public void GoalIsSnappedFartherThanTheStart()
        {
            GridPath.Grid grid = Open(20, 20);
            for (int z = 13; z <= 17; z++)
            {
                for (int x = 13; x <= 17; x++)
                {
                    Block(grid, x, z);
                }
            }

            Assert.IsTrue(GridPath.Solve(grid, new Vector3(1.5f, 0f, 1.5f), new Vector3(15.5f, 0f, 15.5f), 1f, 4f, out Vector3[] corners));
            Vector3 last = corners[corners.Length - 1];
            Assert.Less(new Vector2(last.x - 15.5f, last.z - 15.5f).magnitude, 4f);
        }

        [Test]
        public void GoalInsideBlockedCellSnapsToTheNearestWalkableCell()
        {
            GridPath.Grid grid = Open(20, 20);
            Block(grid, 15, 15);
            Assert.IsTrue(GridPath.Solve(grid, new Vector3(1.5f, 0f, 1.5f), new Vector3(15.5f, 0f, 15.5f), 2f, out Vector3[] corners));
            Vector3 last = corners[corners.Length - 1];
            Assert.Less(new Vector2(last.x - 15.5f, last.z - 15.5f).magnitude, 1.5f);
        }

        [Test]
        public void GoalBehindACounterEndsAtTheNearestReachableCell()
        {
            // 台（x=10 の 1 列、z=5..15）の向こう側は囲まれていて入れない。手前の一番近い升まで行く。
            GridPath.Grid grid = Open(20, 20);
            for (int z = 5; z <= 15; z++)
            {
                Block(grid, 10, z);
                Block(grid, 12, z);
            }

            for (int x = 10; x <= 12; x++)
            {
                Block(grid, x, 5);
                Block(grid, x, 15);
            }

            Assert.IsTrue(GridPath.Solve(grid, new Vector3(2.5f, 0f, 10.5f), new Vector3(11.5f, 0f, 10.5f), 3f, out Vector3[] corners));
            Vector3 last = corners[corners.Length - 1];
            Assert.AreEqual(9, grid.CellOf(last).x);
            Assert.AreEqual(10, grid.CellOf(last).y);
        }

        [Test]
        public void NextCornerOnTheFirstLegIsTheSecondCorner()
        {
            // 角 0（出発点）から 1 m 進んだ所にいる。出発点へ引き返さず、角 1 へ向かう。
            var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f) };
            Assert.AreEqual(1, GridPath.NextCorner(new Vector3(1f, 0f, 0.2f), corners));
        }

        [Test]
        public void NextCornerOnTheSecondLegIsTheThirdCorner()
        {
            var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f) };
            Assert.AreEqual(2, GridPath.NextCorner(new Vector3(10.3f, 0f, 4f), corners));
        }

        [Test]
        public void NextCornerBeforeTheStartIsTheStart()
        {
            var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            Assert.AreEqual(0, GridPath.NextCorner(new Vector3(-5f, 0f, 0f), corners));
        }

        [Test]
        public void NearPathIsTrueOnlyCloseToTheLine()
        {
            var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            Assert.IsTrue(GridPath.NearPath(new Vector3(5f, 0f, 2f), corners, 3f));
            Assert.IsFalse(GridPath.NearPath(new Vector3(5f, 0f, 4f), corners, 3f));
        }

        [Test]
        public void NextCornerSkipsTheUpperFloorRightAbove()
        {
            // 1F の出発点から奥の階段で 2F へ上がり、出発点の真上まで戻ってくる道。1F にいる間は、
            // 真上の 2F の区間が近くても数えず、階段へ向かう角 1 を選ぶ。
            var corners = new[]
            {
                new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 30f), new Vector3(0f, 4.8f, 40f),
                new Vector3(1f, 4.8f, 40f), new Vector3(1f, 4.8f, 0f),
            };
            Assert.AreEqual(1, GridPath.NextCorner(new Vector3(0.9f, 0f, 3f), corners));
            Assert.AreEqual(4, GridPath.NextCorner(new Vector3(1f, 4.8f, 10f), corners));
        }

        [Test]
        public void NearPathIgnoresThePathOnAnotherFloor()
        {
            var corners = new[] { new Vector3(0f, 4.8f, 0f), new Vector3(10f, 4.8f, 0f) };
            Assert.IsFalse(GridPath.NearPath(new Vector3(5f, 0f, 0f), corners, 3f));
            Assert.IsTrue(GridPath.NearPath(new Vector3(5f, 4.8f, 1f), corners, 3f));
        }

        [Test]
        public void UpperFloorIsReachedByTheStairs()
        {
            GridPath.Grid grid = TwoFloors();
            Assert.IsTrue(GridPath.Solve(grid, new Vector3(2.5f, 0f, 2.5f), new Vector3(15.5f, 4.5f, 2.5f), 2f, out Vector3[] corners));
            Assert.AreEqual(4f, corners[corners.Length - 1].y, 0.01f);
            Assert.IsTrue(System.Array.Exists(corners, c => c.z > 17f), "階段（z = 18, 19）を通っていない");
        }

        [Test]
        public void GroundUnderTheUpperFloorIsReachedWithoutTheStairs()
        {
            GridPath.Grid grid = TwoFloors();
            Assert.IsTrue(GridPath.Solve(grid, new Vector3(2.5f, 0f, 2.5f), new Vector3(15.5f, 0.5f, 2.5f), 2f, out Vector3[] corners));
            Assert.AreEqual(1, corners.Length);
            Assert.AreEqual(0f, corners[0].y, 0.01f);
        }

        [Test]
        public void GoalAboveADeskEndsOnTheFloorBesideIt()
        {
            // 机の天板（0.75 m）は段差で乗れない。印が天板の真上に浮いていても、机の脇の床で止まる。
            GridPath.Grid grid = Open(20, 20);
            for (int z = 9; z <= 11; z++)
            {
                for (int x = 11; x <= 13; x++)
                {
                    grid.Heights[x + z * grid.Width] = 0.75f;
                }
            }

            Assert.IsTrue(GridPath.Solve(grid, new Vector3(2.5f, 0f, 10.5f), new Vector3(12.5f, 1.2f, 10.5f), 4f, out Vector3[] corners));
            Vector3 last = corners[corners.Length - 1];
            Assert.AreEqual(0f, last.y, 0.01f);
            Assert.AreEqual(10, grid.CellOf(last).x);
        }

        [Test]
        public void BothFloorsOfOneColliderAreSampled()
        {
            // 1 つのメッシュに 1 階（高さ 0）と 2 階（高さ 4）の床がある。線は 2 階の床で止まらず、下の床も拾う。
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-20f, 0f, -20f), new Vector3(-20f, 0f, 20f), new Vector3(20f, 0f, 20f), new Vector3(20f, 0f, -20f),
                    new Vector3(-20f, 4f, -20f), new Vector3(-20f, 4f, 20f), new Vector3(20f, 4f, 20f), new Vector3(20f, 4f, -20f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 },
            };
            mesh.RecalculateNormals();
            var building = new GameObject("TwoFloorMesh");
            try
            {
                building.AddComponent<MeshCollider>().sharedMesh = mesh;
                Physics.SyncTransforms();
                GridPath.Grid grid = GridPath.Sample(new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f), 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers);
                Vector2Int cell = grid.CellOf(Vector3.zero);
                Assert.AreEqual(0f, grid.Heights[grid.Node(cell.x, cell.y, 0)], 0.01f);
                Assert.AreEqual(4f, grid.Heights[grid.Node(cell.x, cell.y, 1)], 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(building);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FloorsNearTheStartAreKeptWhenAColumnHasTooMany()
        {
            // 2 m おきに 0〜12 m の床が 7 枚ある。10 m から調べると 4〜16 m の範囲の 5 枚が当たり、
            // 持てるのは 4 枚。出発点から遠い 4 m を捨て、12 m を残す。
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            for (int floor = 0; floor <= 6; floor++)
            {
                float y = floor * 2f;
                int at = vertices.Count;
                vertices.Add(new Vector3(-20f, y, -20f));
                vertices.Add(new Vector3(-20f, y, 20f));
                vertices.Add(new Vector3(20f, y, 20f));
                vertices.Add(new Vector3(20f, y, -20f));
                triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
            }

            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            var building = new GameObject("TallMesh");
            try
            {
                building.AddComponent<MeshCollider>().sharedMesh = mesh;
                Physics.SyncTransforms();
                GridPath.Grid grid = GridPath.Sample(new Vector3(0f, 10f, 0f), new Vector3(5f, 10f, 0f), 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers);
                Vector2Int cell = grid.CellOf(Vector3.zero);
                var kept = new float[GridPath.MaxLayers];
                for (int layer = 0; layer < GridPath.MaxLayers; layer++)
                {
                    kept[layer] = grid.Heights[grid.Node(cell.x, cell.y, layer)];
                }

                float[] expected = { 6f, 8f, 10f, 12f };
                for (int layer = 0; layer < expected.Length; layer++)
                {
                    Assert.AreEqual(expected[layer], kept[layer], 0.01f, "層 " + layer);
                }
            }
            finally
            {
                Object.DestroyImmediate(building);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FloorsOfBothTheStartAndTheGoalAreKept()
        {
            // 2 m おきの床 7 枚（0〜12 m）。2 m から 10 m の階へ向かうとき、柱に残す 4 枚に両方の階が入る
            // （出発点の近くだけを残すと、目的の階が抜けて別の階への道が引けない）。
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            for (int floor = 0; floor <= 6; floor++)
            {
                float y = floor * 2f;
                int at = vertices.Count;
                vertices.Add(new Vector3(-20f, y, -20f));
                vertices.Add(new Vector3(-20f, y, 20f));
                vertices.Add(new Vector3(20f, y, 20f));
                vertices.Add(new Vector3(20f, y, -20f));
                triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
            }

            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            var building = new GameObject("TallMesh");
            try
            {
                building.AddComponent<MeshCollider>().sharedMesh = mesh;
                Physics.SyncTransforms();
                GridPath.Grid grid = GridPath.Sample(new Vector3(0f, 2f, 0f), new Vector3(5f, 10f, 0f), 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers);
                Vector2Int cell = grid.CellOf(Vector3.zero);
                var kept = new System.Collections.Generic.List<float>();
                for (int layer = 0; layer < GridPath.MaxLayers; layer++)
                {
                    kept.Add(grid.Heights[grid.Node(cell.x, cell.y, layer)]);
                }

                Assert.That(kept.Exists(y => Mathf.Abs(y - 2f) < 0.01f), "出発点の階 " + string.Join(",", kept));
                Assert.That(kept.Exists(y => Mathf.Abs(y - 10f) < 0.01f), "目的の階 " + string.Join(",", kept));
            }
            finally
            {
                Object.DestroyImmediate(building);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AWideMarginReachesStairsFarFromBothEnds()
        {
            // 出発点と目的地のどちらからも 20 m 離れた所にしか上る段が無い。既定の幅では格子に段が入らず道が無く、
            // 幅を広げると段を回る道が見つかる。
            var objects = new System.Collections.Generic.List<GameObject>();
            try
            {
                objects.Add(Box("Ground", new Vector3(10f, -0.5f, 0f), new Vector3(80f, 1f, 80f)));
                // 上の階（高さ 3 m）。x < 0 の側だけにあり、出発点（x = 6）の真上は空いている。
                objects.Add(Box("Upper", new Vector3(-15f, 2.75f, 0f), new Vector3(30f, 0.5f, 80f)));
                // 段: z = 25..31 を上る（0.25 m ずつ 12 段）、上の階の縁 x = -2..0 に着く。
                for (int i = 0; i < 12; i++)
                {
                    float top = 0.25f * (i + 1);
                    objects.Add(Box("Step" + i, new Vector3(1f, top * 0.5f, 25f + 0.5f * i + 0.25f), new Vector3(2f, top, 0.5f)));
                }

                objects.Add(Box("Landing", new Vector3(1f, 1.5f, 32f), new Vector3(2f, 3f, 2f)));
                Physics.SyncTransforms();
                var from = new Vector3(6f, 0f, 0f);
                var goal = new Vector3(-6f, 3f, 0f);
                GridPath.Grid narrow = GridPath.Sample(from, goal, 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers, 10f);
                Assert.IsFalse(GridPath.Solve(narrow, from, goal, 1f, 1f, out _), "既定の幅では段が入らない");
                GridPath.Grid wide = GridPath.Sample(from, goal, 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers, 40f);
                Assert.IsTrue(GridPath.Solve(wide, from, goal, 1f, 1f, out Vector3[] corners), "広げると段を回れる");
                Assert.Greater(corners.Length, 1);
            }
            finally
            {
                foreach (GameObject o in objects)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }

        private static GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var o = new GameObject(name);
            o.transform.position = center;
            o.AddComponent<BoxCollider>().size = size;
            return o;
        }
        /// <summary>出発点 (4.5, 2.5) のまわりの自分の側を机で塞ぐ。自分の側で一番近い歩ける升は 2.2 升先。</summary>
        private static void BlockCorner(GridPath.Grid grid)
        {
            foreach (Vector2Int cell in new[]
            {
                new Vector2Int(4, 2), new Vector2Int(3, 2), new Vector2Int(2, 2), new Vector2Int(4, 1), new Vector2Int(4, 3),
                new Vector2Int(3, 1), new Vector2Int(3, 3), new Vector2Int(2, 1), new Vector2Int(2, 3), new Vector2Int(4, 0),
                new Vector2Int(4, 4),
            })
            {
                Block(grid, cell.x, cell.y);
            }
        }

        /// <summary>
        /// x = 5 に壁。出発点 (4.5, 2.5) は机と壁の隅で、一番近い歩ける升は壁の向こう (6.5, 2.5)。
        /// 壁の向こうは見えないとすると、自分の側の升へ寄せる。
        /// </summary>
        [Test]
        public void StartIsSnappedOnlyToCellsItCanSee()
        {
            GridPath.Grid grid = Open(10, 10);
            for (int z = 0; z < 10; z++)
            {
                Block(grid, 5, z);
            }

            BlockCorner(grid);
            Vector3 from = new Vector3(4.5f, 0f, 2.5f);

            Assert.IsTrue(GridPath.TryNearest(grid, from, 3f, out int nearest));
            Assert.Greater(grid.World(nearest).x, 5f);

            Assert.IsTrue(GridPath.TryNearest(grid, from, 3f, cell => cell.x < 5f, out int seen));
            Assert.Less(grid.World(seen).x, 5f);
        }

        [Test]
        public void StartFallsBackToNearestWhenNoCellIsSeen()
        {
            GridPath.Grid grid = Open(10, 10);
            Assert.IsTrue(GridPath.TryNearest(grid, new Vector3(4.5f, 0f, 4.5f), 2f, cell => false, out int found));
            Assert.AreEqual(NodeAt(grid, new Vector3(4.5f, 0f, 4.5f)), found);
        }

        [Test]
        public void PathFromACornerStaysOnTheSameSideOfTheWall()
        {
            GridPath.Grid grid = Open(10, 10);
            for (int z = 0; z < 9; z++)
            {
                Block(grid, 5, z);
            }

            BlockCorner(grid);
            Vector3 from = new Vector3(4.5f, 0f, 2.5f);
            Vector3 goal = new Vector3(7.5f, 0f, 2.5f);

            Assert.IsTrue(GridPath.Solve(grid, from, goal, 3f, 1f, cell => cell.x < 5f, out Vector3[] corners));
            Assert.Less(corners[0].x, 5f, "最初の角は壁のこちら側");
            Assert.IsTrue(System.Array.Exists(corners, c => c.z > 8f), "壁の切れ目（z = 9）を回る");
        }

        [Test]
        public void SoilInsideACurbHigherThanTheStepIsNotEntered()
        {
            // 花壇: 地面 0.021 m、縁石（厚み 0.2 m）の天端 0.45 m、内側の土 0.38 m。
            // 土へは地面から 0.36 m の段に見えるが、間の縁石は 0.43 m あって上がれない。
            var made = new System.Collections.Generic.List<GameObject>();
            GameObject Box(Vector3 min, Vector3 max)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.position = (min + max) * 0.5f;
                box.transform.localScale = max - min;
                made.Add(box);
                return box;
            }

            try
            {
                Box(new Vector3(-20f, -0.1f, -20f), new Vector3(20f, 0.021f, 20f));
                Box(new Vector3(0f, 0.021f, -2f), new Vector3(4f, 0.45f, -1.8f));
                Box(new Vector3(0f, 0.021f, 1.8f), new Vector3(4f, 0.45f, 2f));
                Box(new Vector3(0f, 0.021f, -1.8f), new Vector3(0.2f, 0.45f, 1.8f));
                Box(new Vector3(3.8f, 0.021f, -1.8f), new Vector3(4f, 0.45f, 1.8f));
                Box(new Vector3(0.2f, 0.021f, -1.8f), new Vector3(3.8f, 0.38f, 1.8f));
                Physics.SyncTransforms();

                // 升の並びが縁石に対してどうずれても入らないことを、出発点を少しずつずらして確かめる。
                for (int k = 0; k < 8; k++)
                {
                    Vector3 from = new Vector3(-3f - k * 0.05f, 0.021f, 0.3f);
                    Vector3 goal = new Vector3(2f, 0.38f, 0f);
                    GridPath.Grid grid = GridPath.Sample(from, goal, 0.3f, 1.7f, 0.4f, Physics.DefaultRaycastLayers);
                    Assert.IsTrue(GridPath.Solve(grid, from, goal, 1f, 4f, out Vector3[] corners), "k=" + k);
                    foreach (Vector3 c in corners)
                    {
                        Assert.Less(c.y, 0.2f, "k=" + k + " 土の上 " + c);
                    }
                }
            }
            finally
            {
                foreach (GameObject box in made)
                {
                    Object.DestroyImmediate(box);
                }
            }
        }
    }
}
