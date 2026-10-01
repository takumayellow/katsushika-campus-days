using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ミニマップの目的地の輪 (#163)。目的地はミニマップだけに出す。輪は円の中ならその位置に、外なら向きを保って縁に置く。
    /// 屋内では屋内の地点をそのまま出し、キャンパスの地点はいまいる建物の出口に替える。外では屋内の地点を出さない。
    /// </summary>
    public sealed class MinimapObjectiveTests
    {
        private const float PixelsPerMeter = 2f;
        private const float Limit = 100f;
        private const float Tolerance = 0.01f;

        /// <summary>キャンパスから遠く離れた屋内の模型の中の位置。</summary>
        private static readonly Vector3 Indoor = new Vector3(2000f, 0f, 0f);

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            QuestObjectiveLocator.Invalidate();
        }

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
            QuestObjectiveLocator.Invalidate();
        }

        [Test]
        public void GoalInsideTheCircleStaysWhereItIs()
        {
            Vector2 offset = Minimap.PlaceMarker(new Vector3(13f, 5f, -4f), new Vector3(3f, 0f, 6f), PixelsPerMeter, Limit);

            AssertNear(new Vector2(20f, -20f), offset);
        }

        [Test]
        public void GoalOutsideTheCircleSitsOnTheRimFacingIt()
        {
            Vector2 offset = Minimap.PlaceMarker(new Vector3(300f, 0f, 400f), Vector3.zero, PixelsPerMeter, Limit);

            AssertNear(new Vector2(60f, 80f), offset);
        }

        [Test]
        public void Outdoors_CampusGoalIsShown()
        {
            Minimap minimap = MakeMinimap();
            MakeZone("gate_main", new Vector3(10f, 0f, 20f));

            Assert.IsTrue(minimap.TryGoal(Visit("gate_main"), Vector3.zero, null, out Vector3 goal));
            AssertNear(new Vector3(10f, 0f, 20f), goal);
        }

        [Test]
        public void Outdoors_GoalInsideAnInteriorModelIsNotShown()
        {
            Minimap minimap = MakeMinimap();
            MakeZone("far_spot", Indoor + new Vector3(5f, 0f, 5f));

            Assert.IsFalse(minimap.TryGoal(Visit("far_spot"), Vector3.zero, null, out _),
                "外にいるのに、誰も歩いて行けない屋内の模型の中を指した");
        }

        [Test]
        public void Indoors_GoalInTheSameModelIsShownWhereItIs()
        {
            Minimap minimap = MakeMinimap();
            minimap.SetIndoor("図書館");
            MakeZone("far_spot", Indoor + new Vector3(5f, 0f, 5f));
            MakeExit("library", Indoor + new Vector3(0f, 0f, -8f));

            Assert.IsTrue(minimap.TryGoal(Visit("far_spot"), Indoor, "library", out Vector3 goal),
                "屋内の目的地がミニマップから消えた");
            AssertNear(Indoor + new Vector3(5f, 0f, 5f), goal);
        }

        [Test]
        public void Indoors_CampusGoalPointsAtTheExitOfThisBuilding()
        {
            Minimap minimap = MakeMinimap();
            minimap.SetIndoor("図書館");
            MakeZone("gate_main", new Vector3(10f, 0f, 20f));
            MakeExit("library", Indoor + new Vector3(0f, 0f, -8f));
            MakeExit("gym", Indoor + new Vector3(0f, 0f, -2f));

            Assert.IsTrue(minimap.TryGoal(Visit("gate_main"), Indoor, "library", out Vector3 goal));
            AssertNear(Indoor + new Vector3(0f, 0f, -8f), goal);
        }

        [Test]
        public void Indoors_CampusGoalWithoutAnExitIsNotShown()
        {
            Minimap minimap = MakeMinimap();
            minimap.SetIndoor("図書館");
            MakeZone("gate_main", new Vector3(10f, 0f, 20f));

            Assert.IsFalse(minimap.TryGoal(Visit("gate_main"), Indoor, "library", out _),
                "出口が無いのに、屋内からキャンパスの地点をそのまま指した");
        }

        [Test]
        public void LeavingTheBuildingStopsPointingAtTheExit()
        {
            Minimap minimap = MakeMinimap();
            minimap.SetIndoor("図書館");
            minimap.ClearIndoor();
            MakeZone("gate_main", new Vector3(10f, 0f, 20f));
            MakeExit("library", Indoor + new Vector3(0f, 0f, -8f));

            Assert.IsTrue(minimap.TryGoal(Visit("gate_main"), Vector3.zero, null, out Vector3 goal));
            AssertNear(new Vector3(10f, 0f, 20f), goal);
        }

        [Test]
        public void Indoors_CampusPictureIsHiddenButMarkersStay()
        {
            Minimap minimap = MakeMinimap();
            RectTransform map = MakeRect("Map", minimap.transform);
            RectTransform markers = MakeRect("Markers", minimap.transform);
            minimap.Bind(map, markers, null, null, null, null);

            minimap.SetIndoor("図書館");
            Assert.IsFalse(map.gameObject.activeSelf, "建物の中なのにキャンパスの絵が出ている");
            Assert.IsTrue(markers.gameObject.activeSelf, "建物の中で目的地の輪と NPC の点が消えた");

            minimap.ClearIndoor();
            Assert.IsTrue(map.gameObject.activeSelf, "外に出てもキャンパスの絵が戻らない");
        }

        [Test]
        public void ExitOfTheCurrentBuildingIsTheNearestOneWithThatId()
        {
            MakeExit("library", new Vector3(0f, 0f, 10f));
            MakeExit("library", new Vector3(0f, 0f, 3f));
            MakeExit("gym", new Vector3(0f, 0f, 1f));

            Assert.IsTrue(QuestObjectiveLocator.TryLocateExit("library", Vector3.zero, out Vector3 position));
            Assert.AreEqual(3f, position.z, Tolerance, "ほかの建物の出口か、遠いほうの出口を指した");
            Assert.IsFalse(QuestObjectiveLocator.TryLocateExit("lecture", Vector3.zero, out _));
            Assert.IsFalse(QuestObjectiveLocator.TryLocateExit(string.Empty, Vector3.zero, out _));
        }

        private Minimap MakeMinimap()
        {
            var go = new GameObject("MinimapForTest");
            _created.Add(go);
            Minimap minimap = go.AddComponent<Minimap>();
            minimap.Configure(Vector2.zero, 175f, 60f, 142f);
            return minimap;
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private void MakeZone(string placeId, Vector3 position)
        {
            var go = new GameObject("ZoneForTest_" + placeId);
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<VisitZone>().PlaceId = placeId;
        }

        private void MakeExit(string buildingId, Vector3 position)
        {
            var go = new GameObject("ExitForTest_" + buildingId);
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<InteriorExit>().BuildingId = buildingId;
        }

        private static QuestStep Visit(string placeId)
        {
            return new QuestStep { Kind = QuestStepKind.Visit, Target = placeId };
        }

        private static void AssertNear(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, "x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, "y");
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, "x");
            Assert.AreEqual(expected.z, actual.z, Tolerance, "z");
        }
    }
}
