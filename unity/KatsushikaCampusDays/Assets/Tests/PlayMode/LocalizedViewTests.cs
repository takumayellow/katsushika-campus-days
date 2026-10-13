using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 会話ウィンドウと追跡表示が、ラベルにいまの言語の文（DisplayText / DisplayTitle）を入れ、
    /// 表示中に言語を切り替えると出し直すこと (#17, #92, #93)。
    /// 文そのものの選び分けは EditMode の LocalizedTextTests が見る。ここはビューがそれを使っているかを見る。
    /// どちらも L.LocaleChanged で出し直すので、InteractPromptLocaleTests と同じく AddComponent で Awake まで走る PlayMode で見る。
    /// Start は走らせない（GameManager と DialogueSystem を作らずに済む）。
    /// </summary>
    public sealed class LocalizedViewTests
    {
        private const string PlayerName = "みらい";
        private const string PlayerId = "mirai";

        private const string DialogueJson = @"{
  ""id"": ""kaname"",
  ""speaker"": ""中川 かなめ"",
  ""topics"": [
    {
      ""id"": ""hello"",
      ""lines"": [
        { ""text"": ""おはよう。今日も実験？"", ""text_en"": ""Morning. Lab again today?"" },
        { ""speaker"": ""自分"", ""text"": ""うん、午後から。"", ""text_en"": ""Yeah, this afternoon."" },
        { ""speaker"": """", ""text"": ""……よし。"", ""text_en"": ""...Right."" }
      ]
    }
  ]
}";

        private const string QuestJson = @"{
  ""id"": ""q_i18n_view"",
  ""title"": ""はじめての実験"",
  ""title_en"": ""First Experiment"",
  ""steps"": [
    { ""id"": ""enter"", ""type"": ""enter"", ""target"": ""lab1"", ""text"": ""第1実験棟に入る"", ""text_en"": ""Enter Experiment Building 1"" }
  ]
}";

        [Test]
        public void DialogueView_ShowsTheLineInTheCurrentLanguage_AndFollowsASwitch()
        {
            List<DialogueLine> lines = HelloLines();
            var host = new GameObject("LocalizedViewTests.Dialogue");
            Run(host, () =>
            {
                GameObject root = Child(host, "Root");
                TMP_Text speaker = Label(root, "Speaker");
                TMP_Text body = Label(root, "Body");
                DialogueView view = host.AddComponent<DialogueView>();
                view.Bind(root, speaker, body, null);

                SwitchTo("ja");
                view.Show(lines[0], PlayerName, PlayerId);
                Assert.IsTrue(root.activeSelf, "行を出しても会話ウィンドウが開かない");
                Assert.AreEqual("おはよう。今日も実験？", body.text);
                Assert.AreEqual("中川 かなめ", speaker.text);
                Assert.AreEqual(0, body.maxVisibleCharacters, "新しい行は 1 文字目から送る");

                SwitchTo("en");
                Assert.AreEqual("Morning. Lab again today?", body.text, "英語に切り替えても表示中の行が日本語のまま");
                Assert.AreEqual(L.Get("ui.npc.kaname"), speaker.text, "英語に切り替えても話者名が日本語のまま");
                Assert.AreEqual(body.text.Length, body.maxVisibleCharacters, "切り替えた行は全文を出す");

                view.Show(lines[1], PlayerName, PlayerId);
                Assert.AreEqual("Yeah, this afternoon.", body.text, "英語で出した行が英語にならない");
                Assert.AreEqual(L.Get("ui.npc.self"), speaker.text);

                view.Show(lines[2], PlayerName, PlayerId);
                Assert.AreEqual("...Right.", body.text);
                Assert.AreEqual(PlayerName, speaker.text, "話者が空の行はプレイヤーの名前を出す");

                SwitchTo("ja");
                Assert.AreEqual("……よし。", body.text, "日本語に戻しても表示中の行が英語のまま");
                Assert.AreEqual(PlayerName, speaker.text, "言語を切り替えたらプレイヤーの名前が消えた");

                view.Show(lines[1], PlayerName, PlayerId);
                Assert.AreEqual("うん、午後から。", body.text);
                Assert.AreEqual(DialogueLine.SelfSpeaker, speaker.text);
            });
        }

        [Test]
        public void QuestTrackerView_ShowsTheTitleAndStepInTheCurrentLanguage_AndFollowsASwitch()
        {
            QuestData quest = QuestData.FromJson(MiniJson.Deserialize(QuestJson) as Dictionary<string, object>);
            Assert.IsNotNull(quest);
            var quests = new QuestSystem { Clock = () => 12f };
            quests.Load(new[] { quest });
            Assert.IsTrue(quests.StartQuest(quest.Id), quest.Id + " を受注できない");

            var host = new GameObject("LocalizedViewTests.Tracker");
            Run(host, () =>
            {
                GameObject root = Child(host, "Root");
                root.SetActive(false);
                TMP_Text title = Label(root, "Title");
                TMP_Text step = Label(root, "Step");
                QuestTrackerView view = host.AddComponent<QuestTrackerView>();
                view.Bind(root, title, step);

                SwitchTo("ja");
                view.Watch(quests);
                Assert.IsTrue(root.activeSelf, "受注中のクエストがあるのに追跡表示が出ない");
                Assert.AreEqual("はじめての実験", title.text);
                Assert.AreEqual("・第1実験棟に入る", step.text);

                SwitchTo("en");
                Assert.AreEqual("First Experiment", title.text, "英語に切り替えても題名が日本語のまま");
                Assert.AreEqual("・Enter Experiment Building 1", step.text, "英語に切り替えても目標が日本語のまま");

                SwitchTo("ja");
                Assert.AreEqual("はじめての実験", title.text, "日本語に戻しても題名が英語のまま");
                Assert.AreEqual("・第1実験棟に入る", step.text, "日本語に戻しても目標が英語のまま");
            });
        }

        private static List<DialogueLine> HelloLines()
        {
            DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(DialogueJson) as Dictionary<string, object>);
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.Topics.Count);
            Assert.AreEqual(3, data.Topics[0].Lines.Count);
            return data.Topics[0].Lines;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static TMP_Text Label(GameObject parent, string name)
        {
            return Child(parent, name).AddComponent<TextMeshProUGUI>();
        }

        private static void SwitchTo(string locale)
        {
            L.SetLocale(locale);
            Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
        }

        /// <summary>
        /// body を走らせ、host を壊して言語と保存済みの設定を元に戻す。
        /// 会話ウィンドウを開くと HUD があれば遊び用の表示を隠すので、会話の外の状態（表示）に戻す。
        /// </summary>
        private static void Run(GameObject host, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                body();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                HUD hud = HUD.Instance;
                if (hud != null)
                {
                    hud.SetGameplayUIVisible(true);
                }

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
