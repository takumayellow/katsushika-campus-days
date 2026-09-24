using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// カメラを止める当たり判定の選び方と、見る点からの球の掃引 (#12)。
    /// 壁（Building）・床（Ground）・木の幹（Default の trunk）で止まり、プレイヤー・NPC・トリガー・見えない柵は素通りする。
    /// 掃引のテストはシーンの中身と重ならないよう y = 10000 に置く。
    /// </summary>
    public sealed class CameraObstacleFilterTests
    {
        private const float Radius = 0.25f;
        private static readonly Vector3 Origin = new Vector3(0f, 10000f, 0f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void Blocks_GroundAndBuildingButOnlyTrunksOnDefault()
        {
            const int ground = 8;
            const int building = 9;

            Assert.IsTrue(CameraObstacleFilter.Blocks(ground, "floor_1f", false, ground, building));
            Assert.IsTrue(CameraObstacleFilter.Blocks(building, "wall", false, ground, building));
            Assert.IsTrue(CameraObstacleFilter.Blocks(CameraObstacleFilter.DefaultLayer, "trunk", false, ground, building));

            Assert.IsFalse(CameraObstacleFilter.Blocks(CameraObstacleFilter.DefaultLayer, "Keepout_BasinWall_0", false, ground, building),
                "堀の見えない壁ではカメラを寄せない");
            Assert.IsFalse(CameraObstacleFilter.Blocks(CameraObstacleFilter.DefaultLayer, "Blocker", false, ground, building));
            Assert.IsFalse(CameraObstacleFilter.Blocks(CameraObstacleFilter.DefaultLayer, null, false, ground, building));
            Assert.IsFalse(CameraObstacleFilter.Blocks(10, "Player", false, ground, building), "プレイヤー");
            Assert.IsFalse(CameraObstacleFilter.Blocks(11, "NPC_x", false, ground, building), "NPC");
            Assert.IsFalse(CameraObstacleFilter.Blocks(building, "wall", true, ground, building), "トリガー");
            Assert.IsFalse(CameraObstacleFilter.Blocks(CameraObstacleFilter.DefaultLayer, "trunk", true, ground, building));
        }

        [Test]
        public void Blocks_MissingLayersAreNotMatched()
        {
            Assert.IsFalse(CameraObstacleFilter.Blocks(5, "wall", false, -1, -1));
            Assert.IsTrue(CameraObstacleFilter.Blocks(0, "trunk", false, -1, -1));
        }

        [Test]
        public void IsCeiling_OnlyDownwardFaces()
        {
            Assert.IsTrue(CameraObstacleFilter.IsCeiling(Vector3.down));
            Assert.IsTrue(CameraObstacleFilter.IsCeiling(new Vector3(0.5f, -0.8f, 0f).normalized));
            Assert.IsFalse(CameraObstacleFilter.IsCeiling(Vector3.up));
            Assert.IsFalse(CameraObstacleFilter.IsCeiling(Vector3.forward), "壁");
        }

        [Test]
        public void Mask_CoversDefaultGroundAndBuilding()
        {
            int mask = CameraObstacleFilter.Mask;
            Assert.AreNotEqual(0, mask & 1, "Default（幹）");
            Assert.AreNotEqual(0, mask & (1 << Layer("Ground")));
            Assert.AreNotEqual(0, mask & (1 << Layer("Building")));
            Assert.AreEqual(0, mask & (1 << Layer("Player")));
            Assert.AreEqual(0, mask & (1 << Layer("NPC")));
        }

        [Test]
        public void Sweep_StopsAtTheNearestTrunkOrWallAndIgnoresTheRest()
        {
            // 見る点から奥（+Z）へ: トリガーの壁 0.6 m、プレイヤー 0.9 m、NPC 1.1 m、見えない柵 1.3 m、幹 2 m、建物の壁 3 m。
            Box("TriggerWall", Layer("Building"), new Vector3(0f, 0f, 0.6f), new Vector3(3f, 3f, 0.1f), true);
            Box("Player", Layer("Player"), new Vector3(0f, 0f, 0.9f), new Vector3(3f, 3f, 0.1f), false);
            Box("NPC_test", Layer("NPC"), new Vector3(0f, 0f, 1.1f), new Vector3(3f, 3f, 0.1f), false);
            Box("Keepout_BasinWall_0", CameraObstacleFilter.DefaultLayer, new Vector3(0f, 0f, 1.3f), new Vector3(3f, 3f, 0.1f), false);
            GameObject trunk = Trunk(new Vector3(0f, -1.5f, 2f), 0.3f);
            Box("Wall", Layer("Building"), new Vector3(0f, 0f, 3f), new Vector3(3f, 3f, 0.2f), false);
            Physics.SyncTransforms();

            float trunkHit = CameraObstacleFilter.Sweep(Origin, Radius, Vector3.forward, 6f, out Vector3 normal);
            Assert.AreEqual(2f - 0.3f - Radius, trunkHit, 0.02f, "幹の手前で止まる");
            Assert.Less(normal.z, -0.5f);

            Object.DestroyImmediate(trunk);
            Physics.SyncTransforms();
            float wallHit = CameraObstacleFilter.Sweep(Origin, Radius, Vector3.forward, 6f, out _);
            Assert.AreEqual(3f - 0.1f - Radius, wallHit, 0.02f, "建物の壁の手前で止まる");

            Assert.AreEqual(-1f, CameraObstacleFilter.Sweep(Origin, Radius, Vector3.forward, 2f, out _), 1e-6f,
                "届かない距離の壁は数えない");
            Assert.AreEqual(-1f, CameraObstacleFilter.Sweep(Origin, Radius, Vector3.back, 6f, out _), 1e-6f,
                "後ろに何も無ければ当たらない");
        }

        [Test]
        public void Sweep_WallAlreadyTouchingTheLookAtPoint()
        {
            // 見る点の球が最初から触れている壁。カメラの向きにある壁は 0 m で止め、真横の壁は止めない。
            Box("WallAhead", Layer("Building"), new Vector3(0f, 0f, 0.2f), new Vector3(3f, 3f, 0.1f), false);
            Box("WallBeside", Layer("Building"), new Vector3(0.28f, 0f, 0f), new Vector3(0.1f, 3f, 3f), false);
            Physics.SyncTransforms();

            Assert.AreEqual(0f, CameraObstacleFilter.Sweep(Origin, Radius, Vector3.forward, 4f, out _), 0.02f);
            Assert.AreEqual(-1f, CameraObstacleFilter.Sweep(Origin, Radius, Vector3.back, 4f, out _), 1e-6f,
                "真横の壁に触れているだけなら後ろへは下がれる");
        }

        [Test]
        public void CeilingAbove_FindsTheLowestBlockingCeiling()
        {
            Box("Ceiling", Layer("Building"), new Vector3(0f, 2f, 0f), new Vector3(4f, 0.2f, 4f), false);
            Box("Keepout_BasinWall_1", CameraObstacleFilter.DefaultLayer, new Vector3(0f, 1f, 0f), new Vector3(4f, 0.2f, 4f), false);
            Physics.SyncTransforms();

            Assert.AreEqual(Origin.y + 1.9f, CameraObstacleFilter.CeilingAbove(Origin, Radius, 3f), 0.02f,
                "見えない柵は天井に数えない");
            Assert.IsTrue(float.IsPositiveInfinity(CameraObstacleFilter.CeilingAbove(Origin, Radius, 1f)),
                "届かない天井は無いのと同じ");
        }

        private static int Layer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            Assert.GreaterOrEqual(layer, 0, "レイヤー " + name + " が無い");
            return layer;
        }

        private GameObject Box(string name, int layer, Vector3 offset, Vector3 size, bool trigger)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.layer = layer;
            go.transform.position = Origin + offset;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
            return go;
        }

        private GameObject Trunk(Vector3 offset, float radius)
        {
            var go = new GameObject(CameraObstacleFilter.TreeTrunkName);
            _created.Add(go);
            go.layer = CameraObstacleFilter.DefaultLayer;
            go.transform.position = Origin + offset;
            CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = 6f;
            capsule.center = new Vector3(0f, 3f, 0f);
            return go;
        }
    }
}
