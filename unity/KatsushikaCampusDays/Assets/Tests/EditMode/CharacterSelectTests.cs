using System.Linq;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルのキャラクター選択（CharacterSelect）の選び方。開いたときはいまのキャラ（知らない id なら先頭）、
    /// ←→ は端で反対の端へ回り込む。回り込みを外すと、端のキャラを選べない・範囲の外を指して決定できなくなる。
    /// </summary>
    public sealed class CharacterSelectTests
    {
        private static int Count => GameManager.PlayableCharacterIds.Length;

        [Test]
        public void StartIndex_PointsAtTheCurrentCharacter()
        {
            for (int i = 0; i < Count; i++)
            {
                Assert.AreEqual(i, CharacterSelect.StartIndex(GameManager.PlayableCharacterIds[i]),
                    GameManager.PlayableCharacterIds[i] + " で開いたのに別の子が選ばれている");
            }
        }

        [TestCase("no_such_character")]
        [TestCase("")]
        [TestCase(null)]
        public void StartIndex_UnknownCharacter_FallsBackToTheFirst(string id)
        {
            Assert.AreEqual(0, CharacterSelect.StartIndex(id), "知らない id で範囲の外を指している");
        }

        [TestCase(0, 1, 3, 1)]
        [TestCase(1, 1, 3, 2)]
        [TestCase(2, 1, 3, 0)]
        [TestCase(0, -1, 3, 2)]
        [TestCase(1, -1, 3, 0)]
        [TestCase(2, -1, 3, 1)]
        public void Step_WrapsAtBothEnds(int index, int step, int count, int expected)
        {
            Assert.AreEqual(expected, CharacterSelect.Step(index, step, count));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void Step_WithOnlyOneCharacter_StaysPut(int step)
        {
            Assert.AreEqual(0, CharacterSelect.Step(0, step, 1));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void Step_GoingAllTheWayAround_VisitsEveryoneOnceAndComesBack(int step)
        {
            var seen = new bool[Count];
            int index = 0;
            for (int i = 0; i < Count; i++)
            {
                Assert.IsFalse(seen[index], "一周するまでに同じ子を 2 度通った");
                seen[index] = true;
                index = CharacterSelect.Step(index, step, Count);
                Assert.That(index, Is.InRange(0, Count - 1), "範囲の外を指した");
            }

            Assert.AreEqual(0, index, "一周しても最初の子に戻らない");
        }

        [TestCase(0, 0, 3, 0)]
        [TestCase(1, 0, 3, 1)]
        [TestCase(2, 0, 3, -1)]
        [TestCase(0, 2, 3, 1)]
        [TestCase(1, 2, 3, -1)]
        [TestCase(2, 2, 3, 0)]
        public void CarouselSlot_PutsOneNeighbourOnEachSide(int i, int index, int count, int expected)
        {
            Assert.AreEqual(expected, CharacterSelect.CarouselSlot(i, index, count));
        }

        [Test]
        public void CarouselSlot_SelectedIsAlwaysInTheMiddle()
        {
            for (int index = 0; index < Count; index++)
            {
                Assert.AreEqual(0, CharacterSelect.CarouselSlot(index, index, Count));
                var slots = Enumerable.Range(0, Count).Select(i => CharacterSelect.CarouselSlot(i, index, Count));
                Assert.AreEqual(Count, slots.Distinct().Count(), "2 人が同じ場所に重なる");
            }
        }
    }
}
