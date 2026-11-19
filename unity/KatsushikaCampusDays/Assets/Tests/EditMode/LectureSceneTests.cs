using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// はじめての講義で席に着いたら、教授の短い講義（lecture / t_lecture_talk）が流れる (#410)。
    /// 流すのは LectureScene。ここでは流すきっかけと、実データの話題が s3 の段に合っていることを見る。
    /// </summary>
    public sealed class LectureSceneTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        [Test]
        public void 席に着く段の完了だけで講義を流す()
        {
            Assert.IsTrue(LectureScene.Triggers("q_lecture", "s2"));
            Assert.IsFalse(LectureScene.Triggers("q_lecture", "s1"), "講義棟に入っただけでは流さない");
            Assert.IsFalse(LectureScene.Triggers("q_lecture", "s3"), "質問を終えたあとには流さない");
            Assert.IsFalse(LectureScene.Triggers("q_gym", "s2"));
            Assert.IsFalse(LectureScene.Triggers(null, null));
        }

        [Test]
        public void 講義の話題は席に着いたあとの段だけで_英訳もある()
        {
            QuestData quest = QuestData.FromJson(Read("Quests", LectureScene.QuestId));
            List<string> ids = quest.Steps.ConvertAll(s => s.Id);
            int seat = ids.IndexOf(LectureScene.SeatStepId);
            Assert.GreaterOrEqual(seat, 0);
            Assert.AreEqual(QuestStepKind.Visit, quest.Steps[seat].Kind, "席に着く段は場所に着く段");
            Assert.AreEqual(LectureScene.AfterStepId, ids[seat + 1], "席に着いた次の段で講義を聞く");

            DialogueTopic talk = Dialogue(LectureScene.DialogueId).Topics.Find(t => t.Id == LectureScene.TopicId);
            Assert.IsNotNull(talk, "lecture に " + LectureScene.TopicId + " が無い");
            Assert.AreEqual(LectureScene.QuestId + "/" + LectureScene.AfterStepId, talk.RequiresStep);
            Assert.IsTrue(talk.Once, "講義は一度だけ");
            Assert.GreaterOrEqual(talk.Lines.Count, 3);
            foreach (DialogueLine line in talk.Lines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(line.Text));
                Assert.IsFalse(string.IsNullOrEmpty(line.TextEn), line.Text + " に英訳が無い");
            }

            Assert.IsTrue(talk.Lines.Exists(l => l.Text.Contains("101")), "部屋の札と同じ 101 を名乗る");
            Assert.IsTrue(talk.Lines.Exists(l => l.Text.Contains("ビット")), "報酬の「1 ビットの考え方」の話をする");
        }

        [Test]
        public void 講義のあとに教授へ話しかけると_質問の話が出る()
        {
            Dictionary<string, object> node = Read("Quests", LectureScene.QuestId);
            node["prerequisites"] = new List<object>();
            var quests = new QuestSystem();
            quests.Load(new[] { QuestData.FromJson(node) }, true);
            Assert.IsTrue(quests.StartQuest(LectureScene.QuestId));
            quests.ReportEnter("lecture");
            quests.ReportVisit("poi_lecture_hall_seats", true);
            Assert.AreEqual(LectureScene.AfterStepId, quests.CurrentStepId(LectureScene.QuestId));

            DialogueData data = Dialogue(LectureScene.DialogueId);
            var spent = new HashSet<string> { data.Id + "/" + LectureScene.TopicId };
            DialogueTopic chosen = DialogueSystem.SelectTopic(data, quests, spent, 0, _ => false);
            DialogueTopic topic = DialogueSystem.ReportTalkAndSelect(data, quests, spent, chosen, 0, _ => false);

            Assert.IsTrue(quests.IsCompleted(LectureScene.QuestId), "話しかけて質問の段を終える");
            Assert.AreEqual("t_lecture_done", topic.Id, "講義を聞いたあとは「今日の話はここまで」から始まる");
        }

        private static Dictionary<string, object> Read(string folder, string id)
        {
            string path = Path.Combine(DataRoot, folder, id + ".json");
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        private static DialogueData Dialogue(string id) => DialogueData.FromJson(Read("Dialogue", id));
    }
}
