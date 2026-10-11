using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 本編のエンディング (#176) の純関数と、エンディングが前提にしている実データ。
    /// 画面を開いてページを送る流れは PlayMode の FormalEndingPlayTests が見る。
    /// </summary>
    public sealed class FormalEndingTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        // ---- いつ出すか ----

        [Test]
        public void ShouldPlay_OnlyForTheSunsetQuestAndOnlyOnce()
        {
            Assert.IsTrue(FormalEnding.ShouldPlay(false, FormalEnding.SunsetQuestId));
            Assert.IsFalse(FormalEnding.ShouldPlay(true, FormalEnding.SunsetQuestId), "二度目は出さない");
            Assert.IsFalse(FormalEnding.ShouldPlay(false, "q_park"), "夕日でないクエストで出た");
            Assert.IsFalse(FormalEnding.ShouldPlay(false, "q_sq_night_walk"), "夕日のあとのサブクエストで出た");
            Assert.IsFalse(FormalEnding.ShouldPlay(false, null));
            Assert.IsFalse(FormalEnding.ShouldPlay(false, string.Empty));
        }

        [Test]
        public void ShouldStartDialogue_WaitsForTheDelayAndForOtherDialogue()
        {
            Assert.IsFalse(FormalEnding.ShouldStartDialogue(0f, false), "達成した瞬間に始めた");
            Assert.IsFalse(FormalEnding.ShouldStartDialogue(FormalEnding.DialogueDelaySeconds - 0.01f, false));
            Assert.IsTrue(FormalEnding.ShouldStartDialogue(FormalEnding.DialogueDelaySeconds, false), "境目で始めない");
            Assert.IsFalse(FormalEnding.ShouldStartDialogue(99f, true), "ほかの会話の途中で始めた");
        }

        [Test]
        public void ShouldAdvance_IgnoresInputRightAfterAPageOpens()
        {
            float guard = FormalEnding.InputGuardSeconds;
            Assert.Greater(guard, 0f, "会話を送った Enter でそのままページを送ってしまう");
            Assert.IsFalse(FormalEnding.ShouldAdvance(true, 0f, true), "出した瞬間に送った");
            Assert.IsFalse(FormalEnding.ShouldAdvance(true, guard - 0.01f, true));
            Assert.IsTrue(FormalEnding.ShouldAdvance(true, guard, true), "境目で受け付けない");
            Assert.IsFalse(FormalEnding.ShouldAdvance(false, 99f, true), "出していないのに送った");
            Assert.IsFalse(FormalEnding.ShouldAdvance(true, 99f, false), "押していないのに送った");
        }

        // ---- 文面 ----

        [TestCase(false)]
        [TestCase(true)]
        public void HeadingText_EveryPageHasItsOwnHeading(bool english)
        {
            var headings = new HashSet<string>();
            for (int page = 0; page < FormalEnding.PageCount; page++)
            {
                string heading = FormalEnding.HeadingText(page, english);
                Assert.IsNotEmpty(heading);
                Assert.IsTrue(headings.Add(heading), "見出しが重なっている: " + heading);
            }

            Assert.AreEqual(FormalEnding.HeadingText(FormalEnding.PageCount - 1, english),
                FormalEnding.HeadingText(FormalEnding.PageCount + 3, english), "範囲の外が最後のページに寄らない");
        }

        [Test]
        public void ChoiceText_CountsPagesAndTheLastOneReturnsToCampus()
        {
            int last = FormalEnding.PageCount - 1;
            StringAssert.Contains("(1/" + FormalEnding.PageCount + ")", FormalEnding.ChoiceText(0, false));
            StringAssert.Contains("次へ", FormalEnding.ChoiceText(0, false));
            StringAssert.Contains("(" + FormalEnding.PageCount + "/" + FormalEnding.PageCount + ")",
                FormalEnding.ChoiceText(last, false));
            StringAssert.Contains("キャンパスに戻る", FormalEnding.ChoiceText(last, false));
            StringAssert.Contains("back to campus", FormalEnding.ChoiceText(last, true));
            StringAssert.Contains("next", FormalEnding.ChoiceText(0, true));
        }

        private static ResultData Result(int quests, int collectibles, int photos, int buildings)
        {
            return new ResultData
            {
                Quests = quests, QuestTotal = 15,
                Collectibles = collectibles, CollectibleTotal = 20,
                Photos = photos, PhotoTotal = 6,
                Buildings = buildings, BuildingTotal = 9
            };
        }

        [Test]
        public void RecapText_ShowsTheSameCountsAsTheDayEndResult()
        {
            string ja = FormalEnding.RecapText(2, Result(8, 7, 3, 4), false);
            StringAssert.Contains("2 日目", ja);
            StringAssert.Contains("クエスト  8 / 15", ja);
            StringAssert.Contains("隠しアイテム  7 / 20", ja);
            StringAssert.Contains("写真  3 / 6", ja);
            StringAssert.Contains("入った建物  4 / 9", ja);

            string en = FormalEnding.RecapText(1, Result(8, 0, 0, 4), true);
            StringAssert.Contains("day 1", en);
            StringAssert.Contains("Quests  8 / 15", en);
            StringAssert.Contains("Buildings entered  4 / 9", en);
        }

        [Test]
        public void RecapText_ClampsNonsenseInput()
        {
            string text = FormalEnding.RecapText(0, Result(-4, -1, 99, -3), false);
            StringAssert.Contains("1 日目", text, "0 日目を出した");
            StringAssert.Contains("入った建物  0 / 9", text);
            StringAssert.Contains("写真  6 / 6", text, "総数で頭打ちにならない");
            StringAssert.DoesNotContain("-", text, "負の数が出た");
            StringAssert.Contains("1 日目", FormalEnding.RecapText(1, null, false), "リザルトが無いと落ちる");
        }

        [Test]
        public void PeopleText_ListsMetPeopleInAFixedOrder()
        {
            string text = FormalEnding.PeopleText(new[] { "sora", "inari" }, false, id => "<" + id + ">");
            int inari = text.IndexOf("<inari>", System.StringComparison.Ordinal);
            int sora = text.IndexOf("<sora>", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(inari, 0, text);
            Assert.Greater(sora, inari, "話した順でなく決まった順に並べる: " + text);
            StringAssert.DoesNotContain("<kaname>", text, "話していない人が出た");
        }

        [Test]
        public void PeopleText_CountsTheLectureProfessorAsTheProfessor()
        {
            string lectureOnly = FormalEnding.PeopleText(new[] { "lecture" }, false, id => "<" + id + ">");
            StringAssert.Contains("<prof>", lectureOnly, "講義の教授としか話していないのに教授が出ない");
            StringAssert.DoesNotContain("<lecture>", lectureOnly);

            string both = FormalEnding.PeopleText(new[] { "prof", "lecture" }, false, id => "<" + id + ">");
            Assert.AreEqual(1, CountOf(both, "<prof>"), "同じ教授を 2 回数えた: " + both);
        }

        [Test]
        public void PeopleText_SkipsThingsThatAreNotPeople()
        {
            // 拾ったメモや手紙の会話データは人ではないので並べない。
            string text = FormalEnding.PeopleText(new[] { "note_dome", "letter" }, false, id => "<" + id + ">");
            Assert.AreEqual("今日は誰とも話さなかった。", text);
            Assert.AreEqual("You did not talk to anyone today.", FormalEnding.PeopleText(null, true, null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PersonLine_EveryPersonHasTheirOwnLine(bool english)
        {
            var lines = new HashSet<string>();
            foreach (string id in FormalEnding.PeopleIds)
            {
                string line = FormalEnding.PersonLine(id, english);
                Assert.IsNotEmpty(line, id + " の一言が無い");
                Assert.IsTrue(lines.Add(line), id + " の一言がほかの人と同じ: " + line);
            }

            Assert.IsEmpty(FormalEnding.PersonLine("lecture", english), "並べない id に一言が付いた");
        }

        [Test]
        public void PeopleIds_AreRealDialogueData()
        {
            foreach (string id in FormalEnding.PeopleIds)
            {
                Assert.IsTrue(File.Exists(Path.Combine(DataRoot, "Dialogue", id + ".json")), id + " の会話データが無い");
            }
        }

        // ---- 前提にしている実データ ----

        [Test]
        public void SunsetTopic_ExistsInTheDialogueData()
        {
            Dictionary<string, object> node = ReadObject(Path.Combine(DataRoot, "Dialogue", FormalEnding.DialogueId + ".json"));
            DialogueData data = DialogueData.FromJson(node);
            DialogueTopic topic = data.Topics.Find(t => t.Id == FormalEnding.TopicId);
            Assert.IsNotNull(topic, FormalEnding.DialogueId + " に " + FormalEnding.TopicId + " が無い");
            Assert.IsNotEmpty(topic.Lines);
            Assert.IsTrue(topic.Once, "once でないと、あとで話しかけたときに同じ話がまた出る");
            Assert.IsTrue(string.IsNullOrEmpty(topic.StartsQuest), "エンディングの会話でクエストを始めない");
        }

        [Test]
        public void SunsetQuest_IsTheLastMainQuestAndNeedsEveryOtherMainQuest()
        {
            var main = new List<QuestData>();
            QuestData sunset = null;
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(ReadObject(file));
                if (quest.Id == FormalEnding.SunsetQuestId)
                {
                    sunset = quest;
                }
                else if (!quest.Side && !quest.Secret)
                {
                    main.Add(quest);
                }
            }

            Assert.IsNotNull(sunset, FormalEnding.SunsetQuestId + " が無い");
            foreach (QuestData quest in main)
            {
                CollectionAssert.Contains(sunset.Prerequisites, quest.Id,
                    "夕日の前に終えなくてもよい本編がある（エンディングのあとに本編が残る）: " + quest.Id);
                Assert.Less(quest.Order, sunset.Order, quest.Id + " が夕日より後の順番");
            }
        }

        // ---- クレジットのページ ----

        [TestCase("ja")]
        [TestCase("en")]
        public void CreditsBody_IsTheCreditsTextWithoutTheBackHint(string locale)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale(locale);
                    string body = CreditsView.BuildBody();
                    string text = CreditsView.BuildText();
                    StringAssert.StartsWith(body, text, "クレジット画面とエンディングで本文が違う");
                    StringAssert.DoesNotContain("Esc", body, "エンディングのページに「Esc で戻る」が出る");
                    StringAssert.Contains("Esc", text);
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            int at = text.IndexOf(part, System.StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(part, at + part.Length, System.StringComparison.Ordinal);
            }

            return count;
        }
    }
}
