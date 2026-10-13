using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 隠しストーリー「はじまりの春の宝探し」(#175)。隠しアイテムのメモ、いなり先輩の依頼、図書館 2 階のドームの扉、
    /// 受注前に拾った鍵の追いつき、クエスト数の集計から隠しクエストを外すことを、実データで確かめる。
    /// </summary>
    public sealed class SecretStoryTests
    {
        private const string SecretQuest = HiddenNote.SecretQuestId;
        private const string DomeKey = "c_dome_key";
        private const float EveningHour = 19.5f;

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        private static CollectibleCatalog Catalog()
        {
            return CollectibleCatalog.Parse(File.ReadAllText(Path.Combine(DataRoot, "Collectibles", "collectibles.json")));
        }

        private static List<QuestData> LoadQuests()
        {
            var quests = new List<QuestData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                quests.Add(QuestData.FromJson(ReadObject(file)));
            }

            return quests;
        }

        private static DialogueData Dialogue(string id)
        {
            DialogueData data = DialogueData.FromJson(ReadObject(Path.Combine(DataRoot, "Dialogue", id + ".json")));
            Assert.IsNotNull(data, id + ".json を読めない");
            return data;
        }

        /// <summary>実データのクエストを読み、rows の (id, 状態, 済んだステップ数) で始める。</summary>
        private static QuestSystem Quests(params (string id, int state, int stepsDone)[] rows)
        {
            var quests = new QuestSystem { Clock = () => EveningHour };
            quests.Load(LoadQuests());
            var progress = new QuestProgress();
            foreach ((string id, int state, int stepsDone) in rows)
            {
                progress.QuestIds.Add(id);
                progress.States.Add(state);
                progress.StepsDone.Add(stepsDone);
            }

            quests.Restore(progress);
            return quests;
        }

        private static string TopicId(DialogueTopic topic)
        {
            return topic != null ? topic.Id : "(なし)";
        }

        [Test]
        public void HiddenItems_CarryNotesNumberedOneToTwentyInBothLanguages()
        {
            CollectibleCatalog catalog = Catalog();
            var numbers = new List<int>();
            foreach (CatalogItem item in catalog.Items)
            {
                if (!item.IsHidden)
                {
                    Assert.IsFalse(item.HasNote, item.Id + " は隠しアイテムではないのにメモがある");
                    continue;
                }

                Assert.IsTrue(item.HasNote, item.Id + " にメモが無い");
                Assert.IsFalse(string.IsNullOrEmpty(item.NoteEn), item.Id + " のメモに英語が無い");
                numbers.Add(item.NoteNo);
            }

            numbers.Sort();
            var expected = new List<int>();
            for (int i = 1; i <= catalog.HiddenCount; i++)
            {
                expected.Add(i);
            }

            Assert.AreEqual(20, catalog.HiddenCount);
            CollectionAssert.AreEqual(expected, numbers, "メモの番号が 1〜20 の通し番号になっていない");
            Assert.AreEqual(20, catalog.FindItem(DomeKey).NoteNo, "鍵は 20 番のメモに添える");
        }

        [Test]
        public void Inari_AsksAboutTheHunt_FromFiveHiddenItemsAfterTheLibrary()
        {
            DialogueData inari = Dialogue("inari");
            // 図書館のお礼（once）はもう聞いた。
            var spent = new HashSet<string> { "inari/t_done" };

            QuestSystem done = Quests(("q_library", QuestProgress.StateCompleted, 0));
            Assert.AreNotEqual("t_secret_start", TopicId(DialogueSystem.SelectTopic(inari, done, spent, 4, _ => false)));
            Assert.AreEqual("t_secret_start", TopicId(DialogueSystem.SelectTopic(inari, done, spent, 5, _ => false)));

            // 図書館の前提がそろうまでは、5 個拾っていても依頼しない。
            QuestSystem before = Quests();
            Assert.AreNotEqual("t_secret_start", TopicId(DialogueSystem.SelectTopic(inari, before, spent, 20, _ => true)));

            // 受注中・達成後は、それぞれの話題になる。
            QuestSystem active = Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, QuestProgress.StateActive, 2));
            Assert.AreEqual("t_secret_active", TopicId(DialogueSystem.SelectTopic(inari, active, spent, 20, _ => true)));
            QuestSystem finished = Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, QuestProgress.StateCompleted, 0));
            Assert.AreEqual("t_secret_done", TopicId(DialogueSystem.SelectTopic(inari, finished, spent, 20, _ => true)));
        }

        [TestCase(QuestProgress.StateCompleted, 20, true, false, "t_open")]
        [TestCase(QuestProgress.StateActive, 20, true, false, "t_letter")]
        [TestCase(QuestProgress.StateActive, 20, true, true, "t_read")]
        [TestCase(QuestProgress.StateActive, 4, false, false, "t_locked")]
        [TestCase(QuestProgress.StateNotStarted, 5, true, false, "t_key_ask")]
        [TestCase(QuestProgress.StateNotStarted, 2, true, false, "t_key_only")]
        [TestCase(QuestProgress.StateNotStarted, 0, false, false, "t_idle")]
        public void DomeDoor_ChoosesTheTopicByQuestAndKey(int state, int hidden, bool hasKey, bool letterRead, string expected)
        {
            DialogueData door = Dialogue("dome_door");
            QuestSystem quests = state == QuestProgress.StateNotStarted
                ? Quests(("q_library", QuestProgress.StateCompleted, 0))
                : Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, state, hasKey ? 1 : 0));
            var spent = new HashSet<string>();
            if (letterRead)
            {
                spent.Add("dome_door/t_letter");
            }

            DialogueTopic topic = DialogueSystem.SelectTopic(door, quests, spent, hidden, id => hasKey && id == DomeKey);
            Assert.AreEqual(expected, TopicId(topic));
        }

        [Test]
        public void OpeningTheDoorWithTheKey_AdvancesTheHuntToInari()
        {
            DialogueData door = Dialogue("dome_door");
            QuestSystem quests = Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, QuestProgress.StateActive, 1));
            var spent = new HashSet<string>();

            DialogueTopic chosen = DialogueSystem.SelectTopic(door, quests, spent, 20, id => id == DomeKey);
            DialogueSystem.ReportTalkAndSelect(door, quests, spent, chosen, 20, id => id == DomeKey);

            Assert.AreEqual("s3", quests.Find(SecretQuest).CurrentStep.Id, "扉を開けたら、いなりに知らせるステップへ進む");
        }

        [Test]
        public void KeyPickedUpBeforeTheHunt_CountsAfterLoadingASave()
        {
            (string, int, int)[] rows =
            {
                ("q_library", QuestProgress.StateCompleted, 0),
                (SecretQuest, QuestProgress.StateActive, 0),
            };

            QuestSystem withoutNote = Quests(rows);
            Assert.AreEqual("s1", withoutNote.Find(SecretQuest).CurrentStep.Id);

            var quests = new QuestSystem { Clock = () => EveningHour };
            quests.Load(LoadQuests());
            quests.NoteHeldFromSave(new[] { DomeKey, "c_other" });
            var progress = new QuestProgress();
            foreach ((string id, int state, int stepsDone) in rows)
            {
                progress.QuestIds.Add(id);
                progress.States.Add(state);
                progress.StepsDone.Add(stepsDone);
            }

            quests.Restore(progress);
            Assert.AreEqual(1, quests.HeldCount(DomeKey));
            Assert.AreEqual("s2", quests.Find(SecretQuest).CurrentStep.Id, "セーブに載っている鍵で 1 つ目のステップが済む");
        }

        [Test]
        public void NoteHeldFromSave_KeepsCountsFromThisSession()
        {
            var quests = new QuestSystem { Clock = () => EveningHour };
            quests.Load(LoadQuests());
            quests.ReportCollect("c_milk");
            quests.ReportCollect("c_milk");
            quests.NoteHeldFromSave(new[] { "c_milk", null, string.Empty });
            Assert.AreEqual(2, quests.HeldCount("c_milk"));
            quests.NoteHeldFromSave(null);
            Assert.AreEqual(2, quests.HeldCount("c_milk"));
        }

        [Test]
        public void SecretQuest_IsLeftOutOfTheQuestCountAchievements()
        {
            CollectibleCatalog catalog = Catalog();
            List<QuestData> all = LoadQuests();
            CatalogAchievement allQuests = null;
            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                if (achievement.Id == "ach_all_quests")
                {
                    allQuests = achievement;
                }
            }

            Assert.IsNotNull(allQuests);
            var shown = all.FindAll(q => !q.Secret);
            Assert.AreEqual(1, all.Count - shown.Count, "隠しクエストは 1 つ");

            // 隠しクエストを終えても、ほかのクエストを 1 つ残していれば届かない。
            var completed = new HashSet<string> { SecretQuest };
            for (int i = 1; i < shown.Count; i++)
            {
                completed.Add(shown[i].Id);
            }

            var almost = new AchievementRecord(new string[0], new string[0], 0, new string[0], all, completed.Contains);
            Assert.IsFalse(AchievementBook.IsMet(allQuests, catalog, almost));

            completed.Add(shown[0].Id);
            completed.Remove(SecretQuest);
            var every = new AchievementRecord(new string[0], new string[0], 0, new string[0], all, completed.Contains);
            Assert.IsTrue(AchievementBook.IsMet(allQuests, catalog, every), "隠しクエスト無しで全クエストの称号が届く");
        }

        [Test]
        public void HiddenNote_Build_WritesTheNumberedNoteAndAppendsTheHint()
        {
            CollectibleCatalog catalog = Catalog();
            CatalogItem key = catalog.FindItem(DomeKey);
            DialogueTopic hint = Dialogue(HiddenNote.DialogueId).Topics.Find(t => t.Id == HiddenNote.HintTopicId);
            Assert.IsNotNull(hint, "note.json に t_hint が無い");

            DialogueTopic plain = HiddenNote.Build(key, catalog.HiddenCount, null);
            Assert.AreEqual(1, plain.Lines.Count);
            DialogueLine line = plain.Lines[0];
            StringAssert.StartsWith("[20/20]  ", line.Text);
            StringAssert.StartsWith("[20/20]  ", line.TextEn);
            Assert.IsTrue(line.Written, "メモは書かれた文字として出す");
            Assert.AreEqual("ui.npc.note", line.SpeakerKey);

            DialogueTopic withHint = HiddenNote.Build(key, catalog.HiddenCount, hint);
            Assert.AreEqual(1 + hint.Lines.Count, withHint.Lines.Count);

            Assert.IsNull(HiddenNote.Build(catalog.FindItem("c_blank_note"), catalog.HiddenCount, hint), "メモの無いアイテム");
            Assert.IsNull(HiddenNote.Build(null, catalog.HiddenCount, hint));
        }

        [Test]
        public void HiddenNote_ShouldHint_OnlyFromFiveUntilTheHuntStarts()
        {
            QuestSystem open = Quests(("q_library", QuestProgress.StateCompleted, 0));
            Assert.IsFalse(HiddenNote.ShouldHint(4, open));
            Assert.IsTrue(HiddenNote.ShouldHint(5, open));
            Assert.IsFalse(HiddenNote.ShouldHint(20, null));

            QuestSystem active = Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, QuestProgress.StateActive, 0));
            Assert.IsFalse(HiddenNote.ShouldHint(20, active));
            QuestSystem done = Quests(("q_library", QuestProgress.StateCompleted, 0), (SecretQuest, QuestProgress.StateCompleted, 0));
            Assert.IsFalse(HiddenNote.ShouldHint(20, done));
        }

        [Test]
        public void NpcTalker_PromptKey_ShowsInspectAndFallsBackToTalk()
        {
            var go = new GameObject("door");
            try
            {
                // NPCTalker は Collider を要る（RequireComponent）。Collider は抽象なので先に付ける。
                go.AddComponent<BoxCollider>();
                NPCTalker talker = go.AddComponent<NPCTalker>();
                talker.PromptKey = "ui.interact.inspect";
                Assert.AreEqual(L.Get("ui.interact.inspect", "ui.interact.inspect"), talker.PromptLabel);
                Assert.AreNotEqual(L.Get("ui.interact.talk", "話す"), talker.PromptLabel);

                talker.PromptKey = null;
                Assert.AreEqual(NPCTalker.TalkPromptKey, talker.PromptKey);
                Assert.AreEqual(L.Get("ui.interact.talk", "話す"), talker.PromptLabel);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
