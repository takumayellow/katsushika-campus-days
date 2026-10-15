using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// エレベーターの乗り場は、扉の面の上でプレイヤーに最も近い点で「調べる」の候補を測る (#303)。
    /// 判定の中心は扉から 0.7 m 前にあり、そこより扉に寄って扉を向くと、中心が背中側になって乗れなかった。
    /// </summary>
    public sealed class ElevatorPanelAimTests
    {
        private const float FacingDot = 0.15f;
        private const float Range = 2.4f;

        private GameObject _go;
        private ElevatorPanel _panel;

        [SetUp]
        public void SetUp()
        {
            // 着いたときの向き（+z）に扉を背にする。扉の面は z = -0.7。
            _go = new GameObject("Elevator_test");
            _panel = _go.AddComponent<ElevatorPanel>();
            _panel.SetDoor(0.7f, 1.2f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void AimPoint_IsTheNearestPointOnTheDoorFace()
        {
            AssertNear(new Vector3(0.3f, 0f, -0.7f), _panel.AimPoint(new Vector3(0.3f, 1f, 0.5f)));
            AssertNear(new Vector3(1.2f, 0f, -0.7f), _panel.AimPoint(new Vector3(2.5f, 0f, -0.4f)));
            AssertNear(new Vector3(-1.2f, 0f, -0.7f), _panel.AimPoint(new Vector3(-3f, 0f, 0.2f)));
        }

        [Test]
        public void AimPoint_FollowsThePanelRotation()
        {
            _go.transform.SetPositionAndRotation(new Vector3(10f, 4f, 0f), Quaternion.Euler(0f, 90f, 0f));
            AssertNear(new Vector3(9.3f, 4f, -0.5f), _panel.AimPoint(new Vector3(11f, 4f, -0.5f)));
        }

        [Test]
        public void StandingRightAtTheDoorAndFacingIt_IsACandidate()
        {
            Vector3 feet = new Vector3(0.4f, 0f, -0.4f);
            Assert.IsTrue(InteractionPrompt.TryScore(feet, Vector3.back, _panel.AimPoint(feet), Range, FacingDot, out _),
                "扉のすぐ前で扉を向いても候補にならない");
            Assert.IsFalse(InteractionPrompt.TryScore(feet, Vector3.back, _go.transform.position, Range, FacingDot, out _),
                "判定の中心で測っても候補になる（この位置では中心が背中側のはず）");
        }

        [Test]
        public void BesideTheSecondDoorFacingIt_IsACandidate()
        {
            Vector3 feet = new Vector3(1.6f, 0f, -0.35f);
            Assert.IsTrue(InteractionPrompt.TryScore(feet, Vector3.back, _panel.AimPoint(feet), Range, FacingDot, out _),
                "端の扉の前で扉を向いても候補にならない");
        }

        [Test]
        public void FarFromTheDoor_IsNotACandidate()
        {
            Vector3 feet = new Vector3(0f, 0f, 2.0f);
            Assert.IsFalse(InteractionPrompt.TryScore(feet, Vector3.back, _panel.AimPoint(feet), Range, FacingDot, out _),
                "扉から 2.7 m 離れても候補になる");
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, "期待 " + expected.ToString("F2") + " 実際 " + actual.ToString("F2"));
        }
    }
}
