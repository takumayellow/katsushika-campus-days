using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 英語表示で会話の本文・話者名、クエストの題名・あらすじ・目標・報酬が英語になり、
    /// 日本語に戻すと日本語になり、英語の無いものは日本語のまま出ること (#92, #93, #95)。
    /// 辞書は実物（Resources/KCD/Localization）を使う。
    /// </summary>
    public sealed class LocalizedTextTests
    {
        private const string PlayerName = "PLAYER";

        private const string DialogueJson = @"{
  ""id"": ""kaname"",
  ""speaker"": ""中川 かなめ"",
  ""topics"": [
    {
      ""id"": ""hello"",
      ""lines"": [
        { ""text"": ""おはよう。今日も実験？"", ""text_en"": ""Morning. Lab again today?"" },
        { ""speaker"": ""自分"", ""text"": ""うん、午後から。"", ""text_en"": ""Yeah, this afternoon."" },
        { ""text"": ""英語がまだ無い行。"" },
        ""文字列だけの行。""
      ]
    }
  ]
}";

        private const string QuestJson = @"{
  ""id"": ""q_i18n_lab"",
  ""title"": ""はじめての実験"",
  ""title_en"": ""First Experiment"",
  ""summary"": ""実験棟へ行ってみよう。"",
  ""summary_en"": ""Go and see the lab building."",
  ""rewardText"": ""報酬: 実験用ゴーグル"",
  ""rewardText_en"": ""Reward: Lab Goggles"",
  ""steps"": [
    { ""id"": ""enter"", ""type"": ""enter"", ""target"": ""lab1"", ""text"": ""第1実験棟に入る"", ""text_en"": ""Enter Experiment Building 1"" },
    { ""id"": ""report"", ""type"": ""flag"", ""target"": ""i18n_report"", ""text"": ""先生に報告する"" }
  ]
}";

        private const string JapaneseOnlyQuestJson = @"{
  ""id"": ""q_i18n_ja_only"",
  ""title"": ""日本語だけの依頼"",
  ""summary"": ""英語の文はまだ無い。"",
  ""rewardText"": ""報酬: 日本語の礼状"",
  ""steps"": [
    { ""id"": ""only"", ""type"": ""flag"", ""target"": ""i18n_ja_only"", ""text"": ""日本語だけの目標"" }
  ]
}";

        /// <summary>和文の句読点・かな・漢字・全角文字。英語表示の文に混ざってはいけない。</summary>
        private static readonly Regex Japanese = new Regex("[\\u3000-\\u30FF\\u3400-\\u9FFF\\uFF00-\\uFFEF]");

        [Test]
        public void Dialogue_English_LinesAndSpeakersAreEnglish()
        {
            List<DialogueLine> lines = HelloLines();

            In("en", () =>
            {
                Assert.AreEqual("Morning. Lab again today?", lines[0].DisplayText);
                Assert.AreEqual("Yeah, this afternoon.", lines[1].DisplayText);

                string npc = DialogueView.SpeakerLabel(lines[0], PlayerName);
                string self = DialogueView.SpeakerLabel(lines[1], PlayerName);
                Assert.AreEqual(L.Get("ui.npc.kaname"), npc);
                Assert.AreEqual(L.Get("ui.npc.self"), self);
                AssertEnglish(npc);
                AssertEnglish(self);
            });
        }

        [Test]
        public void Dialogue_BackToJapanese_LinesAndSpeakersAreJapanese()
        {
            List<DialogueLine> lines = HelloLines();

            In("en", () =>
            {
                Assert.AreEqual("Morning. Lab again today?", lines[0].DisplayText);

                SwitchTo("ja");
                Assert.AreEqual("おはよう。今日も実験？", lines[0].DisplayText);
                Assert.AreEqual("うん、午後から。", lines[1].DisplayText);
                Assert.AreEqual("中川 かなめ", DialogueView.SpeakerLabel(lines[0], PlayerName));
                Assert.AreEqual(DialogueLine.SelfSpeaker, DialogueView.SpeakerLabel(lines[1], PlayerName));
            });
        }

        [Test]
        public void Dialogue_English_LinesWithoutEnglishStayJapanese()
        {
            List<DialogueLine> lines = HelloLines();

            In("en", () =>
            {
                Assert.AreEqual("英語がまだ無い行。", lines[2].DisplayText);
                Assert.AreEqual("文字列だけの行。", lines[3].DisplayText);

                // 本文が日本語のままでも、話者名は辞書の英語名にする。
                Assert.AreEqual(L.Get("ui.npc.kaname"), DialogueView.SpeakerLabel(lines[3], PlayerName));
            });
        }

        [Test]
        public void Dialogue_EmptySpeakerIsThePlayer()
        {
            var line = new DialogueLine { Speaker = string.Empty, Text = "……。" };

            In("en", () => Assert.AreEqual(PlayerName, DialogueView.SpeakerLabel(line, PlayerName)));
        }

        [Test]
        public void Quest_English_TitleSummaryObjectiveAndRewardAreEnglish()
        {
            QuestData quest = Quest(QuestJson);
            QuestSystem quests = Started(quest);

            In("en", () =>
            {
                Assert.AreEqual("First Experiment", quest.DisplayTitle);
                Assert.AreEqual("Go and see the lab building.", quest.DisplaySummary);
                Assert.AreEqual("Reward: Lab Goggles", quest.DisplayRewardText);
                Assert.AreEqual("・Enter Experiment Building 1", QuestTrackerView.StepLine(quest));

                string completed = QuestTrackerView.CompletedMessage(quest);
                Assert.AreEqual(L.Format("ui.hud.quest_completed", "First Experiment"), completed);
                AssertEnglish(completed);

                string log = Log(quests);
                StringAssert.Contains(L.Get("ui.questlog.active") + "First Experiment", log);
                StringAssert.Contains("Go and see the lab building.", log);
                StringAssert.Contains("・Enter Experiment Building 1", log);
                StringAssert.DoesNotContain("はじめての実験", log);
                StringAssert.DoesNotContain("第1実験棟に入る", log);
            });
        }

        [Test]
        public void Quest_BackToJapanese_IsJapaneseAgain()
        {
            QuestData quest = Quest(QuestJson);
            QuestSystem quests = Started(quest);

            In("en", () =>
            {
                Assert.AreEqual("First Experiment", quest.DisplayTitle);

                SwitchTo("ja");
                Assert.AreEqual("はじめての実験", quest.DisplayTitle);
                Assert.AreEqual("実験棟へ行ってみよう。", quest.DisplaySummary);
                Assert.AreEqual("報酬: 実験用ゴーグル", quest.DisplayRewardText);
                Assert.AreEqual("・第1実験棟に入る", QuestTrackerView.StepLine(quest));
                Assert.AreEqual(L.Format("ui.hud.quest_completed", "はじめての実験"), QuestTrackerView.CompletedMessage(quest));

                string log = Log(quests);
                StringAssert.Contains("はじめての実験", log);
                StringAssert.Contains("・第1実験棟に入る", log);
                StringAssert.DoesNotContain("First Experiment", log);
            });
        }

        [Test]
        public void Quest_English_TextsWithoutEnglishStayJapanese()
        {
            QuestData quest = Quest(JapaneseOnlyQuestJson);
            QuestData mixed = Quest(QuestJson);

            In("en", () =>
            {
                Assert.AreEqual("日本語だけの依頼", quest.DisplayTitle);
                Assert.AreEqual("英語の文はまだ無い。", quest.DisplaySummary);
                Assert.AreEqual("報酬: 日本語の礼状", quest.DisplayRewardText);
                Assert.AreEqual("・日本語だけの目標", QuestTrackerView.StepLine(quest));

                // 題名は英語、2 つ目の目標だけ英語が無い。
                Assert.AreEqual("先生に報告する", mixed.Steps[1].DisplayText);
            });
        }

        [Test]
        public void SeatPrompt_FollowsTheLanguage()
        {
            In("en", () =>
            {
                Assert.AreEqual("Sit", L.Get("ui.interact.sit", "座る"));

                SwitchTo("ja");
                Assert.AreEqual("座る", L.Get("ui.interact.sit", "座る"));
            });
        }

        private static List<DialogueLine> HelloLines()
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(DialogueJson) as Dictionary<string, object>);
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.Topics.Count);
            Assert.AreEqual(4, data.Topics[0].Lines.Count);
            return data.Topics[0].Lines;
        }

        private static QuestData Quest(string json)
        {
            QuestData quest = QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
            Assert.IsNotNull(quest);
            return quest;
        }

        private static QuestSystem Started(QuestData quest)
        {
            var quests = new QuestSystem();
            quests.Load(new[] { quest });
            Assert.IsTrue(quests.StartQuest(quest.Id), quest.Id + " を受注できない");
            return quests;
        }

        private static string Log(QuestSystem quests)
        {
            var builder = new StringBuilder();
            QuestLogView.Compose(builder, quests);
            return builder.ToString();
        }

        private static void AssertEnglish(string text)
        {
            Assert.IsFalse(string.IsNullOrEmpty(text), "英語表示の文が空");
            Assert.IsFalse(Japanese.IsMatch(text), "英語表示に日本語が残っている: " + text);
        }

        private static void SwitchTo(string locale)
        {
            L.SetLocale(locale);
            Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
        }

        /// <summary>言語を切り替えて body を走らせ、言語と保存済みの設定を元に戻す（CreditsTextTests と同じ）。</summary>
        private static void In(string locale, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                SwitchTo(locale);
                body();
            }
            finally
            {
                L.SetLocale(before);
                if (hadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, savedPref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }
    }
}
