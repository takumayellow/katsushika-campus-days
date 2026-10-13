using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// エスカレーターに立つと、何も押さなくても上の階まで運ばれ、降り口の平場に送り出される。
    /// 講義棟の 1F→2F の区間と同じ寸法（水平 8.0 m、高さ 4.6 m）の斜めの板と、上下の床の箱で見る。
    /// 段の部品は斜めの板に沿って 1 段ぶんずつ動き、元の位置から 1 段より遠くへ行かない。
    /// </summary>
    public sealed class EscalatorPlayTests
    {
        private static readonly Vector3 Top = new Vector3(8f, 4.6f, 0f);

        /// <summary>待ち時間を縮めるため、定格の 4 倍で回す（段の線の長さ 9.2 m を 4.6 秒）。</summary>
        private const float Speed = Escalator.DefaultSpeed * 4f;

        private GameObject _world;
        private GameObject _host;
        private Escalator _escalator;
        private Transform _steps;
        private bool _lockedBefore;

        [SetUp]
        public void SetUp()
        {
            _lockedBefore = KCDInput.MovementLocked;
            _world = new GameObject("EscalatorPlayTests");
            AddFloor(new Vector3(-3f, 0f, 0f), 6f);
            AddFloor(new Vector3(Top.x + 3f, Top.y, 0f), 6f);

            var flight = new GameObject("Escalator");
            flight.transform.SetParent(_world.transform, false);
            _steps = new GameObject("escsteps_test_flight").transform;
            _steps.SetParent(_world.transform, false);
            _escalator = flight.AddComponent<Escalator>();
            _escalator.Configure(Top, 23, _steps, Speed);
            _escalator.AddRamp(-1);

            _host = new GameObject("EscalatorRider");
            int layer = LayerMask.NameToLayer("Player");
            Assert.GreaterOrEqual(layer, 0, "Player レイヤーが無い");
            _host.layer = layer;
            var controller = _host.AddComponent<CharacterController>();
            controller.height = 1.6f;
            controller.radius = 0.28f;
            controller.center = new Vector3(0f, 0.82f, 0f);
            _host.AddComponent<PlayerController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_world);
            KCDInput.MovementLocked = _lockedBefore;
        }

        [UnityTest]
        public IEnumerator StandingOnTheFirstStep_RidesUpToTheNextFloor()
        {
            _host.GetComponent<PlayerController>().Teleport(new Vector3(0.3f, 0.2f, 0f), 90f);
            Physics.SyncTransforms();
            // 段の線の長さ 9.2 m を 2 m/s で 4.6 秒。降り口の先へ送り出されるまで待つ。
            float until = Time.time + 10f;
            while (Time.time < until && _host.transform.position.x < Top.x + 0.2f)
            {
                yield return null;
            }

            Vector3 at = _host.transform.position;
            Assert.Greater(at.x, Top.x + 0.2f, "上の端まで運ばれない（今 " + at.ToString("F2") + "）");
            Assert.AreEqual(Top.y, at.y, 0.15f, "上の階の床に立っていない（今 " + at.ToString("F2") + "）");
            Assert.AreEqual(0f, at.z, 0.05f, "横へずれた");
        }

        [UnityTest]
        public IEnumerator Steps_MoveAlongTheFlightWithinOneStep()
        {
            Vector3 step = Top / 23f;
            float farthest = 0f;
            float until = Time.time + 1f;
            while (Time.time < until)
            {
                Vector3 offset = _steps.localPosition;
                Vector3 across = offset - step * (Vector3.Dot(offset, step) / step.sqrMagnitude);
                Assert.Less(across.magnitude, 1e-3f, "段が斜めの板の向きから外れて動く");
                Assert.LessOrEqual(offset.magnitude, step.magnitude + 1e-3f, "段が 1 段より先へ動く");
                farthest = Mathf.Max(farthest, offset.magnitude);
                yield return null;
            }

            Assert.Greater(farthest, step.magnitude * 0.3f, "段が動かない");
        }

        [UnityTest]
        public IEnumerator WhereTwoFlightsOverlap_TheOneUnderTheFeetCarries()
        {
            // 上の端の先（降り口の平場）に、逆向きに上る次の区間の乗り口が重なる。先に登録された方ではなく、
            // 段の線の高さが足に近い方（いま降りた区間）が運ぶ。
            _escalator.enabled = false;
            var next = new GameObject("EscalatorNext");
            next.transform.SetParent(_world.transform, false);
            next.transform.position = new Vector3(Top.x + 0.6f, Top.y, 0f);
            next.AddComponent<Escalator>().Configure(new Vector3(-8f, 4.6f, 0f), 23, null, Speed);
            _escalator.enabled = true;
            yield return null;

            Vector3 v = Escalator.CarryAt(new Vector3(Top.x + 0.3f, Top.y, 0f));
            Assert.Greater(v.x, 0f, "降り口の先で、次の区間の向き（−x）へ押し戻される");
        }

        private void AddFloor(Vector3 topCenter, float length)
        {
            var floor = new GameObject("Floor");
            floor.transform.SetParent(_world.transform, false);
            floor.transform.position = topCenter - Vector3.up * 0.25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(length, 0.5f, 4f);
        }
    }
}
