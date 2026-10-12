using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>出口の範囲が入った所から離れて、扉の外まで伸びていること（#212）。</summary>
    public sealed class InteriorExitBoxTests
    {
        [TestCase(1.25f)]
        [TestCase(0.95f)]
        public void NearEdge_IsSpawnGapFromTheSpawn_AndFarEdgeIsPastTheDoor(float distance)
        {
            Vector3 spawn = new Vector3(40f, 0.1f, 7f);
            Vector3 exit = spawn + new Vector3(0.6f, 0f, 0.8f) * distance;
            InteriorExit.BoxFor(exit, spawn, out Vector3 center, out float yaw, out Vector3 size);

            Vector3 axis = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 flatCenter = new Vector3(center.x, spawn.y, center.z);
            float near = Vector3.Dot(flatCenter - spawn, axis) - size.z * 0.5f;
            float far = Vector3.Dot(flatCenter - spawn, axis) + size.z * 0.5f;
            Assert.AreEqual(InteriorExit.SpawnGap, near, 1e-3f);
            Assert.AreEqual(distance + InteriorExit.OutwardReach, far, 1e-3f);
            Assert.AreEqual(InteriorExit.BoxWidth, size.x, 1e-4f);
            Assert.AreEqual(exit.y + 1.2f, center.y, 1e-4f);
        }

        [Test]
        public void SpawnOnTheMarker_StillGivesABoxAheadOfTheSpawn()
        {
            Vector3 spawn = new Vector3(5f, 0f, 5f);
            InteriorExit.BoxFor(spawn, spawn, out _, out _, out Vector3 size);
            Assert.Greater(size.z, 0f);
        }
    }
}
