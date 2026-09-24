using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// モブの枠の帳簿（MobPool）を守る (#22)。シーンに焼き込んだ人数と同時に出す上限を超えて貸さないこと、
    /// 返した枠は冷めるまで貸さないこと。実行中に Instantiate / Destroy をしないための前提。
    /// </summary>
    public sealed class MobPoolTests
    {
        [Test]
        public void 上限まで貸したらそれ以上は貸さない()
        {
            var pool = new MobPool(MobScheduler.MaxActive, MobScheduler.MaxActive);
            var slots = new HashSet<int>();
            for (int i = 0; i < MobScheduler.MaxActive; i++)
            {
                int slot = pool.Acquire(0f);
                Assert.GreaterOrEqual(slot, 0, i + " 人目を貸せない");
                Assert.IsTrue(slots.Add(slot), "同じ枠を 2 回貸した: " + slot);
            }

            Assert.AreEqual(MobScheduler.MaxActive, pool.ActiveCount);
            Assert.AreEqual(-1, pool.Acquire(0f), "上限を超えて貸した");
            Assert.AreEqual(MobScheduler.MaxActive, pool.ActiveCount);
        }

        [Test]
        public void 同時に出す上限が枠の数より小さければ上限で止まる()
        {
            var pool = new MobPool(24, 8);
            for (int i = 0; i < 8; i++)
            {
                Assert.GreaterOrEqual(pool.Acquire(0f), 0);
            }

            Assert.AreEqual(-1, pool.Acquire(0f));
            Assert.AreEqual(8, pool.ActiveCount);
        }

        [Test]
        public void 上限は枠の数で切る()
        {
            var pool = new MobPool(4, 24);
            Assert.AreEqual(4, pool.Capacity);
            Assert.AreEqual(4, pool.MaxActive);
            for (int i = 0; i < 4; i++)
            {
                Assert.GreaterOrEqual(pool.Acquire(0f), 0);
            }

            Assert.AreEqual(-1, pool.Acquire(0f));
        }

        [Test]
        public void 枠が無ければ誰も貸さない()
        {
            var pool = new MobPool(0, MobScheduler.MaxActive);
            Assert.AreEqual(0, pool.MaxActive);
            Assert.AreEqual(-1, pool.Acquire(0f));
        }

        [Test]
        public void 返した枠は冷めるまで貸さない()
        {
            var pool = new MobPool(1, 1);
            int slot = pool.Acquire(0f);
            Assert.AreEqual(0, slot);

            Assert.IsTrue(pool.Release(slot, 10f, 6f));
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.IsFalse(pool.IsActive(slot));

            Assert.AreEqual(-1, pool.Acquire(15.9f), "冷める前に貸した");
            Assert.AreEqual(slot, pool.Acquire(16f), "冷めたのに貸さない");
        }

        [Test]
        public void 冷めている枠があればそちらを貸す()
        {
            var pool = new MobPool(2, 2);
            int a = pool.Acquire(0f);
            int b = pool.Acquire(0f);
            pool.Release(a, 0f, 100f);
            pool.Release(b, 0f, 0f);

            Assert.AreEqual(b, pool.Acquire(1f));
            Assert.AreEqual(-1, pool.Acquire(1f));
        }

        [Test]
        public void 借りていない枠は返せない()
        {
            var pool = new MobPool(4, 4);
            Assert.IsFalse(pool.Release(0, 0f, 0f));
            Assert.IsFalse(pool.Release(-1, 0f, 0f));
            Assert.IsFalse(pool.Release(4, 0f, 0f));

            int slot = pool.Acquire(0f);
            Assert.IsTrue(pool.Release(slot, 0f, 0f));
            Assert.IsFalse(pool.Release(slot, 0f, 0f), "同じ枠を 2 回返せた");
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void 返しては借りを繰り返しても数が狂わない()
        {
            var pool = new MobPool(MobScheduler.MaxActive, MobScheduler.MaxActive);
            var held = new List<int>();
            float now = 0f;
            for (int step = 0; step < 500; step++)
            {
                now += 0.5f;
                if (step % 3 == 2 && held.Count > 0)
                {
                    int slot = held[0];
                    held.RemoveAt(0);
                    Assert.IsTrue(pool.Release(slot, now, 2f));
                }
                else
                {
                    int slot = pool.Acquire(now);
                    if (slot >= 0)
                    {
                        CollectionAssert.DoesNotContain(held, slot, "貸し出し中の枠をもう一度貸した");
                        held.Add(slot);
                    }
                }

                Assert.AreEqual(held.Count, pool.ActiveCount);
                Assert.LessOrEqual(pool.ActiveCount, MobScheduler.MaxActive);
            }
        }
    }
}
