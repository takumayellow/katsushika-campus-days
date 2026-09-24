using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// ポーズメニュー（PauseMenu）の Esc の門とカーソル。写真モード・モーダルを閉じたフレーム・結果画面・寮の締め・
    /// ポーズから開いた設定パネルの間は Esc でポーズを開閉しないこと、閉じているポーズはクエストログに Esc を譲ること、
    /// 上下のカーソルが端で回り込むこと。開閉そのもの（時間停止と封鎖）は GameplayBlockTests が見る。
    /// </summary>
    public sealed class PauseMenuTests
    {
        // ---- Esc を見ない条件 ----

        [Test]
        public void IgnoresMenuKey_NothingInTheWay_ListensToEsc()
        {
            Assert.IsFalse(PauseMenu.IgnoresMenuKey(false, false, false, false, false),
                "何も出ていないのに Esc でポーズが開かない");
        }

        [TestCase(true, false, false, false, false, "写真モードの間に Esc でポーズが開閉した")]
        [TestCase(false, true, false, false, false, "モーダルを Esc で閉じた同じフレームでポーズが開いた")]
        [TestCase(false, false, true, false, false, "結果画面の上でポーズが開いた")]
        [TestCase(false, false, false, true, false, "寮の締めの間にポーズが開いた（閉じると時間が動き出す）")]
        [TestCase(false, false, false, false, true, "設定パネルの間に Esc でポーズ本体が開閉した")]
        [TestCase(true, true, true, true, true, "全部重なっているのに Esc を見た")]
        public void IgnoresMenuKey_AnyOfThem_LeavesThePauseAlone(bool photoMode, bool modalClosedThisFrame,
            bool resultOpen, bool dormEndingShowing, bool settingsOpen, string reason)
        {
            Assert.IsTrue(PauseMenu.IgnoresMenuKey(photoMode, modalClosedThisFrame, resultOpen, dormEndingShowing,
                settingsOpen), reason);
        }

        // ---- クエストログに Esc を譲るか ----

        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        [TestCase(false, true, true, true)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, false)]
        public void QuestLogKeepsTheMenuKey_OnlyWhileThePauseIsClosed(bool pauseOpen, bool questLogOpen,
            bool questLogClosedThisFrame, bool expected)
        {
            // ログを Esc で閉じたフレームに譲らないと、同じ Esc でポーズが開く。
            // ポーズが開いているのに譲ると、Esc でポーズを閉じられなくなる。
            Assert.AreEqual(expected,
                PauseMenu.QuestLogKeepsTheMenuKey(pauseOpen, questLogOpen, questLogClosedThisFrame),
                "pause=" + pauseOpen + " log=" + questLogOpen + " closedThisFrame=" + questLogClosedThisFrame);
        }

        // ---- カーソル ----

        [Test]
        public void EntryCount_MatchesTheFiveActions()
        {
            // 再開 / セーブ / ロード / 設定 / タイトルへ。項目を足すなら Execute の分岐も足す。
            Assert.AreEqual(5, PauseMenu.EntryCount);
        }

        [TestCase(0, 1, 1)]
        [TestCase(2, 1, 3)]
        [TestCase(2, -1, 1)]
        [TestCase(0, -1, 4)]
        [TestCase(4, 1, 0)]
        public void MoveCursor_WrapsAtBothEnds(int index, int step, int expected)
        {
            Assert.AreEqual(expected, PauseMenu.MoveCursor(index, step));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void MoveCursor_FullLoop_VisitsEveryEntryOnce(int step)
        {
            var seen = new HashSet<int>();
            int index = 0;
            for (int i = 0; i < PauseMenu.EntryCount; i++)
            {
                Assert.That(index, Is.InRange(0, PauseMenu.EntryCount - 1), "カーソルが項目の外に出た");
                Assert.IsTrue(seen.Add(index), index + " に二度止まった");
                index = PauseMenu.MoveCursor(index, step);
            }

            Assert.AreEqual(0, index, "一周しても最初の項目に戻らない");
            Assert.AreEqual(PauseMenu.EntryCount, seen.Count);
        }
    }
}
