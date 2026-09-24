using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 画質の段が Low のとき, 遠い木のまとまりを描かない判定 (#70)。距離はまとまりの箱の一番近い点から測るので,
    /// 立っているマスや隣のマスが消えることはない。
    /// </summary>
    public sealed class TreeChunkDistanceTests
    {
        private static readonly Bounds Cell = new Bounds(new Vector3(64f, 10f, 64f), new Vector3(128f, 20f, 128f));

        [Test]
        public void IsBeyond_NoLimit_NeverHides()
        {
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(5000f, 0f, 0f), 0f));
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(5000f, 0f, 0f), -1f));
        }

        [Test]
        public void IsBeyond_InsideTheCell_NeverHides()
        {
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(64f, 1.6f, 64f), 1f));
        }

        [Test]
        public void IsBeyond_MeasuresFromTheNearestEdgeOfTheCell()
        {
            // 箱の端（x = 128）から 300 m ちょうどまでは描き, それより遠いと描かない。
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(428f, 10f, 64f), 300f), "端から 300 m");
            Assert.IsTrue(TreeChunkDistance.IsBeyond(Cell, new Vector3(428.5f, 10f, 64f), 300f), "端から 300.5 m");
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(428.5f, 10f, 64f), 301f), "距離を延ばせば描く");
        }

        [Test]
        public void IsBeyond_CountsHeightToo()
        {
            // カメラが高く上がっても（写真モードなど）, 箱からの距離で決める。
            Assert.IsTrue(TreeChunkDistance.IsBeyond(Cell, new Vector3(64f, 400f, 64f), 300f));
            Assert.IsFalse(TreeChunkDistance.IsBeyond(Cell, new Vector3(64f, 300f, 64f), 300f));
        }
    }
}
