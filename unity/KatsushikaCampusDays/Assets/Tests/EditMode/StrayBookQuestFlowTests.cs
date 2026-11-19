using System.Collections.Generic;
using System.IO;
using KCD.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// サブクエスト「迷子の本」（q_sq_stray_book）を実データで最初から最後までなぞる (#410)。
    /// 公園の依頼を終えたいなりが頼み、図書館に入り、2 階の閲覧席で本を拾い、1 階の司書に返し、いなりに報告して終わる。
    /// 本と司書の置き場所はステップの place に書き、<see cref="QuestPlaceStage"/> がシーンに置く。
    /// 会話は DialogueSystem と同じ順（話しかけた時点で talk を報告し、閉じたときに依頼を受ける）で進める。
    /// </summary>
    public sealed class StrayBookQuestFlowTests
    {
        private const string ParkId = "q_park";
        private const string BookId = "q_sq_stray_book";

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private QuestSystem _quests;
        private HashSet<string> _spent;

        [SetUp]
        public void SetUp()
        {
            NpcAffinity.Reset();
            _spent = new HashSet<string>();
            _quests = new QuestSystem();
            _quests.Load(new[] { QuestData.FromJson(Read("Quests", ParkId)), QuestData.FromJson(Read("Quests", BookId)) }, true);
        }

        [TearDown]
        public void TearDown() => NpcAffinity.Reset();

        [Test]
        public void 公園の依頼のあといなりが頼み_閲覧席の本を司書に返して報告すると終わる()
        {
            DialogueData inari = Dialogue("inari");
            DialogueData librarian = Dialogue("librarian");

            Assert.AreEqual("t_default", Talk(librarian).Id, "受注前の司書はいつもの案内");
            CompletePark();

            Assert.AreEqual("t_sq_stray_book_start", Talk(inari).Id, "公園のあと、いなりが迷子の本を頼む");
            Assert.IsTrue(_quests.IsActive(BookId));
            Assert.AreEqual("s1", _quests.CurrentStepId(BookId));

            _quests.ReportEnter("library");
            Assert.AreEqual("s2", _quests.CurrentStepId(BookId));
            Assert.AreEqual("t_default", Talk(librarian).Id, "本を拾う前の司書はいつもの案内");

            _quests.ReportCollect(CollectableMeshes.StrayBookId);
            Assert.AreEqual("s3", _quests.CurrentStepId(BookId));

            Assert.AreEqual("t_return", Talk(librarian).Id, "本を持って行くと司書が受け取る");
            Assert.AreEqual("s4", _quests.CurrentStepId(BookId));

            Talk(inari);
            Assert.IsTrue(_quests.IsCompleted(BookId), "いなりに報告すると終わる");
            Assert.AreEqual("t_default", Talk(librarian).Id, "終えたあとの司書はいつもの案内に戻る");
        }

        [Test]
        public void 受注前に本を拾っていても_図書館に入れば司書の段まで進む()
        {
            CompletePark();
            _quests.ReportCollect(CollectableMeshes.StrayBookId);
            Assert.AreEqual("t_sq_stray_book_start", Talk(Dialogue("inari")).Id);

            _quests.ReportEnter("library");
            Assert.AreEqual("s3", _quests.CurrentStepId(BookId), "先に拾った本で collect の段が済む");
        }

        [Test]
        public void 本と司書の置き場所がステップに書いてある()
        {
            List<QuestPlaceStage.Place> places =
                QuestPlaceStage.Parse(File.ReadAllText(Path.Combine(DataRoot, "Quests", BookId + ".json")));

            QuestPlaceStage.Place book = places.Find(p => p.IsCollect);
            Assert.AreEqual(CollectableMeshes.StrayBookId, book.Target);
            Assert.AreEqual("library", book.Building);
            StringAssert.StartsWith("seat_library_", book.At, "本は 2 階の閲覧席の机に置く");

            QuestPlaceStage.Place librarian = places.Find(p => p.IsTalk);
            Assert.AreEqual("librarian", librarian.Target);
            Assert.AreEqual("library", librarian.Building);
            Assert.AreEqual("poi_library_counter", librarian.Toward, "司書はカウンターの内側に立つ");
            Assert.IsTrue(File.Exists(Path.Combine(DataRoot, "Dialogue", librarian.Target + ".json")), "司書の会話が無い");
        }

        private void CompletePark()
        {
            Assert.IsTrue(_quests.StartQuest(ParkId));
            // 公園の依頼は葉を拾うだけ。数を集める段でも、上限までに必ず終わる。
            for (int i = 0; i < 20 && _quests.IsActive(ParkId); i++)
            {
                _quests.ReportCollect("leaf");
            }

            Assert.IsTrue(_quests.IsCompleted(ParkId));
        }

        /// <summary>DialogueSystem.TalkTo と Finish の順: 選ぶ → talk を報告して選び直す → 閉じたら使い終えて依頼を受ける。</summary>
        private DialogueTopic Talk(DialogueData data)
        {
            DialogueTopic chosen = DialogueSystem.SelectTopic(data, _quests, _spent, 0, _ => false);
            DialogueTopic topic = DialogueSystem.ReportTalkAndSelect(data, _quests, _spent, chosen, 0, _ => false);
            Assert.IsNotNull(topic, data.Id + " に出す話題が無い");

            string key = data.Id + "/" + topic.Id;
            if (DialogueSystem.SpendsOnFinish(topic, key))
            {
                _spent.Add(key);
            }

            if (!string.IsNullOrEmpty(topic.StartsQuest))
            {
                _quests.StartQuest(topic.StartsQuest);
            }

            return topic;
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
