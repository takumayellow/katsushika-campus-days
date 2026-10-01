using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 「〜と話す」でクエストを達成したその会話で、お礼（requiresCompletedQuest）の話題が出るか (#94)。
    /// 以前は話題を選んでから talk を報告していたので、達成した会話では受注中のヒントが出て、お礼は次に話したときだった。
    /// 実データの会話とクエストを読み、全 NPC について達成の瞬間に出る話題を表と突き合わせる。
    /// </summary>
    public sealed class DialogueCompletionTopicTests
    {
        private const float EveningHour = 19.5f;

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        /// <summary>(クエスト, 最後に話す相手, 達成した会話で出る話題)。</summary>
        private static readonly string[][] Expected =
        {
            // 教授の最初のあいさつ（無条件・once）はそのまま。体育館の依頼は次に話したときに出る。
            new[] { "q_orientation", "prof", "t_welcome" },
            new[] { "q_library", "inari", "t_done" },
            new[] { "q_lunch", "kaname", "t_done" },
            new[] { "q_coffee", "sora", "t_done" },
            new[] { "q_sq_greenhouse", "prof", "t_sq_greenhouse_done" },
            new[] { "q_sq_lost_card", "kaname", "t_sq_lost_card_done" },
            new[] { "q_sq_lab_notebook", "sora", "t_sq_lab_notebook_done" },
            new[] { "q_sq_stray_book", "inari", "t_sq_stray_book_done" },
        };

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
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

        private static List<DialogueData> LoadDialogues()
        {
            var dialogues = new List<DialogueData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Dialogue"), "*.json"))
            {
                dialogues.Add(DialogueData.FromJson(ReadObject(file)));
            }

            return dialogues;
        }

        private static QuestSystem NewGame(float hour)
        {
            var quests = new QuestSystem { Clock = () => hour };
            quests.Load(LoadQuests());
            return quests;
        }

        private static string ExpectedTopic(string questId, string npcId)
        {
            for (int i = 0; i < Expected.Length; i++)
            {
                if (Expected[i][0] == questId && Expected[i][1] == npcId)
                {
                    return Expected[i][2];
                }
            }

            return null;
        }

        /// <summary>前提を（前提の前提まで）完了にし、quest を最後の talk の手前まで進めたセーブを読んだ状態にする。</summary>
        private static void RestoreJustBeforeTheLastTalk(QuestSystem quests, QuestData quest)
        {
            var chain = new List<string>();
            AddPrerequisites(quests, quest, chain);

            var progress = new QuestProgress();
            for (int i = 0; i < chain.Count; i++)
            {
                progress.QuestIds.Add(chain[i]);
                progress.StepsDone.Add(quests.Find(chain[i]).Steps.Count);
                progress.States.Add(QuestProgress.StateCompleted);
            }

            progress.QuestIds.Add(quest.Id);
            progress.StepsDone.Add(quest.Steps.Count - 1);
            progress.States.Add(QuestProgress.StateActive);
            quests.Restore(progress);
        }

        private static void AddPrerequisites(QuestSystem quests, QuestData quest, List<string> chain)
        {
            for (int i = 0; i < quest.Prerequisites.Count; i++)
            {
                QuestData prerequisite = quests.Find(quest.Prerequisites[i]);
                Assert.IsNotNull(prerequisite, quest.Id + " の前提 " + quest.Prerequisites[i] + " が無い");
                AddPrerequisites(quests, prerequisite, chain);
                if (!chain.Contains(prerequisite.Id))
                {
                    chain.Add(prerequisite.Id);
                }
            }
        }

        /// <summary>DialogueSystem.TalkTo と同じ順（選ぶ → talk を報告してお礼に差し替える）で、最初に出る話題を返す。</summary>
        private static DialogueTopic Talk(DialogueData npc, QuestSystem quests, ICollection<string> spent)
        {
            DialogueTopic chosen = DialogueSystem.SelectTopic(npc, quests, spent);
            Assert.IsNotNull(chosen, npc.Id + " に出せる話題が無い");
            return DialogueSystem.ReportTalkAndSelect(npc, quests, spent, chosen);
        }

        /// <summary>DialogueSystem.Finish と同じ後始末。</summary>
        private static void Finish(DialogueData npc, DialogueTopic topic, QuestSystem quests, ICollection<string> spent)
        {
            if (topic.Once)
            {
                spent.Add(npc.Id + "/" + topic.Id);
            }

            if (!string.IsNullOrEmpty(topic.StartsQuest))
            {
                quests.StartQuest(topic.StartsQuest);
            }

            if (!string.IsNullOrEmpty(topic.SetsFlag))
            {
                quests.ReportFlag(topic.SetsFlag);
            }
        }

        [Test]
        public void EveryNpc_TalkThatCompletesAQuest_ShowsTheExpectedTopic()
        {
            List<DialogueData> dialogues = LoadDialogues();
            Assert.IsNotEmpty(dialogues);
            var checkedPairs = new HashSet<string>();

            for (int d = 0; d < dialogues.Count; d++)
            {
                DialogueData npc = dialogues[d];
                QuestSystem probe = NewGame(EveningHour);

                for (int q = 0; q < probe.All.Count; q++)
                {
                    QuestData quest = probe.All[q];
                    QuestStep last = quest.Steps[quest.Steps.Count - 1];
                    if (last.Kind != QuestStepKind.Talk || last.Target != npc.Id)
                    {
                        continue;
                    }

                    string expected = ExpectedTopic(quest.Id, npc.Id);
                    Assert.IsNotNull(expected,
                        quest.Id + " は " + npc.Id + " と話して達成するのに、その会話で出る話題が表に無い");

                    QuestSystem quests = NewGame(EveningHour);
                    RestoreJustBeforeTheLastTalk(quests, quests.Find(quest.Id));
                    Assert.IsTrue(quests.IsActive(quest.Id), quest.Id + " を最後の talk の手前にできない");

                    DialogueTopic shown = Talk(npc, quests, new HashSet<string>());

                    Assert.IsTrue(quests.IsCompleted(quest.Id), quest.Id + " が " + npc.Id + " と話しても達成にならない");
                    Assert.AreEqual(expected, shown.Id, quest.Id + " を " + npc.Id + " と話して達成した会話の話題");
                    checkedPairs.Add(quest.Id + "/" + npc.Id);
                }
            }

            for (int i = 0; i < Expected.Length; i++)
            {
                Assert.IsTrue(checkedPairs.Contains(Expected[i][0] + "/" + Expected[i][1]),
                    Expected[i][0] + " を " + Expected[i][1] + " と話して達成する組み合わせがデータに無い（表が古い）");
            }
        }

        [Test]
        public void TalkThatDoesNotComplete_KeepsTheActiveHint()
        {
            // 牛乳を持たずに要に話しかけたら、これまでどおり受注中のヒント。
            List<DialogueData> dialogues = LoadDialogues();
            DialogueData kaname = dialogues.Find(d => d.Id == "kaname");
            QuestSystem quests = NewGame(EveningHour);
            var progress = new QuestProgress();
            progress.QuestIds.AddRange(new[] { "q_orientation", "q_lunch" });
            progress.StepsDone.AddRange(new[] { 3, 0 });
            progress.States.AddRange(new[] { QuestProgress.StateCompleted, QuestProgress.StateActive });
            quests.Restore(progress);

            DialogueTopic shown = Talk(kaname, quests, new HashSet<string>());

            Assert.IsTrue(quests.IsActive("q_lunch"));
            Assert.AreEqual("t_active", shown.Id);
        }

        [Test]
        public void MilkDelivered_ThanksInTheSameTalk_ThenIdleOrNextRequest()
        {
            List<DialogueData> dialogues = LoadDialogues();
            DialogueData kaname = dialogues.Find(d => d.Id == "kaname");
            QuestSystem quests = NewGame(EveningHour);
            var spent = new HashSet<string>();
            var progress = new QuestProgress();
            progress.QuestIds.AddRange(new[] { "q_orientation", "q_lunch" });
            progress.StepsDone.AddRange(new[] { 3, 0 });
            progress.States.AddRange(new[] { QuestProgress.StateCompleted, QuestProgress.StateActive });
            quests.Restore(progress);

            quests.ReportCollect("milk");
            DialogueTopic first = Talk(kaname, quests, spent);
            Assert.AreEqual("t_done", first.Id, "牛乳を届けた会話でお礼が出ない");
            Finish(kaname, first, quests, spent);

            DialogueTopic second = Talk(kaname, quests, spent);
            Assert.AreNotEqual("t_done", second.Id, "お礼（once）を二度言う");
            Assert.AreNotEqual("t_active", second.Id, "達成したのに受注中のヒントを言う");
        }

        [Test]
        public void Orientation_FirstGreetingStaysFirst_GymRequestComesNext()
        {
            List<DialogueData> dialogues = LoadDialogues();
            DialogueData prof = dialogues.Find(d => d.Id == "prof");
            QuestSystem quests = NewGame(10f);
            var spent = new HashSet<string>();
            quests.ReportVisit("gate_main", true);
            quests.ReportVisit("campus_mall", true);

            DialogueTopic first = Talk(prof, quests, spent);
            Assert.IsTrue(quests.IsCompleted("q_orientation"));
            Assert.AreEqual("t_welcome", first.Id, "オリエンテーションを終えた会話で最初のあいさつが出ない");
            Finish(prof, first, quests, spent);

            DialogueTopic second = Talk(prof, quests, spent);
            Assert.AreEqual("t_gym_start", second.Id);
            Finish(prof, second, quests, spent);
            Assert.IsTrue(quests.IsActive("q_gym"), "体育館の依頼を受けられない");
        }

        private const string ThanksQuestJson =
            "{\"id\":\"q_a\",\"order\":1,\"steps\":[{\"id\":\"s1\",\"type\":\"talk\",\"target\":\"npc\"}]}";

        private const string NextQuestJson =
            "{\"id\":\"q_b\",\"order\":2,\"prerequisites\":[\"q_a\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"gym\"}]}";

        /// <summary>お礼より上に「q_a を終えたら q_b を頼む」話題がある NPC。</summary>
        private const string NpcJson =
            "{\"id\":\"npc\",\"topics\":[" +
            "{\"id\":\"t_request\",\"once\":true,\"requiresCompletedQuest\":\"q_a\",\"startsQuest\":\"q_b\",\"lines\":[\"r\"]}," +
            "{\"id\":\"t_thanks\",\"once\":true,\"requiresCompletedQuest\":\"q_a\",\"lines\":[\"t\"]}," +
            "{\"id\":\"t_active\",\"requiresActiveQuest\":\"q_a\",\"lines\":[\"h\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"i\"]}]}";

        private static QuestSystem SyntheticQuests()
        {
            var quests = new QuestSystem();
            quests.Load(new[]
            {
                QuestData.FromJson(MiniJson.Deserialize(ThanksQuestJson) as Dictionary<string, object>),
                QuestData.FromJson(MiniJson.Deserialize(NextQuestJson) as Dictionary<string, object>),
            });
            quests.StartQuest("q_a");
            return quests;
        }

        private static DialogueData SyntheticNpc()
        {
            return DialogueData.FromJson(MiniJson.Deserialize(NpcJson) as Dictionary<string, object>);
        }

        [Test]
        public void RequestTopic_IsNotUsedAsThanks()
        {
            QuestSystem quests = SyntheticQuests();

            DialogueTopic shown = Talk(SyntheticNpc(), quests, new HashSet<string>());

            Assert.IsTrue(quests.IsCompleted("q_a"));
            Assert.AreEqual("t_thanks", shown.Id, "依頼の話題をお礼の代わりに出している");
        }

        [Test]
        public void SpentThanks_KeepsTheTopicChosenBeforeTheReport()
        {
            QuestSystem quests = SyntheticQuests();
            var spent = new HashSet<string> { "npc/t_thanks" };

            DialogueTopic shown = Talk(SyntheticNpc(), quests, spent);

            Assert.IsTrue(quests.IsCompleted("q_a"));
            Assert.AreEqual("t_active", shown.Id, "使い終えたお礼を出している");
        }

        [Test]
        public void QuestCompletedEarlier_IsNotThankedAgain()
        {
            QuestSystem quests = SyntheticQuests();
            quests.ReportTalk("npc");
            Assert.IsTrue(quests.IsCompleted("q_a"));
            var spent = new HashSet<string>();

            DialogueTopic chosen = DialogueSystem.SelectTopic(SyntheticNpc(), quests, spent);
            DialogueTopic shown = DialogueSystem.ReportTalkAndSelect(SyntheticNpc(), quests, spent, chosen);

            Assert.AreSame(chosen, shown, "この会話で達成していないクエストのお礼に差し替えている");
        }

        [Test]
        public void NoQuestSystem_ReturnsTheChosenTopic()
        {
            DialogueData npc = SyntheticNpc();
            DialogueTopic chosen = DialogueSystem.SelectTopic(npc, null, null);

            Assert.AreSame(chosen, DialogueSystem.ReportTalkAndSelect(npc, null, null, chosen));
        }
    }
}
