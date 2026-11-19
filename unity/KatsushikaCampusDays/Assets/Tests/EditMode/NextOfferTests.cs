using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 「次の行き先」(#410)。本編を受けていないあいだは、次に受けられる本編の依頼主を追跡枠とミニマップで示す。
    /// ミッションを終えたあと、依頼主に話しかけるまで何をすればいいか分からなくならないようにする。
    /// </summary>
    public sealed class NextOfferTests
    {
        private const string OrientationJson =
            "{\"id\":\"q_o\",\"title\":\"O\",\"order\":1,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"o\"}]}";

        private const string LibraryJson =
            "{\"id\":\"q_lib\",\"title\":\"Lib\",\"order\":2,\"prerequisites\":[\"q_o\"]," +
            "\"offer\":\"水盤でいなりに話しかける\",\"offer_en\":\"Talk to Inari\",\"offerNpc\":\"inari\"," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"library\"}]}";

        private const string LunchJson =
            "{\"id\":\"q_lunch\",\"title\":\"Lunch\",\"order\":3,\"prerequisites\":[\"q_o\"]," +
            "\"offer\":\"食堂の前でかなめに話しかける\",\"offerNpc\":\"kaname\"," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"cafeteria\"}]}";

        /// <summary>案内の無い本編。order が最も小さくても「次の行き先」には出さない。</summary>
        private const string SilentJson =
            "{\"id\":\"q_silent\",\"title\":\"Silent\",\"order\":0,\"prerequisites\":[\"q_o\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"x\"}]}";

        private const string SideJson =
            "{\"id\":\"q_side\",\"title\":\"Side\",\"order\":0,\"side\":true,\"prerequisites\":[\"q_o\"]," +
            "\"offer\":\"寄り道\",\"offerNpc\":\"sora\"," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"y\"}]}";

        /// <summary>受けたその場で数え始める制限時間つきのサブクエスト。</summary>
        private const string SideDashJson =
            "{\"id\":\"q_side_dash\",\"title\":\"Dash\",\"order\":9,\"side\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"poi\",\"timeLimit\":30}]}";

        private static QuestData Parse(string json)
        {
            return QuestData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        private static QuestSystem Make(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(Parse(jsons[i]));
            }

            var quests = new QuestSystem();
            quests.Load(list, true);
            return quests;
        }

        [Test]
        public void FromJson_ReadsOfferAsTalkStep()
        {
            QuestStep step = Parse(LibraryJson).OfferStep;

            Assert.IsNotNull(step);
            Assert.AreEqual(QuestStepKind.Talk, step.Kind);
            Assert.AreEqual("inari", step.Target);
            Assert.AreEqual("水盤でいなりに話しかける", step.Text);
            Assert.AreEqual("Talk to Inari", step.TextEn);
            Assert.IsNull(Parse(SilentJson).OfferStep, "案内の無いクエストに案内のステップができた");
        }

        [Test]
        public void BeforePrerequisites_NoOffer()
        {
            QuestSystem quests = Make(OrientationJson, LibraryJson, LunchJson);

            Assert.IsTrue(quests.IsActive("q_o"));
            Assert.IsNull(quests.NextOffer, "前提がそろう前の依頼を案内している");
            Assert.IsNull(quests.OfferedQuest, "本編を受けているのに次の行き先を出している");
        }

        [Test]
        public void AfterFinishingMission_OffersLowestOrderMainQuestWithOffer()
        {
            QuestSystem quests = Make(OrientationJson, LibraryJson, LunchJson, SilentJson, SideJson);
            quests.ReportEnter("o");

            Assert.IsTrue(quests.IsCompleted("q_o"));
            Assert.AreEqual("q_lib", quests.OfferedQuest?.Id, "本編の次の依頼主を案内していない");

            quests.StartQuest("q_lib");
            Assert.IsNull(quests.OfferedQuest, "本編を受けたあとも次の行き先を出している");

            quests.ReportEnter("library");
            Assert.AreEqual("q_lunch", quests.OfferedQuest?.Id, "終えたら次の依頼へ進まない");

            quests.StartQuest("q_lunch");
            quests.ReportEnter("cafeteria");
            Assert.IsNull(quests.OfferedQuest, "案内の無い本編・サブクエストまで案内している");
        }

        [Test]
        public void OnlySideQuestActive_StillOffersMainQuest()
        {
            QuestSystem quests = Make(OrientationJson, LibraryJson, SideJson);
            quests.ReportEnter("o");
            quests.StartQuest("q_side");

            Assert.AreEqual("q_lib", quests.OfferedQuest?.Id, "サブクエストの陰で本編の次の行き先が消えた");
        }

        [Test]
        public void ChosenOrTimedSideQuest_HidesOffer()
        {
            QuestSystem quests = Make(OrientationJson, LibraryJson, SideJson);
            quests.ReportEnter("o");
            quests.StartQuest("q_side");
            Assert.IsTrue(quests.Track("q_side"));
            Assert.IsNull(quests.OfferedQuest, "自分で選んだクエストより次の行き先を優先している");

            QuestSystem timed = Make(OrientationJson, LibraryJson, SideDashJson);
            timed.ReportEnter("o");
            timed.StartQuest("q_side_dash");
            Assert.IsNull(timed.OfferedQuest, "制限時間を数えている最中に次の行き先へ切り替えた");
        }

        [Test]
        public void OfferLine_UsesCurrentLanguage()
        {
            string line = QuestTrackerView.OfferLine(Parse(LibraryJson));

            StringAssert.Contains(L.Pick("水盤でいなりに話しかける", "Talk to Inari"), line);
            Assert.AreEqual(string.Empty, QuestTrackerView.OfferLine(null));
        }

        [Test]
        public void Minimap_PointsAtOfferNpc_UntilTheQuestIsTaken()
        {
            QuestSystem quests = Make(OrientationJson, LibraryJson);
            quests.ReportEnter("o");

            QuestStep step = Minimap.ObjectiveStep(quests);
            Assert.IsNotNull(step);
            Assert.AreEqual(QuestStepKind.Talk, step.Kind);
            Assert.AreEqual("inari", step.Target);

            quests.StartQuest("q_lib");
            Assert.AreEqual("library", Minimap.ObjectiveStep(quests)?.Target, "受けたあとはクエストの目的地を指す");
        }

        /// <summary>
        /// 本物のデータで、自分で受ける本編（autoStart でないもの）には案内があり、案内の NPC がその依頼を会話で出す人であること。
        /// 食い違うと、案内どおりに話しかけても依頼が出ない。
        /// </summary>
        [Test]
        public void RealData_EveryManualMainQuestHasOfferFromItsQuestGiver()
        {
            string root = Path.Combine(Application.dataPath, "Data");
            var starters = new Dictionary<string, HashSet<string>>();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Dialogue"), "*.json"))
            {
                var node = MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                DialogueData data = DialogueData.FromJson(node);
                Assert.IsNotNull(data, file);
                foreach (DialogueTopic topic in data.Topics)
                {
                    if (string.IsNullOrEmpty(topic.StartsQuest))
                    {
                        continue;
                    }

                    if (!starters.TryGetValue(topic.StartsQuest, out HashSet<string> npcs))
                    {
                        npcs = new HashSet<string>();
                        starters[topic.StartsQuest] = npcs;
                    }

                    npcs.Add(data.Id);
                }
            }

            int checkedCount = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Quests"), "*.json"))
            {
                QuestData quest = Parse(File.ReadAllText(file));
                Assert.IsNotNull(quest, file);
                if (quest.Side || quest.Secret || quest.AutoStart)
                {
                    continue;
                }

                checkedCount++;
                Assert.IsNotNull(quest.OfferStep, quest.Id + " に次の行き先の案内（offer / offerNpc）が無い");
                Assert.IsFalse(string.IsNullOrEmpty(quest.OfferEn), quest.Id + " に英語の案内（offer_en）が無い");
                Assert.IsTrue(starters.TryGetValue(quest.Id, out HashSet<string> givers) && givers.Contains(quest.OfferNpc),
                    quest.Id + " の offerNpc（" + quest.OfferNpc + "）はこの依頼を会話で出さない");
            }

            Assert.Greater(checkedCount, 0, "自分で受ける本編が 1 つも見つからない");
        }
    }
}
