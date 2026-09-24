using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 実データの 13 クエストを、プレイヤーにできる操作（会話・入館・到着・拾得）だけで全部達成できるか (#65)。
    ///
    /// クエストの JSON と会話の JSON は別々に書かれていて、組み合わせたときにしか分からない詰みがあった。
    /// - autoStart のクエストが前提を見ずに最初から受注されていた（QuestSystem.Load）。
    /// - オリエンテーションの前に空・稲荷・要と話すと、前提で断られる依頼の話題（once）を使い切ってしまい、
    ///   コーヒー・図書館・昼食と、その先のサイドクエストが受けられなくなった（DialogueSystem.SelectTopic）。
    /// ここでは会話を DialogueSystem と同じ順（話しかけた時点で talk を報告し、閉じたときに依頼を受ける）でなぞる。
    /// 場所・建物・拾う物がシーンに置かれているかは、シーンを開くテスト（CampusQuestTargetsTests）が見る。
    /// </summary>
    public sealed class QuestAchievabilityTests
    {
        /// <summary>夜の散歩（19 時）より後で、一日の終わり（result.json の day_end_hour = 20 時）より前。</summary>
        private const float EveningHour = 19.5f;

        private const int MaxRounds = 100;

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

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
                QuestData quest = QuestData.FromJson(ReadObject(file));
                Assert.IsNotNull(quest, file);
                quests.Add(quest);
            }

            return quests;
        }

        private static List<DialogueData> LoadDialogues()
        {
            var dialogues = new List<DialogueData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Dialogue"), "*.json"))
            {
                DialogueData data = DialogueData.FromJson(ReadObject(file));
                Assert.IsNotNull(data, file);
                dialogues.Add(data);
            }

            return dialogues;
        }

        private static QuestSystem NewGame(float hour)
        {
            var quests = new QuestSystem { Clock = () => hour };
            quests.Load(LoadQuests());
            return quests;
        }

        /// <summary>プレイヤーの操作をなぞる。会話は NPCTalker.Interact と DialogueSystem.Finish と同じ順で進める。</summary>
        private sealed class Player
        {
            private readonly QuestSystem _quests;
            private readonly List<DialogueData> _dialogues;
            private readonly HashSet<string> _spent = new HashSet<string>();

            public Player(QuestSystem quests, List<DialogueData> dialogues)
            {
                _quests = quests;
                _dialogues = dialogues;
            }

            public void TalkToEveryone()
            {
                for (int i = 0; i < _dialogues.Count; i++)
                {
                    TalkTo(_dialogues[i]);
                }
            }

            private void TalkTo(DialogueData npc)
            {
                DialogueTopic topic = DialogueSystem.SelectTopic(npc, _quests, _spent);
                if (topic == null || topic.Lines.Count == 0)
                {
                    return;
                }

                // NPCTalker.Interact: 会話が始まった時点で talk を報告する。
                _quests.ReportTalk(npc.Id);

                // DialogueSystem.Finish: 閉じたときに once を使い切り、依頼を受け、フラグを立てる。
                if (topic.Once)
                {
                    _spent.Add(npc.Id + "/" + topic.Id);
                }

                if (!string.IsNullOrEmpty(topic.StartsQuest))
                {
                    _quests.StartQuest(topic.StartsQuest);
                }

                if (!string.IsNullOrEmpty(topic.SetsFlag))
                {
                    _quests.ReportFlag(topic.SetsFlag);
                }
            }

            /// <summary>受注中の各クエストの今のステップを 1 回ずつこなす。</summary>
            public void DoCurrentSteps()
            {
                var active = new List<QuestData>();
                for (int i = 0; i < _quests.All.Count; i++)
                {
                    if (_quests.IsActive(_quests.All[i].Id))
                    {
                        active.Add(_quests.All[i]);
                    }
                }

                for (int i = 0; i < active.Count; i++)
                {
                    QuestStep step = active[i].CurrentStep;
                    if (!_quests.IsActive(active[i].Id) || step == null)
                    {
                        continue;
                    }

                    Do(step);
                }
            }

            private void Do(QuestStep step)
            {
                switch (step.Kind)
                {
                    case QuestStepKind.Talk:
                        DialogueData npc = _dialogues.Find(d => d.Id == step.Target);
                        Assert.IsNotNull(npc, "話しかける相手 " + step.Target + " の会話データが無い");
                        TalkTo(npc);
                        break;
                    case QuestStepKind.Enter:
                        _quests.ReportEnter(step.Target);
                        break;
                    case QuestStepKind.Visit:
                        _quests.ReportVisit(step.Target, true);
                        break;
                    case QuestStepKind.Collect:
                        _quests.ReportCollect(step.Target);
                        break;
                    case QuestStepKind.Flag:
                        _quests.ReportFlag(step.Target);
                        break;
                }
            }
        }

        private static List<string> NotCompleted(QuestSystem quests)
        {
            var missing = new List<string>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                if (!quests.IsCompleted(quests.All[i].Id))
                {
                    missing.Add(quests.All[i].Id + (quests.IsActive(quests.All[i].Id) ? "（受注中）" : "（未受注）"));
                }
            }

            return missing;
        }

        [TestCase(true, TestName = "AllThirteenQuests_CanBeCompleted_TalkingToEveryoneFirst")]
        [TestCase(false, TestName = "AllThirteenQuests_CanBeCompleted_DoingStepsFirst")]
        public void AllThirteenQuests_CanBeCompleted(bool talkFirst)
        {
            QuestSystem quests = NewGame(EveningHour);
            var player = new Player(quests, LoadDialogues());
            Assert.AreEqual(13, quests.All.Count, "クエストの数が 13 ではない");

            for (int round = 0; round < MaxRounds && NotCompleted(quests).Count > 0; round++)
            {
                if (talkFirst)
                {
                    player.TalkToEveryone();
                    player.DoCurrentSteps();
                }
                else
                {
                    player.DoCurrentSteps();
                    player.TalkToEveryone();
                }
            }

            List<string> missing = NotCompleted(quests);
            Assert.IsEmpty(missing, "達成できないクエスト: " + string.Join(", ", missing));
        }

        [Test]
        public void TalkingToSoraBeforeOrientation_StillLeavesTheCoffeeRequest()
        {
            List<DialogueData> dialogues = LoadDialogues();
            DialogueData sora = dialogues.Find(d => d.Id == "sora");
            QuestSystem quests = NewGame(EveningHour);
            var spent = new HashSet<string>();

            DialogueTopic before = DialogueSystem.SelectTopic(sora, quests, spent);
            Assert.IsTrue(string.IsNullOrEmpty(before.StartsQuest),
                "オリエンテーション前に受けられない依頼（" + before.StartsQuest + "）を出している");
            if (before.Once)
            {
                spent.Add("sora/" + before.Id);
            }

            quests.ReportVisit("gate_main");
            quests.ReportVisit("campus_mall");
            quests.ReportTalk("prof");
            Assert.IsTrue(quests.IsCompleted("q_orientation"));

            DialogueTopic after = DialogueSystem.SelectTopic(sora, quests, spent);
            Assert.AreEqual("q_coffee", after.StartsQuest, "オリエンテーションの後にコーヒーの依頼を受けられない");
        }

        [Test]
        public void NewGame_OnlyOrientationIsActive()
        {
            QuestSystem quests = NewGame(DayRestart.DayStartHour);

            var active = new List<string>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                if (quests.IsActive(quests.All[i].Id))
                {
                    active.Add(quests.All[i].Id);
                }
            }

            CollectionAssert.AreEqual(new[] { "q_orientation" }, active,
                "前提のそろっていない autoStart まで受注している");
        }

        [Test]
        public void EveryQuestIsStartedSomehow()
        {
            var requested = new HashSet<string>();
            foreach (DialogueData data in LoadDialogues())
            {
                foreach (DialogueTopic topic in data.Topics)
                {
                    if (!string.IsNullOrEmpty(topic.StartsQuest))
                    {
                        requested.Add(topic.StartsQuest);
                    }
                }
            }

            var ids = new HashSet<string>();
            foreach (QuestData quest in LoadQuests())
            {
                ids.Add(quest.Id);
                Assert.IsTrue(quest.AutoStart || requested.Contains(quest.Id),
                    quest.Id + " は autoStart でも会話の依頼（startsQuest）でもなく、受注する手段が無い");
            }

            foreach (string id in requested)
            {
                Assert.IsTrue(ids.Contains(id), "会話が依頼するクエスト " + id + " が存在しない");
            }
        }

        [Test]
        public void TimeConditions_FallBeforeTheEndOfTheDay()
        {
            Dictionary<string, object> result = ReadObject(Path.Combine(DataRoot, "Ending", "result.json"));
            float dayEnd = MiniJson.GetFloat(result, "day_end_hour", 20f);

            foreach (QuestData quest in LoadQuests())
            {
                foreach (QuestStep step in quest.Steps)
                {
                    Assert.Less(step.MinHour, dayEnd,
                        quest.Id + "/" + step.Id + " の時刻（" + step.MinHour + " 時）は一日の終わり（" + dayEnd + " 時）より後");
                }
            }
        }
    }
}
