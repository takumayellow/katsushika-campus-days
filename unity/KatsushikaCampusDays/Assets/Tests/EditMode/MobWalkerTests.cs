using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// モブがプレイヤーの行く手をふさがないことを守る (#22)。
    /// MobWalker.AvoidanceStep は 1 フレームぶんのよける量を返す純粋な関数。
    /// 内側に入られたら離れ、前方にいれば反対側へ横にずれ、関係ない位置なら動かない。
    /// MobWalker.StandSpot は立ち止まる点のまわりの輪から 1 点を選ぶ純粋な関数 (#165)。
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
        public void 立ち止まる所は点のまわりの輪に散らす()
        {
            var center = new Vector3(10f, 2f, -5f);
            for (int i = 0; i <= 8; i++)
            {
                for (int j = 0; j <= 4; j++)
                {
                    Vector3 spot = MobWalker.StandSpot(center, i / 8f, j / 4f);
                    float distance = Vector3.Distance(spot, center);
                    Assert.GreaterOrEqual(distance, MobWalker.StandSpreadMin - 1e-4f, "点に近すぎて重なる");
                    Assert.LessOrEqual(distance, MobWalker.StandSpreadMax + 1e-4f, "点から離れすぎる");
                    Assert.AreEqual(center.y, spot.y, 1e-6f, "高さを変えた");
                }
            }
        }

        [Test]
        public void 立ち止まる所は向きと離れ方で変わる()
        {
            Vector3 a = MobWalker.StandSpot(Vector3.zero, 0f, 0.5f);
            Vector3 b = MobWalker.StandSpot(Vector3.zero, 0.5f, 0.5f);
            Vector3 c = MobWalker.StandSpot(Vector3.zero, 0f, 1f);
            Assert.Greater(Vector3.Distance(a, b), 2f * MobWalker.StandSpreadMin, "向きが違っても同じ所に立つ");
            Assert.Greater(c.magnitude, a.magnitude, "離れ方を大きくしても遠くならない");
            Assert.AreEqual(MobWalker.StandSpreadMax, c.magnitude, 1e-4f);
            Assert.AreEqual(MobWalker.StandSpreadMin, MobWalker.StandSpot(Vector3.zero, 0.3f, 0f).magnitude, 1e-4f);
        }

        [Test]
        public void 立ち止まる所は輪の内と外に同じ数だけ散る()
        {
            // radius01 = 0.5 の円は輪を同じ面積の内と外に分ける。半径をそのまま割り振ると内側に詰まる。
            float min = MobWalker.StandSpreadMin;
            float max = MobWalker.StandSpreadMax;
            float r = MobWalker.StandSpot(Vector3.zero, 0f, 0.5f).magnitude;
            Assert.AreEqual(r * r - min * min, max * max - r * r, 1e-3f, "内と外の面積が違う");
        }

        [Test]
        public void 立ち止まる輪は着いたとみなす距離より広い()
        {
            // 輪が着いたとみなす距離より狭いと、ずらしても同じ点に集まって見える。
            Assert.Greater(MobWalker.StandSpreadMin, MobWalker.ArriveDistance);
            Assert.Greater(MobWalker.StandSpreadMax, MobWalker.StandSpreadMin);
        }

        [Test]
        public void 立っているときの速さは枠ごとに違う()
        {
            int slots = MobScheduler.MaxActive;
            for (int a = 0; a < slots; a++)
            {
                float tempo = MobWalker.IdleTempoFor(a);
                Assert.That(tempo, Is.InRange(MobWalker.IdleTempoMin, MobWalker.IdleTempoMax), "枠 " + a);
                for (int b = a + 1; b < slots; b++)
                {
                    Assert.Greater(Mathf.Abs(tempo - MobWalker.IdleTempoFor(b)), 0.01f, "枠 " + a + " と " + b + " の速さが近い");
                }
            }
        }

        [Test]
        public void 続けて貸す枠どうしは速さが大きく違う()
        {
            // MobPool は枠を順に貸すので、隣り合う枠の人は同じ入口から続けて出て並びやすい。
            int slots = MobScheduler.MaxActive;
            for (int a = 0; a < slots; a++)
            {
                float gap = Mathf.Abs(MobWalker.IdleTempoFor(a) - MobWalker.IdleTempoFor((a + 1) % slots));
                Assert.Greater(gap, 0.05f, "枠 " + a + " と次の枠");
            }
        }

        [Test]
        public void 枠ごとの速さは止まっているときだけ掛ける()
        {
            Assert.AreEqual(0.9f, MobWalker.ApplyTempo(1f, 0f, 0.9f), 1e-5f);
            Assert.AreEqual(1.6f, MobWalker.ApplyTempo(1.6f, MobWalker.TempoFadeSpeed, 0.9f), 1e-5f, "歩いているときは足の運びのまま");
            Assert.AreEqual(1.6f, MobWalker.ApplyTempo(1.6f, 1.4f, 1.1f), 1e-5f);
            float half = MobWalker.ApplyTempo(1f, MobWalker.TempoFadeSpeed * 0.5f, 0.9f);
            Assert.That(half, Is.InRange(0.9f, 1f), "途中はなめらかに 1 へ寄る");
        }

        [Test]
        public void 他の_NPC_より道を譲る()
        {
            // NavMeshAgent の avoidancePriority は数が大きいほど譲る側。名前のある NPC は Unity の既定の 50 のまま。
            Assert.Greater(MobWalker.AvoidancePriority, 50);
        }
    }
}
