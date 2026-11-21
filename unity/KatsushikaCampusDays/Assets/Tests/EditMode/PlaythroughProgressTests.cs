using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 通しプレイで、進んだかを測る基準の残りの道のり（<see cref="PlaythroughDirector.RaisedBest"/>）。
    /// 引き上げ続けると止まっていても進んだことになり、ワープも探し直しも起きなくなる。
    /// </summary>
    public sealed class PlaythroughProgressTests
    {
        [Test]
        public void RaisesUpToTheLimitThenKeepsBest()
        {
            var raises = 0;
            var best = 10f;
            for (var i = 0; i < PlaythroughDirector.MaxRouteRaises; i++)
            {
                best = PlaythroughDirector.RaisedBest(best, best + 1f, ref raises);
            }

            Assert.AreEqual(10f + PlaythroughDirector.MaxRouteRaises, best, 1e-4f);
            Assert.AreEqual(PlaythroughDirector.MaxRouteRaises, raises);

            var after = PlaythroughDirector.RaisedBest(best, best + 5f, ref raises);
            Assert.AreEqual(best, after, 1e-4f);
            Assert.AreEqual(PlaythroughDirector.MaxRouteRaises, raises);
        }

        [Test]
        public void ShorterOrEqualRouteDoesNotCount()
        {
            var raises = 0;
            Assert.AreEqual(10f, PlaythroughDirector.RaisedBest(10f, 8f, ref raises), 1e-4f);
            Assert.AreEqual(10f, PlaythroughDirector.RaisedBest(10f, 10f, ref raises), 1e-4f);
            Assert.AreEqual(0, raises);
        }

        [Test]
        public void ShortEndBehindAWallIsRejected()
        {
            // 研究室の間仕切り（高さ 3m）の手前で終わった道。目的地は壁の向こう 2.8m。
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = new Vector3(1.4f, 1.5f, 0f);
                wall.transform.localScale = new Vector3(0.2f, 3f, 6f);
                Physics.SyncTransforms();
                Assert.IsTrue(PlaythroughDirector.EndsShortBehindWall(Vector3.zero, new Vector3(2.8f, 0.9f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void ShortEndAcrossALowCounterIsKept()
        {
            // レジの台（高さ 1m）越しに 2m 先の商品。机より高い所は開いているので、その道は使う。
            GameObject counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                counter.transform.position = new Vector3(1f, 0.5f, 0f);
                counter.transform.localScale = new Vector3(0.6f, 1f, 2f);
                Physics.SyncTransforms();
                Assert.IsFalse(PlaythroughDirector.EndsShortBehindWall(Vector3.zero, new Vector3(2f, 1.05f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(counter);
            }
        }

        [Test]
        public void EndNearTheGoalIsNotChecked()
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = new Vector3(0.7f, 1.5f, 0f);
                wall.transform.localScale = new Vector3(0.2f, 3f, 6f);
                Physics.SyncTransforms();
                Assert.IsFalse(PlaythroughDirector.EndsShortBehindWall(Vector3.zero, new Vector3(1.2f, 0.9f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }
    }
}
