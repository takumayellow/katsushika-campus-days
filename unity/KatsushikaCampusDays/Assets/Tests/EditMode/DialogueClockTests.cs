using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 会話のあいだは時計を止めることと、会話でフラグを立てる相手を探せること（#410）。
    /// </summary>
    public sealed class DialogueClockTests
    {
        private const string BoardJson =
            "{\"id\":\"board\",\"topics\":[" +
            "{\"id\":\"t_read\",\"setsFlag\":\"f_read\",\"lines\":[\"読んだ\"]}," +
            "{\"id\":\"t_write\",\"lines\":[\"書く？\"],\"choices\":[" +
            "{\"text\":\"書く\",\"setsFlag\":\"f_wrote\"},{\"text\":\"やめる\"}]}]}";

        private static DialogueData Board()
        {
            return DialogueData.FromJson(MiniJson.Deserialize(BoardJson) as Dictionary<string, object>);
        }

        [Test]
        public void RaisesFlag_FindsTopicAndChoiceFlags()
        {
            DialogueData board = Board();

            Assert.IsTrue(board.RaisesFlag("f_read"), "話題の終わりで立つフラグ");
            Assert.IsTrue(board.RaisesFlag("f_wrote"), "選択肢で立つフラグ");
            Assert.IsFalse(board.RaisesFlag("f_other"));
            Assert.IsFalse(board.RaisesFlag(string.Empty));
            Assert.IsFalse(board.RaisesFlag(null));
        }

        [Test]
        public void Clock_AdvancesOnlyWhenNotPausedNotTalkingAndDayHasLength()
        {
            Assert.IsTrue(DayNightCycle.Advances(false, false, 720f));
            Assert.IsFalse(DayNightCycle.Advances(false, true, 720f), "会話中は止める");
            Assert.IsFalse(DayNightCycle.Advances(true, false, 720f), "止めてあれば止める");
            Assert.IsFalse(DayNightCycle.Advances(false, false, 0f), "一日の長さが無ければ止める");
        }
    }
}
