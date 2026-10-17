using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 会話の選択肢と NPC の好感度 (#9)。選択肢の読み込み、選んだときの好感度と答えた話題の記録、
    /// 好感度で出る話題と答えた話題を出さない選び方、セーブへの載せ方、選択肢の描き方。
    /// </summary>
    public sealed class DialogueChoiceTests
    {
        private const string KanameJson =
            "{\"id\":\"kaname\",\"topics\":[" +
            "{\"id\":\"t_choice\",\"once\":true,\"lines\":[\"どうだった？\"],\"choices\":[" +
            "{\"text\":\"おいしかった\",\"text_en\":\"Great\",\"affinity\":2,\"setsFlag\":\"f_liked\",\"lines\":[\"でしょ\",\"また来て\"]}," +
            "{\"text\":\"ふつう\",\"affinity\":1,\"lines\":[\"そう\"]}," +
            "{\"text\":\"まだ\"}]}," +
            "{\"id\":\"t_friend\",\"requiresAffinity\":2,\"once\":true,\"lines\":[\"来た来た\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"いらっしゃい\"]}]}";

        private PlayerPrefsKeyScope _localePref;
        private string _savedLocale;

        [SetUp]
        public void SetUp()
        {
            NpcAffinity.Reset();
            _localePref = new PlayerPrefsKeyScope(L.PrefKey);
            _savedLocale = L.Locale;
            L.SetLocale("ja");
        }

        [TearDown]
        public void TearDown()
        {
            NpcAffinity.Reset();
            L.SetLocale(_savedLocale);
            _localePref.Dispose();
        }

        private static DialogueData Kaname()
        {
            return DialogueData.FromJson(MiniJson.Deserialize(KanameJson) as Dictionary<string, object>);
        }

        private static DialogueTopic Select(DialogueData data)
        {
            return DialogueSystem.SelectTopic(data, new QuestSystem(), new HashSet<string>());
        }

        [Test]
        public void ParsesChoicesWithTheirAffinityFlagAndReplies()
        {
            DialogueTopic topic = Kaname().Topics[0];

            Assert.IsTrue(topic.HasChoices);
            Assert.AreEqual(3, topic.Choices.Count);
            Assert.AreEqual(2, topic.Choices[0].Affinity);
            Assert.AreEqual("f_liked", topic.Choices[0].SetsFlag);
            Assert.AreEqual("Great", topic.Choices[0].TextEn);
            Assert.AreEqual(2, topic.Choices[0].Lines.Count);
            Assert.AreEqual(0, topic.Choices[2].Lines.Count, "返事の無い選択肢は選んだところで閉じる");
            Assert.AreEqual(2, Kaname().Topics[1].RequiresAffinity);
            Assert.IsFalse(Kaname().Topics[2].HasChoices);
        }

        [Test]
        public void StepChoiceWrapsAroundBothEnds()
        {
            Assert.AreEqual(1, DialogueSystem.StepChoice(0, 1, 3));
            Assert.AreEqual(0, DialogueSystem.StepChoice(2, 1, 3));
            Assert.AreEqual(2, DialogueSystem.StepChoice(0, -1, 3));
            Assert.AreEqual(0, DialogueSystem.StepChoice(0, 1, 0));
        }

        [Test]
        public void ApplyChoiceRaisesAffinityAndRemembersTheTopic()
        {
            DialogueChoice choice = Kaname().Topics[0].Choices[0];

            DialogueSystem.ApplyChoice("kaname", "kaname/t_choice", choice);

            Assert.AreEqual(2, NpcAffinity.Get("kaname"));
            Assert.IsTrue(NpcAffinity.HasAnswered("kaname/t_choice"));
            Assert.AreEqual(0, NpcAffinity.Get("sora"));
        }

        [Test]
        public void AnsweredChoiceTopicIsNotOfferedAgain()
        {
            Assert.AreEqual("t_choice", Select(Kaname()).Id);

            NpcAffinity.NoteAnswered("kaname/t_choice");

            Assert.AreEqual("t_idle", Select(Kaname()).Id, "ロードのあとも同じ質問をしない");
        }

        [Test]
        public void AffinityTopicWaitsForEnoughAffinityThenTakesPriorityOverIdle()
        {
            NpcAffinity.NoteAnswered("kaname/t_choice");
            NpcAffinity.Add("kaname", 1);
            Assert.AreEqual("t_idle", Select(Kaname()).Id);

            NpcAffinity.Add("kaname", 1);
            Assert.AreEqual("t_friend", Select(Kaname()).Id);
        }

        [Test]
        public void ChoiceTopicIsSpentOnlyOnceAnswered()
        {
            DialogueData data = Kaname();
            Assert.IsFalse(DialogueSystem.SpendsOnFinish(data.Topics[0], "kaname/t_choice"), "選ばずに閉じたらもう一度聞く");
            Assert.IsTrue(DialogueSystem.SpendsOnFinish(data.Topics[1], "kaname/t_friend"));
            Assert.IsFalse(DialogueSystem.SpendsOnFinish(data.Topics[2], "kaname/t_idle"), "once でない話題");

            NpcAffinity.NoteAnswered("kaname/t_choice");
            Assert.IsTrue(DialogueSystem.SpendsOnFinish(data.Topics[0], "kaname/t_choice"));
        }

        [Test]
        public void AffinityStaysBetweenZeroAndMax()
        {
            NpcAffinity.Add("kaname", 99);
            Assert.AreEqual(NpcAffinity.Max, NpcAffinity.Get("kaname"));

            NpcAffinity.Add("kaname", -99);
            Assert.AreEqual(0, NpcAffinity.Get("kaname"));
            CollectionAssert.IsEmpty(NpcAffinity.SortedValues(), "0 の好感度はセーブに載せない");

            NpcAffinity.Add(string.Empty, 3);
            NpcAffinity.Add(null, 3);
            CollectionAssert.IsEmpty(NpcAffinity.SortedValues());
        }

        [Test]
        public void CaptureAndRestoreRoundTrip()
        {
            NpcAffinity.Add("sora", 3);
            NpcAffinity.Add("kaname", 2);
            NpcAffinity.NoteAnswered("sora/t_choice");
            List<string> values = NpcAffinity.SortedValues();
            List<string> answered = NpcAffinity.SortedAnswered();
            CollectionAssert.AreEqual(new[] { "kaname=2", "sora=3" }, values);

            NpcAffinity.Reset();
            NpcAffinity.Restore(values, answered);

            Assert.AreEqual(3, NpcAffinity.Get("sora"));
            Assert.AreEqual(2, NpcAffinity.Get("kaname"));
            Assert.IsTrue(NpcAffinity.HasAnswered("sora/t_choice"));
        }

        [Test]
        public void RestoreSkipsBrokenEntriesAndClampsValues()
        {
            NpcAffinity.Add("old", 5);

            NpcAffinity.Restore(new[] { "kaname=2", "=4", "sora", "inari=x", null, "prof=99", "a=b=1" }, null);

            Assert.AreEqual(0, NpcAffinity.Get("old"), "前の記録は置き換える");
            Assert.AreEqual(2, NpcAffinity.Get("kaname"));
            Assert.AreEqual(0, NpcAffinity.Get("sora"));
            Assert.AreEqual(0, NpcAffinity.Get("inari"));
            Assert.AreEqual(NpcAffinity.Max, NpcAffinity.Get("prof"));
            Assert.AreEqual(1, NpcAffinity.Get("a=b"));
            CollectionAssert.IsEmpty(NpcAffinity.SortedAnswered());
        }

        [Test]
        public void SaveDataCarriesAffinityAndOldSavesReadAsEmpty()
        {
            var data = new SaveData
            {
                Affinity = new List<string> { "kaname=2" },
                AnsweredChoices = new List<string> { "kaname/t_choice" },
            };

            SaveData back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
            CollectionAssert.AreEqual(new[] { "kaname=2" }, back.Affinity);
            CollectionAssert.AreEqual(new[] { "kaname/t_choice" }, back.AnsweredChoices);

            SaveData old = JsonUtility.FromJson<SaveData>("{\"SaveVersion\":3}");
            Assert.IsNotNull(old.Affinity);
            Assert.IsNotNull(old.AnsweredChoices);
            CollectionAssert.IsEmpty(old.Affinity);
        }

        [Test]
        public void ComposeChoicesMarksTheSelectedRowAndLinksEveryRow()
        {
            List<DialogueChoice> choices = Kaname().Topics[0].Choices;

            string keys = DialogueView.ComposeChoices(choices, 1, false);
            StringAssert.Contains("<link=\"choice:0\">   おいしかった</link>", keys);
            StringAssert.Contains("<link=\"choice:1\"><color=#FFD98A>▶ ふつう</color></link>", keys);
            StringAssert.Contains("<link=\"choice:2\">", keys);
            StringAssert.Contains("Enter", keys, "キーのときは操作の案内を出す");

            string touch = DialogueView.ComposeChoices(choices, 0, true);
            StringAssert.StartsWith("<size=", touch);
            StringAssert.Contains("choice:-1", touch, "指のときは選ばずに閉じるリンクを最後に置く");
            StringAssert.DoesNotContain("Enter", touch);
        }

        [Test]
        public void ComposeChoicesUsesTheEnglishTextWhenThereIsOne()
        {
            L.SetLocale("en");
            List<DialogueChoice> choices = Kaname().Topics[0].Choices;

            string text = DialogueView.ComposeChoices(choices, 0, false);

            StringAssert.Contains("Great", text);
            StringAssert.Contains("ふつう", text, "英語の無い選択肢は日本語で出す");
        }
    }
}
