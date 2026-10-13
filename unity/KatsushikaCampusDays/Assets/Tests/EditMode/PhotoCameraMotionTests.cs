using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// フォトモードの自由カメラの動き (#12)。
    /// 見る点から一定の距離の内側に留まり、壁の手前で止まり、斜めに当たった分は壁に沿って滑る。
    /// 当たり判定はシーンの中身と重ならないよう y = 10000 に置く。
    /// </summary>
    public sealed class PhotoCameraMotionTests
    {
        private const float Range = 8f;
        private const float Stop = PhotoCameraMotion.CastRadius + PhotoCameraMotion.Skin;
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
        public void Constrain_MovesFreelyWhenNothingBlocks()
        {
            // 通り道にプレイヤー・トリガーがあっても止まらない。
            Box("Player", Layer("Player"), new Vector3(0f, 0f, 1f), new Vector3(3f, 3f, 0.1f), false);
            Box("TriggerWall", Layer("Building"), new Vector3(0f, 0f, 2f), new Vector3(3f, 3f, 0.1f), true);
            Physics.SyncTransforms();

            Vector3 desired = Origin + new Vector3(0.5f, 0.2f, 3f);
            AssertNear(desired, PhotoCameraMotion.Constrain(Origin, desired, Origin, Range));
        }

        [Test]
        public void Constrain_KeepsTheCameraWithinRangeOfTheAnchor()
        {
            Vector3 from = Origin + new Vector3(0f, 0f, 7.5f);
            Vector3 result = PhotoCameraMotion.Constrain(from, Origin + new Vector3(0f, 0f, 12f), Origin, Range);

            AssertNear(Origin + new Vector3(0f, 0f, Range), result);
        }

        [Test]
        public void Constrain_StopsInFrontOfAWallItMovesStraightInto()
        {
            // 壁の手前の面は z = 1.9。球の半径と skin の分だけ手前で止まる。
            Box("Wall", Layer("Building"), new Vector3(0f, 0f, 2f), new Vector3(6f, 6f, 0.2f), false);
            Physics.SyncTransforms();

            Vector3 result = PhotoCameraMotion.Constrain(Origin, Origin + new Vector3(0f, 0f, 4f), Origin, Range);

            AssertNear(Origin + new Vector3(0f, 0f, 1.9f - Stop), result);
        }

        [Test]
        public void Constrain_SlidesAlongAWallItHitsAtAnAngle()
        {
            Box("Wall", Layer("Building"), new Vector3(0f, 0f, 2f), new Vector3(6f, 6f, 0.2f), false);
            Physics.SyncTransforms();

            Vector3 result = PhotoCameraMotion.Constrain(Origin, Origin + new Vector3(2f, 0f, 4f), Origin, Range);

            Assert.AreEqual(2f, result.x - Origin.x, 0.02f, "壁に沿って横へは最後まで進む");
            Assert.AreEqual(Origin.y, result.y, 0.02f);
            Assert.Less(result.z - Origin.z, 1.9f - PhotoCameraMotion.CastRadius, "壁には入らない");
            Assert.Greater(result.z - Origin.z, 1.9f - Stop - 0.1f, "壁の手前まで寄っている");
        }

        [Test]
        public void Constrain_StopsInACorner()
        {
            // 奥の壁（面 z = 1.9）に斜めに当たり、滑った先の横の壁（面 x = 1.4）で止まる。
            Box("WallAhead", Layer("Building"), new Vector3(0f, 0f, 2f), new Vector3(6f, 6f, 0.2f), false);
            Box("WallSide", Layer("Building"), new Vector3(1.5f, 0f, 0f), new Vector3(0.2f, 6f, 6f), false);
            Physics.SyncTransforms();

            Vector3 result = PhotoCameraMotion.Constrain(Origin, Origin + new Vector3(2f, 0f, 4f), Origin, Range);

            Assert.AreEqual(1.4f - Stop, result.x - Origin.x, 0.02f);
            Assert.Less(result.z - Origin.z, 1.9f - PhotoCameraMotion.CastRadius);
        }

        [Test]
        public void Constrain_StopsAtATreeTrunk()
        {
            var trunk = new GameObject(CameraObstacleFilter.TreeTrunkName);
            _created.Add(trunk);
            trunk.layer = CameraObstacleFilter.DefaultLayer;
            trunk.transform.position = Origin + new Vector3(0f, -1.5f, 2f);
            CapsuleCollider capsule = trunk.AddComponent<CapsuleCollider>();
            capsule.radius = 0.3f;
            capsule.height = 6f;
            capsule.center = new Vector3(0f, 3f, 0f);
            Physics.SyncTransforms();

            Vector3 result = PhotoCameraMotion.Constrain(Origin, Origin + new Vector3(0f, 0f, 4f), Origin, Range);

            AssertNear(Origin + new Vector3(0f, 0f, 2f - 0.3f - Stop), result);
        }

        [Test]
        public void Constrain_WithoutMovementStaysPut()
        {
            Vector3 from = Origin + new Vector3(1f, 0.5f, -2f);
            AssertNear(from, PhotoCameraMotion.Constrain(from, from, Origin, Range));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 0.02f, "expected " + expected + " but was " + actual);
        }

        private static int Layer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            Assert.GreaterOrEqual(layer, 0, "レイヤー " + name + " が無い");
            return layer;
        }

        private void Box(string name, int layer, Vector3 offset, Vector3 size, bool trigger)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.layer = layer;
            go.transform.position = Origin + offset;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
        }
    }
}
