using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 画面の上の目的地の印 (#163)。見えている目的地にはその位置に、画面の外や背後の目的地には
    /// 画面の端（余白の内側）に、目的地の向きで置くこと。屋内から外の目的地を指すときの出口探し。
    /// </summary>
    public sealed class ObjectiveWaypointTests
    {
        private static readonly Vector2 Area = new Vector2(1920f, 1080f);
        private const float Margin = 72f;
        private const float Tolerance = 0.01f;

        [Test]
        public void VisibleGoalStaysWhereItIs()
        {
            ObjectiveWaypoint.Placement center = ObjectiveWaypoint.Place(new Vector3(0.5f, 0.5f, 10f), Area, Margin);
            ObjectiveWaypoint.Placement right = ObjectiveWaypoint.Place(new Vector3(0.75f, 0.5f, 10f), Area, Margin);

            Assert.IsTrue(center.OnScreen);
            AssertNear(Vector2.zero, center.Position);
            Assert.IsTrue(right.OnScreen);
            AssertNear(new Vector2(480f, 0f), right.Position);
        }

        [Test]
        public void GoalOffToTheRightSitsOnTheRightEdge()
        {
            ObjectiveWaypoint.Placement placement = ObjectiveWaypoint.Place(new Vector3(1.5f, 0.5f, 10f), Area, Margin);

            Assert.IsFalse(placement.OnScreen);
            AssertNear(new Vector2(888f, 0f), placement.Position);
            Assert.AreEqual(0f, placement.Angle, Tolerance);
        }

        [Test]
        public void GoalInsideTheMarginIsTreatedAsOffScreen()
        {
            // 画面の端から 72 未満の所は札が切れるので、端の印にする。
            ObjectiveWaypoint.Placement placement = ObjectiveWaypoint.Place(new Vector3(0.99f, 0.5f, 10f), Area, Margin);

            Assert.IsFalse(placement.OnScreen);
            AssertNear(new Vector2(888f, 0f), placement.Position);
        }

        [Test]
        public void DiagonalGoalKeepsItsDirectionOnTheEdge()
        {
            // 中央から (1920, 1080) の向き。上の辺（468）に先に当たる。
            ObjectiveWaypoint.Placement placement = ObjectiveWaypoint.Place(new Vector3(1.5f, 1.5f, 10f), Area, Margin);

            Assert.IsFalse(placement.OnScreen);
            AssertNear(new Vector2(1920f * 468f / 1080f, 468f), placement.Position);
            Assert.AreEqual(Mathf.Atan2(1080f, 1920f) * Mathf.Rad2Deg, placement.Angle, Tolerance);
        }

        [Test]
        public void GoalBehindPointsTheOtherWay()
        {
            // 背後の点はビューポート座標が反転して返る。右に見えた (0.6) のは実は左後ろ。
            ObjectiveWaypoint.Placement placement = ObjectiveWaypoint.Place(new Vector3(0.6f, 0.5f, -5f), Area, Margin);

            Assert.IsFalse(placement.OnScreen, "背後の目的地を画面の中に出した");
            AssertNear(new Vector2(-888f, 0f), placement.Position);
            Assert.AreEqual(180f, Mathf.Abs(placement.Angle), Tolerance);
        }

        [Test]
        public void GoalRightBehindPointsDown()
        {
            ObjectiveWaypoint.Placement placement = ObjectiveWaypoint.Place(new Vector3(0.5f, 0.5f, -5f), Area, Margin);

            Assert.IsFalse(placement.OnScreen);
            AssertNear(new Vector2(0f, -468f), placement.Position);
            Assert.AreEqual(-90f, placement.Angle, Tolerance);
        }

        [Test]
        public void ExitOfTheCurrentBuildingIsTheNearestOneWithThatId()
        {
            GameObject far = MakeExit("library", new Vector3(0f, 0f, 10f));
            GameObject near = MakeExit("library", new Vector3(0f, 0f, 3f));
            GameObject other = MakeExit("gym", new Vector3(0f, 0f, 1f));
            try
            {
                QuestObjectiveLocator.Invalidate();

                Assert.IsTrue(QuestObjectiveLocator.TryLocateExit("library", Vector3.zero, out Vector3 position));
                Assert.AreEqual(3f, position.z, Tolerance, "ほかの建物の出口か、遠いほうの出口を指した");
                Assert.IsFalse(QuestObjectiveLocator.TryLocateExit("lecture", Vector3.zero, out _));
                Assert.IsFalse(QuestObjectiveLocator.TryLocateExit(string.Empty, Vector3.zero, out _));
            }
            finally
            {
                Object.DestroyImmediate(far);
                Object.DestroyImmediate(near);
                Object.DestroyImmediate(other);
                QuestObjectiveLocator.Invalidate();
            }
        }

        private static GameObject MakeExit(string buildingId, Vector3 position)
        {
            var go = new GameObject("ExitForTest_" + buildingId);
            go.transform.position = position;
            go.AddComponent<InteriorExit>().BuildingId = buildingId;
            return go;
        }

        private static void AssertNear(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, "x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, "y");
        }
    }
}
