using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// サブクエスト「306 の居残り」（q_sq_first_program）を実データで最初から最後までなぞる (#392)。
    /// 「黒板の 1 バイト」を終えた報告のあと教授が 306 へ行くよう頼み、TA に会い、最前列の PC で外れを選んでも
    /// やり直せ、セミコロンを足すと段が進み、TA に見せてから教授に報告して終わる。
    /// 会話は DialogueSystem と同じ順（話しかけた時点で talk を報告し、閉じたときに依頼を受ける）で進める。
    /// </summary>
    public sealed class FirstProgramQuestFlowTests
    {
        private const string HelloId = "q_sq_hello_world";
        private const string ProgramId = "q_sq_first_program";

        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private QuestSystem _quests;
        private HashSet<string> _spent;

        [SetUp]
        public void SetUp()
        {
            NpcAffinity.Reset();
            _spent = new HashSet<string>();

            // 黒板の 1 バイトの前提（はじめての講義）はここでは見ない。そこまでの道は QuestAchievabilityTests が見る。
            Dictionary<string, object> hello = Read("Quests", HelloId);
            hello["prerequisites"] = new List<object>();
            _quests = new QuestSystem();
            _quests.Load(new[] { QuestData.FromJson(hello), QuestData.FromJson(Read("Quests", ProgramId)) }, true);
        }

        [TearDown]
        public void TearDown() => NpcAffinity.Reset();

        [Test]
        public void 黒板の1バイトを終えると306へ行き_PCを動かして教授に報告して終わる()
        {
            DialogueData lecture = Dialogue("lecture");
            DialogueData ta = Dialogue("ta_306");
            DialogueData pc = Dialogue("pc_306");

            Assert.AreEqual("t_idle", Talk(ta).Id, "受注前の TA はいつもの話");
            Assert.AreEqual("t_idle", Talk(pc).Id, "受注前の PC はログイン画面");

            CompleteHelloWorld(lecture);
            Assert.IsTrue(_quests.IsCompleted(HelloId));
            Assert.IsFalse(_quests.IsActive(ProgramId), "黒板のお礼の会話では、まだ次を頼まない");

            Assert.AreEqual("t_program_start", Talk(lecture).Id, "次に話しかけたら 306 へ行くよう頼む");
            Assert.IsTrue(_quests.IsActive(ProgramId));
            Assert.AreEqual("s1", _quests.CurrentStepId(ProgramId));
            Assert.AreEqual("t_program_active", Talk(lecture).Id, "受注中は 306 の場所を言う");

            Assert.AreEqual("t_idle", Talk(pc).Id, "TA に会う前の PC はログイン画面のまま");
            Assert.AreEqual("t_meet", Talk(ta).Id);
            Assert.AreEqual("s2", _quests.CurrentStepId(ProgramId));
            Assert.AreEqual("t_run", Talk(ta).Id, "PC を動かすまで TA は PC へ促す");

            DialogueTopic compile = Talk(pc);
            Assert.AreEqual("t_compile", compile.Id);
            foreach (DialogueChoice wrong in compile.Choices.FindAll(c => c.Retry))
            {
                Choose(pc, compile, wrong);
                Assert.AreEqual("s2", _quests.CurrentStepId(ProgramId), wrong.Text + " では段が進まない");
                Assert.AreEqual("t_compile", Talk(pc).Id, wrong.Text + " のあとも、もう一度直せる");
            }

            Choose(pc, compile, compile.Choices.Find(c => c.SetsFlag == "first_program_ran"));
            Assert.AreEqual("s3", _quests.CurrentStepId(ProgramId));
            Assert.AreEqual("t_ran", Talk(pc).Id, "動いたあとの画面には HELLO, WORLD が残る");

            Assert.AreEqual("t_show", Talk(ta).Id, "TA に見せると 7F の白板の WORLD. の話になる");
            Assert.AreEqual("s4", _quests.CurrentStepId(ProgramId));
            Assert.AreEqual("t_wait_report", Talk(ta).Id);

            Assert.AreEqual("t_program_done", Talk(lecture).Id, "報告した会話でそのままお礼を言う");
            Assert.IsTrue(_quests.IsCompleted(ProgramId));
            Assert.AreEqual("t_done", Talk(ta).Id);
            Assert.AreNotEqual("t_program_start", Talk(lecture).Id, "終えた依頼を二度頼まない");
        }

        [Test]
        public void PCの問いは正解の選択肢だけが段を進め_外れは答え直せる()
        {
            QuestData quest = QuestData.FromJson(Read("Quests", ProgramId));
            QuestStep flag = quest.Steps.Find(s => s.Kind == QuestStepKind.Flag);
            Assert.IsNotNull(flag);

            DialogueTopic answer = Dialogue("pc_306").Topics.Find(t => t.RequiresStep == ProgramId + "/" + flag.Id);
            Assert.IsNotNull(answer, "pc_306 に " + flag.Id + " の段の話題が無い");
            List<DialogueChoice> setters = answer.Choices.FindAll(c => c.SetsFlag == flag.Target);
            Assert.AreEqual(1, setters.Count);
            Assert.IsFalse(setters[0].Retry);
            Assert.IsTrue(answer.Choices.TrueForAll(c => c.SetsFlag == flag.Target || c.Retry), "外れの選択肢は答え直せる");
        }

        /// <summary>黒板 5 枚を読み、白板に答え、教授に報告する。報告の会話は黒板のお礼になる。</summary>
        private void CompleteHelloWorld(DialogueData lecture)
        {
            Assert.IsTrue(_quests.StartQuest(HelloId));
            QuestData hello = _quests.Find(HelloId);
            foreach (QuestStep step in hello.Steps)
            {
                if (step.Kind == QuestStepKind.Flag)
                {
                    _quests.ReportFlag(step.Target);
                }
                else if (step.Target == lecture.Id)
                {
                    Assert.AreEqual("t_hello_done", Talk(lecture).Id);
                }
                else
                {
                    _quests.ReportTalk(step.Target);
                }
            }
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

        /// <summary>DialogueSystem.Choose と同じ: 答えを覚え（retry は覚えない）、フラグを報告する。</summary>
        private void Choose(DialogueData data, DialogueTopic topic, DialogueChoice choice)
        {
            Assert.IsNotNull(choice);
            DialogueSystem.ApplyChoice(data.Id, data.Id + "/" + topic.Id, choice);
            if (!string.IsNullOrEmpty(choice.SetsFlag))
            {
                _quests.ReportFlag(choice.SetsFlag);
            }
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
