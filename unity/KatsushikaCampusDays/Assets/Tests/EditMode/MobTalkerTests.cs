using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// モブに話しかけたときの返事（MobTalker.PickLine）を守る (#22)。
    /// 会釈か、時間帯に合った一言をかぎ括弧で包んだもの。辞書は今の言語のものを使うので、言語は切り替えない。
    /// </summary>
    public sealed class MobTalkerTests
    {
        [Test]
        public void 台詞のキーは時間帯と番号から作る()
        {
            Assert.AreEqual("ui.mob.line.morning.0", MobTalker.LineKey("morning", 0));
            Assert.AreEqual("ui.mob.line.night.1", MobTalker.LineKey("night", 1));
        }

        [Test]
        public void 会釈の割合を引いたら会釈()
        {
            string nod = L.Get("ui.mob.nod");
            Assert.AreNotEqual("ui.mob.nod", nod, "辞書に ui.mob.nod が無い");
            Assert.AreEqual(nod, MobTalker.PickLine("morning", 0f, 0));
            Assert.AreEqual(nod, MobTalker.PickLine("morning", MobTalker.NodChance * 0.99f, 1));
        }

        [TestCase("morning", 0)]
        [TestCase("lunch", 1)]
        [TestCase("afternoon", 0)]
        [TestCase("evening", 1)]
        [TestCase("night", 0)]
        public void それ以外は時間帯の一言(string band, int index)
        {
            string line = L.Get(MobTalker.LineKey(band, index));
            Assert.AreNotEqual(MobTalker.LineKey(band, index), line, "辞書に台詞が無い");

            string said = MobTalker.PickLine(band, 0.9f, index);
            Assert.AreEqual(L.Format("ui.mob.say", line), said);
            StringAssert.Contains(line, said);
        }

        [Test]
        public void 番号が範囲の外でも辞書にある台詞を使う()
        {
            Assert.AreEqual(MobTalker.PickLine("lunch", 0.9f, MobTalker.LinesPerBand - 1), MobTalker.PickLine("lunch", 0.9f, 99));
            Assert.AreEqual(MobTalker.PickLine("lunch", 0.9f, 0), MobTalker.PickLine("lunch", 0.9f, -5));
        }

        [Test]
        public void 時間帯が分からなければ会釈()
        {
            string nod = L.Get("ui.mob.nod");
            Assert.AreEqual(nod, MobTalker.PickLine(null, 0.9f, 0));
            Assert.AreEqual(nod, MobTalker.PickLine(string.Empty, 0.9f, 0));
            Assert.AreEqual(nod, MobTalker.PickLine("midnight", 0.9f, 0), "辞書に無い時間帯は会釈");
        }
    }
}
