using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 一度きり（once）の話題を使い終えた記録のセーブ (#395)。セーブ → 読み込みのあとも同じ話題を二度出さないこと、
    /// 依頼（startsQuest）を持つ話題を読み込み後にもう一度頼まないこと、その項目を持たない古いセーブも読めること。
    /// </summary>
    public sealed class OnceTopicSaveTests
    {
        private const string KanameJson =
            "{\"id\":\"kaname\",\"topics\":[" +
            "{\"id\":\"t_thanks\",\"once\":true,\"lines\":[\"この前はありがとう\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"いらっしゃい\"]}]}";

        private const string SoraJson =
            "{\"id\":\"sora\",\"topics\":[" +
            "{\"id\":\"t_request\",\"once\":true,\"startsQuest\":\"q_unknown\",\"lines\":[\"頼みがあるんだ\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"やあ\"]}]}";

        [SetUp]
        public void SetUp()
        {
            OnceTopics.Reset();
            NpcAffinity.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            OnceTopics.Reset();
            NpcAffinity.Reset();
        }

        private static DialogueData Load(string json)
        {
            return DialogueData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        private static DialogueTopic Select(DialogueData data)
        {
            return DialogueSystem.SelectTopic(data, new QuestSystem(), OnceTopics.Keys);
        }

        /// <summary>今の記録をセーブの形にして JSON で書き、読み直して戻す。</summary>
        private static SaveData SaveAndLoad()
        {
            var data = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                SpentTopics = OnceTopics.Sorted()
            };

            SaveData restored = SaveDataRules.Parse(SaveDataRules.ToJson(data));
            OnceTopics.Reset();
            OnceTopics.Restore(restored.SpentTopics);
            return restored;
        }

        [Test]
        public void SpentOnceTopicIsNotOfferedAgainAfterSaveAndLoad()
        {
            DialogueData kaname = Load(KanameJson);
            Assert.AreEqual("t_thanks", Select(kaname).Id);

            OnceTopics.Note("kaname/t_thanks");
            SaveData restored = SaveAndLoad();

            CollectionAssert.AreEqual(new[] { "kaname/t_thanks" }, restored.SpentTopics);
            Assert.AreEqual("t_idle", Select(kaname).Id, "読み込み後にお礼の話題がもう一度出ている");
        }

        [Test]
        public void SpentRequestTopicDoesNotAskForTheQuestAgainAfterLoad()
        {
            // 依頼のクエストが受注中でも完了済みでもない（StartQuest が通らなかった）場合も、
            // 使い終えた依頼の話題は読み込み後に出さない。
            DialogueData sora = Load(SoraJson);
            Assert.AreEqual("t_request", Select(sora).Id);

            OnceTopics.Note("sora/t_request");
            SaveAndLoad();

            Assert.AreEqual("t_idle", Select(sora).Id, "読み込み後に同じ依頼をもう一度頼まれている");
        }

        [Test]
        public void OldSaveWithoutSpentTopicsLoadsWithNothingSpent()
        {
            const string oldJson = "{\"SaveVersion\":3,\"CharacterId\":\"mirai\",\"TimeHours\":9.0,\"DayNumber\":1}";

            SaveData data = SaveDataRules.Parse(oldJson);

            Assert.IsNotNull(data, "その項目の無い古いセーブを読めていない");
            Assert.IsNotNull(data.SpentTopics);
            CollectionAssert.IsEmpty(data.SpentTopics);

            OnceTopics.Note("kaname/t_thanks");
            OnceTopics.Restore(data.SpentTopics);
            Assert.AreEqual("t_thanks", Select(Load(KanameJson)).Id, "読み込んだセーブに無い記録が残っている");
        }

        [Test]
        public void ParseDropsEmptyAndDuplicateKeys()
        {
            const string json = "{\"SaveVersion\":3,\"SpentTopics\":[\"kaname/t_thanks\",\"\",\"kaname/t_thanks\",\"sora/t_request\"]}";

            SaveData data = SaveDataRules.Parse(json);

            CollectionAssert.AreEqual(new[] { "kaname/t_thanks", "sora/t_request" }, data.SpentTopics);
        }

        [Test]
        public void RestoreReplacesTheCurrentRecordAndSkipsEmptyKeys()
        {
            OnceTopics.Note("old/t_old");

            OnceTopics.Restore(new[] { "kaname/t_thanks", null, string.Empty });

            CollectionAssert.AreEqual(new[] { "kaname/t_thanks" }, OnceTopics.Sorted());
            OnceTopics.Restore(null);
            CollectionAssert.IsEmpty(OnceTopics.Sorted());
        }

        [Test]
        public void ParseKeepsAffinityAndAnsweredChoices()
        {
            // 選択肢に答えた話題もセーブから戻らないと、読み込み後に同じ選択肢をもう一度聞いてしまう。
            var data = new SaveData { SaveVersion = SaveData.CurrentVersion };
            data.Affinity.Add("kaname=2");
            data.AnsweredChoices.Add("kaname/t_choice");

            SaveData restored = SaveDataRules.Parse(SaveDataRules.ToJson(data));

            CollectionAssert.AreEqual(new[] { "kaname=2" }, restored.Affinity);
            CollectionAssert.AreEqual(new[] { "kaname/t_choice" }, restored.AnsweredChoices);
        }
    }
}
