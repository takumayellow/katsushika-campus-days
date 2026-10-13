using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 席から立つ位置 (#272)。座面の前に体が収まらない席（自分の当たり判定や机が前にかぶる）では、
    /// 座る前に立っていた所へ戻る。前が空いていれば前に出る。座る前の所が遠ければ戻さない。
    /// Awake で CharacterController を掴む本物の PlayerController を使うので PlayMode で見る。
    /// 座面は原点で +z を向き、前に出るのは +z へ DefaultStandForward。
    /// </summary>
    public sealed class StandUpClearancePlayTests
    {
        /// <summary>立った位置を比べる許し (m)。</summary>
        private const float Tolerance = 0.01f;

        private GameObject _host;
        private GameObject _anchor;
        private GameObject _blocker;
        private PlayerController _player;
        private bool _lockedBefore;

        [SetUp]
        public void SetUp()
        {
            _lockedBefore = KCDInput.MovementLocked;
            _host = new GameObject("StandUpClearancePlayTests");
            int layer = LayerMask.NameToLayer("Player");
            Assert.GreaterOrEqual(layer, 0, "Player レイヤーが無い");
            _host.layer = layer;
            var controller = _host.AddComponent<CharacterController>();
            controller.height = 1.6f;
            controller.radius = 0.28f;
            controller.center = new Vector3(0f, 0.82f, 0f);
            _player = _host.AddComponent<PlayerController>();
            _anchor = new GameObject("Anchor");
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_anchor);
            if (_blocker != null)
            {
                Object.Destroy(_blocker);
            }

            KCDInput.MovementLocked = _lockedBefore;
        }

        [UnityTest]
        public IEnumerator BlockedAhead_ReturnsToWhereThePlayerStoodBeforeSitting()
        {
            AddSeatBlocker();
            var before = new Vector3(1.2f, 0f, 0f);
            yield return null;
            SitFrom(before);

            _player.StandUp();
            Assert.IsFalse(_player.IsSitting, "立てない");
            AssertAt(before, "前が席の当たり判定で塞がっているのに、座る前の通路へ戻らない");
            // StandPosition が HasRoom で選んだ位置なので、ここは戻り先の選び方の確かめ。
            // 重力で落ち着いたあとの押し出しは InteriorSeatsStandUpPlayTests（SeatChecks.StandUp）が見る。
            Assert.IsFalse(StuckRecovery.IsPenetrating(_host.GetComponent<CharacterController>(),
                StuckRecovery.SolidMask(_host)), "立った体が席にめり込む");
        }

        [UnityTest]
        public IEnumerator BlockedAhead_FarBeforeSitting_StaysInFront()
        {
            AddSeatBlocker();
            var before = new Vector3(PlayerController.MaxReturnDistance + 2f, 0f, 0f);
            yield return null;
            SitFrom(before);

            _player.StandUp();
            Assert.IsFalse(_player.IsSitting, "立てない");
            AssertAt(new Vector3(0f, 0f, PlayerController.DefaultStandForward),
                "座る前の所が席から遠いのにそこへ飛ぶ");
        }

        [UnityTest]
        public IEnumerator ClearAhead_StandsInFront()
        {
            var before = new Vector3(1.2f, 0f, 0f);
            yield return null;
            SitFrom(before);

            _player.StandUp();
            Assert.IsFalse(_player.IsSitting, "立てない");
            AssertAt(new Vector3(0f, 0f, PlayerController.DefaultStandForward), "前が空いているのに前に出ない");
        }

        /// <summary>座面のまわりの固い箱。前に出る位置（+z 0.55 m）まで体がかぶる、背もたれの深い椅子。</summary>
        private void AddSeatBlocker()
        {
            _blocker = new GameObject("SeatBlocker");
            var box = _blocker.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.45f, 0.25f);
            box.size = new Vector3(0.8f, 0.9f, 1.3f);
        }

        /// <summary>before に置いてから座らせる。座る前に立っていた所はそこになる。</summary>
        private void SitFrom(Vector3 before)
        {
            Physics.SyncTransforms();
            _player.Teleport(before, 0f);
            _player.SitAt(_anchor.transform);
            Assert.IsTrue(_player.IsSitting, "座れない");
            Physics.SyncTransforms();
        }

        private void AssertAt(Vector3 expected, string message)
        {
            Vector3 actual = _host.transform.position;
            Assert.Less((actual - expected).magnitude, Tolerance,
                message + "（立った位置 " + actual.ToString("F2") + "、期待 " + expected.ToString("F2") + "）");
        }
    }
}
