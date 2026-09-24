using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// モブがプレイヤーの行く手をふさがないことを守る (#22)。
    /// MobWalker.AvoidanceStep は 1 フレームぶんのよける量を返す純粋な関数。
    /// 内側に入られたら離れ、前方にいれば反対側へ横にずれ、関係ない位置なら動かない。
    /// </summary>
    public sealed class MobWalkerTests
    {
        private const float Radius = 1.2f;
        private const float Speed = 1.4f;
        private const float Dt = 1f / 60f;

        private static readonly Vector3 North = Vector3.forward;

        [Test]
        public void 内側に入られたらプレイヤーから離れる()
        {
            var self = new Vector3(0f, 0f, 0f);
            var player = new Vector3(0.5f, 0f, 0f);
            Vector3 step = MobWalker.AvoidanceStep(self, North, player, Radius, Speed, Dt);

            Assert.Less(step.x, 0f, "プレイヤーと反対（西）へ動かない");
            Assert.AreEqual(0f, step.y, 1e-6f, "上下には動かさない");
            Assert.LessOrEqual(step.magnitude, Speed * Dt + 1e-6f, "1 フレームで歩く速さより大きく動いた");
            Assert.Greater(Vector3.Distance(self + step, player), Vector3.Distance(self, player));
        }

        [Test]
        public void 重なっていても進む向きの横へ逃げる()
        {
            Vector3 step = MobWalker.AvoidanceStep(Vector3.zero, North, Vector3.zero, Radius, Speed, Dt);
            Assert.Greater(step.magnitude, 0f, "真上に重なると動かない");
            Assert.AreEqual(0f, step.z, 1e-6f, "横ではなく前後に動いた");
        }

        [Test]
        public void 前にいるプレイヤーは反対側へ横によける()
        {
            // 北へ歩いていて、2 m 先のやや東にプレイヤー → 西へずれる。
            Vector3 step = MobWalker.AvoidanceStep(Vector3.zero, North, new Vector3(0.3f, 0f, 2f), Radius, Speed, Dt);
            Assert.Less(step.x, 0f);
            Assert.AreEqual(0f, step.z, 1e-6f, "よけるときに前後へは動かさない");

            // やや西にいれば東へ。
            step = MobWalker.AvoidanceStep(Vector3.zero, North, new Vector3(-0.3f, 0f, 2f), Radius, Speed, Dt);
            Assert.Greater(step.x, 0f);
        }

        [TestCase(0f, 0f, -2f)]
        [TestCase(0f, 0f, 5f)]
        [TestCase(2f, 0f, 2f)]
        public void 後ろ_遠く_横に離れたプレイヤーはよけない(float x, float y, float z)
        {
            Vector3 step = MobWalker.AvoidanceStep(Vector3.zero, North, new Vector3(x, y, z), Radius, Speed, Dt);
            Assert.AreEqual(Vector3.zero, step);
        }

        [Test]
        public void 高さの差は数えない()
        {
            Vector3 flat = MobWalker.AvoidanceStep(Vector3.zero, North, new Vector3(0.5f, 0f, 0f), Radius, Speed, Dt);
            Vector3 raised = MobWalker.AvoidanceStep(Vector3.zero, North, new Vector3(0.5f, 1.5f, 0f), Radius, Speed, Dt);
            Assert.Less(Vector3.Distance(flat, raised), 1e-6f);
        }

        [Test]
        public void 止まっているフレームや半径が無ければ動かない()
        {
            var player = new Vector3(0.5f, 0f, 0f);
            Assert.AreEqual(Vector3.zero, MobWalker.AvoidanceStep(Vector3.zero, North, player, Radius, Speed, 0f));
            Assert.AreEqual(Vector3.zero, MobWalker.AvoidanceStep(Vector3.zero, North, player, 0f, Speed, Dt));
            Assert.AreEqual(Vector3.zero, MobWalker.AvoidanceStep(Vector3.zero, North, player, Radius, 0f, Dt));
        }

        [Test]
        public void 他の_NPC_より道を譲る()
        {
            // NavMeshAgent の avoidancePriority は数が大きいほど譲る側。名前のある NPC は Unity の既定の 50 のまま。
            Assert.Greater(MobWalker.AvoidancePriority, 50);
        }
    }
}
