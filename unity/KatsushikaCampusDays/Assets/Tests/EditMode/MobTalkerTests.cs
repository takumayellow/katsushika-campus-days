using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// モブに話しかけたときの返事（MobTalker.PickLine）と、台詞の選び方（MobScheduler.ChooseLine）を守る (#22)。
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
        public void 会釈は辞書の文()
        {
            string nod = L.Get("ui.mob.nod");
            Assert.AreNotEqual("ui.mob.nod", nod, "辞書に ui.mob.nod が無い");
            Assert.AreEqual(nod, MobTalker.Nod());
        }

        [TestCase("morning", 0)]
        [TestCase("lunch", 1)]
        [TestCase("afternoon", 4)]
        [TestCase("evening", 6)]
        [TestCase("night", 7)]
        public void それ以外は時間帯の一言(string band, int index)
        {
            string line = L.Get(MobTalker.LineKey(band, index));
            Assert.AreNotEqual(MobTalker.LineKey(band, index), line, "辞書に台詞が無い");

            string said = MobTalker.PickLine(band, index);
            Assert.AreEqual(L.Format("ui.mob.say", line), said);
            StringAssert.Contains(line, said);
        }

        [Test]
        public void 番号が範囲の外でも辞書にある台詞を使う()
        {
            Assert.AreEqual(MobTalker.PickLine("lunch", MobTalker.LinesPerBand - 1), MobTalker.PickLine("lunch", 99));
            Assert.AreEqual(MobTalker.PickLine("lunch", 0), MobTalker.PickLine("lunch", -5));
        }

        [Test]
        public void 時間帯が分からなければ会釈()
        {
            string nod = L.Get("ui.mob.nod");
            Assert.AreEqual(nod, MobTalker.PickLine(null, 0));
            Assert.AreEqual(nod, MobTalker.PickLine(string.Empty, 0));
            Assert.AreEqual(nod, MobTalker.PickLine("midnight", 0), "辞書に無い時間帯は会釈");
        }

        [Test]
        public void 時間帯の台詞はどれも違う()
        {
            foreach (string band in new[] { "morning", "lunch", "afternoon", "evening", "night" })
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < MobTalker.LinesPerBand; i++)
                {
                    Assert.IsTrue(seen.Add(MobTalker.PickLine(band, i)), band + " の " + i + " 番が前の台詞と同じ");
                }
            }
        }

        [Test]
        public void 一度言った人と時間帯の分からない人は会釈を返す()
        {
            Assert.IsFalse(MobTalker.RepliesWithNod("lunch", -1), "初めて話しかけた人は一言言う");
            for (int index = 0; index < MobTalker.LinesPerBand; index++)
            {
                Assert.IsTrue(MobTalker.RepliesWithNod("lunch", index), index + " 番を言った人は 2 回目に会釈");
            }

            Assert.IsTrue(MobTalker.RepliesWithNod(null, -1));
            Assert.IsTrue(MobTalker.RepliesWithNod(string.Empty, -1));
        }

        [Test]
        public void 見えている人より台詞が多い()
        {
            Assert.GreaterOrEqual(MobTalker.LinesPerBand, MobScheduler.DefaultDrawnCount);
        }

        [Test]
        public void 台詞は持っている人のいない番号から選ぶ()
        {
            int lines = MobTalker.LinesPerBand;
            var holders = new int[lines];
            var issued = new int[lines];
            int clock = 0;

            // 見えている人が 1 人ずつ話しかけられていくと、毎回まだ誰も言っていない台詞が選ばれる。
            for (int person = 0; person < lines; person++)
            {
                int index = MobScheduler.ChooseLine(holders, issued, person * 5);
                Assert.AreEqual(0, holders[index], person + " 人目が誰かと同じ台詞になった");
                holders[index]++;
                issued[index] = ++clock;
            }

            // 1 人が見えなくなって台詞を手放すと、次の人はその台詞を言う。
            holders[3] = 0;
            Assert.AreEqual(3, MobScheduler.ChooseLine(holders, issued, 0));
        }

        [Test]
        public void 誰も持っていなければ最後に選んでから間のあいた台詞()
        {
            int lines = MobTalker.LinesPerBand;
            var holders = new int[lines];
            var issued = new int[lines];
            for (int i = 0; i < lines; i++)
            {
                issued[i] = i + 1;
            }

            issued[0] = 100;
            Assert.AreEqual(1, MobScheduler.ChooseLine(holders, issued, 0));
            Assert.AreEqual(1, MobScheduler.ChooseLine(holders, issued, 6), "間のあき方が start の順より先");

            System.Array.Clear(issued, 0, lines);
            Assert.AreEqual(5, MobScheduler.ChooseLine(holders, issued, 5), "すべて並んだら start から");
            Assert.AreEqual(5 % lines, MobScheduler.ChooseLine(holders, issued, 5 + lines));
        }
    }
}
