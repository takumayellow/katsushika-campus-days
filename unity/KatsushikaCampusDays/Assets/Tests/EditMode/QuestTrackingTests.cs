using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 追うクエストの選び方 (#410)。受けた直後はそれを追い、Tab の一覧で選び直せる。
    /// 制限時間を数えている最中のものだけは選んだものより先に追い、選んだものが終わったら order の規則に戻る。
    /// あわせて、受注のトーストの文面と、ミニマップが依頼を受けられる人を見分ける判定を見る。
    /// </summary>
    public sealed class QuestTrackingTests
    {
        private const string AJson =
            "{\"id\":\"q_a\",\"title\":\"A\",\"order\":1,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"a\"}]}";

        private const string BJson =
            "{\"id\":\"q_b\",\"title\":\"B\",\"order\":2,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"b\"}]}";

        private const string CJson =
            "{\"id\":\"q_c\",\"title\":\"C\",\"order\":3,\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"c\"}]}";

        /// <summary>依頼主のいない制限時間つき。受けたその場で数え始める。</summary>
        private const string DashJson =
            "{\"id\":\"q_dash\",\"title\":\"Dash\",\"order\":9," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"poi\",\"timeLimit\":30}]}";

        /// <summary>q_b を終えると同時に始まる 2 本。</summary>
        private const string AfterBJson =
            "{\"id\":\"q_after_b\",\"title\":\"After B\",\"order\":4,\"autoStart\":true,\"prerequisites\":[\"q_b\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"d\"}]}";

        private const string AfterBLaterJson =
            "{\"id\":\"q_after_b2\",\"title\":\"After B 2\",\"order\":5,\"autoStart\":true,\"prerequisites\":[\"q_b\"]," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"e\"}]}";

        private const string KanameJson =
            "{\"id\":\"kaname\",\"topics\":[" +
            "{\"id\":\"t_start\",\"once\":true,\"startsQuest\":\"q_a\",\"lines\":[\"a\"]}," +
            "{\"id\":\"t_active\",\"requiresActiveQuest\":\"q_a\",\"lines\":[\"b\"]}," +
            "{\"id\":\"t_idle\",\"lines\":[\"c\"]}]}";

        private static QuestSystem Make(params string[] jsons)
        {
            var quests = new QuestSystem();
            quests.Load(Parse(jsons));
            return quests;
        }

        private static List<QuestData> Parse(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(QuestData.FromJson(MiniJson.Deserialize(jsons[i]) as Dictionary<string, object>));
            }

            return list;
        }

        private static DialogueData Dialogue(string json)
        {
            return DialogueData.FromJson(MiniJson.Deserialize(json) as Dictionary<string, object>);
        }

        private static string Log(QuestSystem quests)
        {
            var builder = new StringBuilder();
            QuestLogView.Compose(builder, quests);
            return builder.ToString();
        }

        private static bool Offers(DialogueData data, QuestSystem quests, ICollection<string> spent)
        {
            return DialogueSystem.OffersQuest(data, quests, spent, 0, _ => false);
        }

        [Test]
        public void StartQuest_TracksTheQuestJustAccepted()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_c");

            Assert.AreEqual("q_c", quests.TrackedQuest.Id, "order が大きくても、受けたばかりのものを追う");
            Assert.AreEqual("q_c", quests.ChosenQuest.Id);
        }

        [Test]
        public void Track_ChoosesOnlyActiveQuests_AndRaisesChangedOnlyWhenItSwitches()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");
            int changed = 0;
            quests.Changed += () => changed++;

            Assert.IsTrue(quests.Track("q_a"));
            Assert.AreEqual("q_a", quests.TrackedQuest.Id);
            Assert.AreEqual(1, changed);

            Assert.IsTrue(quests.Track("q_a"), "選んであるものを選び直しても受け付ける");
            Assert.AreEqual(1, changed, "変わらなければ Changed を出さない");

            Assert.IsFalse(quests.Track("q_c"), "受注していないものは選べない");
            Assert.IsFalse(quests.Track("q_missing"));
            Assert.IsFalse(quests.Track(null));
            Assert.AreEqual("q_a", quests.TrackedQuest.Id);
            Assert.AreEqual(1, changed);
        }

        [Test]
        public void AutoStartedTogether_TracksTheLowestOrder()
        {
            QuestSystem quests = Make(AJson, BJson, AfterBJson, AfterBLaterJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");
            quests.ReportEnter("b");

            Assert.IsTrue(quests.IsActive("q_after_b"));
            Assert.IsTrue(quests.IsActive("q_after_b2"));
            Assert.AreEqual("q_after_b", quests.TrackedQuest.Id, "続けて自動で受けたもののうち order が最も小さいもの（q_a ではない）");
        }

        [Test]
        public void Load_TracksTheFirstAutoStart_ButASilentLoadChoosesNothing()
        {
            QuestSystem quests = Make(AJson, AfterBJson.Replace(",\"prerequisites\":[\"q_b\"]", string.Empty));
            Assert.AreEqual("q_after_b", quests.ChosenQuest.Id, "新しいゲームで自動で受けたものも追う");

            quests.Load(Parse(AJson, AfterBJson.Replace(",\"prerequisites\":[\"q_b\"]", string.Empty)), true);
            Assert.IsNull(quests.ChosenQuest, "黙って作り直したときは選ばない");
            Assert.AreEqual("q_after_b", quests.TrackedQuest.Id, "選んでいなくても規則で追う");
        }

        [Test]
        public void RunningChallenge_BeatsTheChoice_AndTheChoiceSurvivesIt()
        {
            QuestSystem quests = Make(AJson, DashJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_dash");
            Assert.IsTrue(quests.Track("q_a"));

            Assert.AreEqual("q_dash", quests.TrackedQuest.Id, "数えている間は残り秒を見せる");
            Assert.AreEqual("q_a", quests.ChosenQuest.Id, "選んだものは覚えている");

            quests.Tick(31f, false);
            Assert.AreEqual("q_a", quests.TrackedQuest.Id, "数え終わったら選んだものに戻る");
        }

        [Test]
        public void FinishingTheChosenQuest_FallsBackToTheOrderRule()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");
            quests.StartQuest("q_c");
            Assert.IsTrue(quests.Track("q_b"));

            quests.ReportEnter("b");

            Assert.IsNull(quests.ChosenQuest);
            Assert.AreEqual("q_a", quests.TrackedQuest.Id);
        }

        [Test]
        public void FinishingAnotherQuest_KeepsTheChoice()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_c");

            quests.ReportEnter("a");

            Assert.AreEqual("q_c", quests.TrackedQuest.Id);
        }

        [Test]
        public void RestoreAndLoad_ForgetTheChoice()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_c");
            QuestProgress saved = quests.Capture();

            quests.Restore(saved);
            Assert.IsNull(quests.ChosenQuest, "選んだものはセーブに書かない");
            Assert.AreEqual("q_a", quests.TrackedQuest.Id, "読み込んだあとは規則で選ぶ");

            Assert.IsTrue(quests.Track("q_c"));
            quests.Load(Parse(AJson, BJson, CJson));
            Assert.IsNull(quests.ChosenQuest);
        }

        [Test]
        public void Compose_MarksTheTrackedTitle_AndLinksEveryActiveTitle()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");

            string log = Log(quests);

            StringAssert.Contains("<link=\"track:0\"><color=#FFE6A8>" + L.Get("ui.questlog.active") + "A</color></link>", log);
            StringAssert.Contains("<link=\"track:1\"><color=#4FD1D9>" + L.Get("ui.questlog.tracking") + "B</color></link>", log);
            StringAssert.DoesNotContain("track:2", log, "受注していないものは出さない");
        }

        [Test]
        public void Compose_MarksTheChoiceWaitingBehindARunningChallenge()
        {
            QuestSystem quests = Make(AJson, DashJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_dash");
            Assert.IsTrue(quests.Track("q_a"));

            string log = Log(quests);

            StringAssert.Contains(L.Get("ui.questlog.tracking") + "Dash", log);
            StringAssert.Contains(L.Get("ui.questlog.track_next") + "A", log);
        }

        [Test]
        public void NextTracked_CyclesThroughActiveQuestsInListOrder()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");
            quests.StartQuest("q_c");

            Assert.AreEqual("q_a", QuestLogView.NextTracked(quests, 1).Id, "下の端からは上の端へ");
            Assert.AreEqual("q_b", QuestLogView.NextTracked(quests, -1).Id);

            Assert.IsTrue(quests.Track("q_a"));
            Assert.AreEqual("q_c", QuestLogView.NextTracked(quests, -1).Id, "上の端からは下の端へ");
            Assert.AreEqual("q_b", QuestLogView.NextTracked(quests, 1).Id);
            Assert.IsNull(QuestLogView.NextTracked(quests, 0), "押していなければ選ばない");
            Assert.IsNull(QuestLogView.NextTracked(null, 1));
        }

        [Test]
        public void NextTracked_StartsFromTheChoiceWhileAChallengeRuns()
        {
            QuestSystem quests = Make(AJson, BJson, DashJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");
            quests.StartQuest("q_dash");
            Assert.IsTrue(quests.Track("q_a"));

            Assert.AreEqual("q_b", QuestLogView.NextTracked(quests, 1).Id, "数えているものではなく、選んだものから進める");
        }

        [Test]
        public void NextTracked_NeedsTwoActiveQuests()
        {
            QuestSystem quests = Make(AJson, BJson);
            quests.StartQuest("q_a");

            Assert.IsNull(QuestLogView.NextTracked(quests, 1));
            Assert.IsNull(QuestLogView.NextTracked(quests, -1));
        }

        [Test]
        public void TrackLinkTarget_ReadsTheListIndexOfAnActiveQuest()
        {
            QuestSystem quests = Make(AJson, BJson, CJson);
            quests.StartQuest("q_a");
            quests.StartQuest("q_b");

            Assert.AreEqual("q_a", QuestLogView.TrackLinkTarget(quests, "track:0").Id);
            Assert.AreEqual("q_b", QuestLogView.TrackLinkTarget(quests, "track:1").Id);
            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, "track:2"), "受注していない");
            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, "track:9"), "範囲の外");
            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, "track:-1"));
            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, "other:0"));
            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, null));
            Assert.IsNull(QuestLogView.TrackLinkTarget(null, "track:0"));
        }

        [Test]
        public void TrackLinkTarget_WithOneActiveQuest_ChoosesNothing()
        {
            QuestSystem quests = Make(AJson, BJson);
            quests.StartQuest("q_a");

            Assert.IsNull(QuestLogView.TrackLinkTarget(quests, "track:0"), "選ぶものが無いので、叩けばほかの場所と同じく閉じる");
        }

        [Test]
        public void TrackHint_ShowsOnlyWithTwoActiveQuests()
        {
            QuestSystem quests = Make(AJson, BJson);
            quests.StartQuest("q_a");
            Assert.AreEqual(string.Empty, QuestLogView.TrackHint(quests, false));

            quests.StartQuest("q_b");
            StringAssert.Contains(L.Get("ui.questlog.track_hint"), QuestLogView.TrackHint(quests, false));
            StringAssert.Contains(L.Get("ui.questlog.track_hint_touch"), QuestLogView.TrackHint(quests, true));
        }

        [Test]
        public void StartedMessage_NamesTheQuestInTheCurrentLanguage()
        {
            QuestData quest = Parse("{\"id\":\"q_i18n\",\"title\":\"はじめての実験\",\"title_en\":\"First Experiment\"," +
                "\"steps\":[{\"id\":\"s1\",\"type\":\"enter\",\"target\":\"a\"}]}")[0];

            In("ja", () => Assert.AreEqual("クエスト受注: はじめての実験", QuestTrackerView.StartedMessage(quest)));
            In("en", () => Assert.AreEqual("Quest started: First Experiment", QuestTrackerView.StartedMessage(quest)));
        }

        [Test]
        public void OffersQuest_OnlyWhileTheRequestWouldBeChosen()
        {
            DialogueData kaname = Dialogue(KanameJson);
            QuestSystem quests = Make(AJson, BJson);

            Assert.IsTrue(Offers(kaname, quests, new HashSet<string>()));
            Assert.IsFalse(Offers(kaname, quests, new HashSet<string> { "kaname/t_start" }), "使い終えた依頼");
            Assert.IsFalse(Offers(kaname, null, new HashSet<string>()));

            quests.StartQuest("q_a");
            Assert.IsFalse(Offers(kaname, quests, new HashSet<string>()), "受注中");

            quests.ReportEnter("a");
            Assert.IsFalse(Offers(kaname, quests, new HashSet<string>()), "達成済み");
        }

        [Test]
        public void OffersQuest_SkipsRequestsThatCannotStart()
        {
            DialogueData unknown = Dialogue("{\"id\":\"sora\",\"topics\":[" +
                "{\"id\":\"t_x\",\"startsQuest\":\"q_missing\",\"lines\":[\"a\"]}]}");
            DialogueData gated = Dialogue("{\"id\":\"sora\",\"topics\":[" +
                "{\"id\":\"t_after\",\"startsQuest\":\"q_after_b\",\"lines\":[\"a\"]},{\"id\":\"t_idle\",\"lines\":[\"c\"]}]}");
            QuestSystem quests = Make(AJson, BJson, AfterBJson.Replace(",\"autoStart\":true", string.Empty));

            Assert.IsFalse(Offers(unknown, quests, new HashSet<string>()), "無いクエストの依頼");
            Assert.IsFalse(Offers(gated, quests, new HashSet<string>()), "前提がそろっていない");

            quests.StartQuest("q_b");
            quests.ReportEnter("b");
            Assert.IsTrue(Offers(gated, quests, new HashSet<string>()), "前提がそろえば受けられる");
        }

        /// <summary>言語を切り替えて body を走らせ、言語と保存済みの設定を元に戻す（LocalizedTextTests と同じ）。</summary>
        private static void In(string locale, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
                Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
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
