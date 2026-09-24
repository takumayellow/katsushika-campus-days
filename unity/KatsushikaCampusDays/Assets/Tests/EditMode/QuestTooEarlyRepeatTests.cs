using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 時刻の条件（minHour）より前に着いたときの「◯時ごろにまた来よう」は、同じステップにつき一日に 1 回 (#54)。
    /// 入った瞬間ごとに知らせると、時刻前に水盤や正門を出入りするたびにトーストが出る。
    /// 時計が戻ったら（翌朝に戻した・前の時刻のセーブを読んだ）新しい一日として、来たらもう一度知らせる。
    /// 留まっているあいだの報告し直しで出さないことは <see cref="QuestTooEarlyTests"/> が見ている。
    /// </summary>
    public sealed class QuestTooEarlyRepeatTests
    {
        private const string SunsetJson =
            "{\"id\":\"q_sunset\",\"order\":7,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"library_pond\",\"minHour\":18}]}";

        /// <summary>同じ水盤で、別の時刻の条件を持つもう 1 件。</summary>
        private const string PondLateJson =
            "{\"id\":\"q_pond_late\",\"order\":8,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"library_pond\",\"minHour\":19}]}";

        private const string NightWalkJson =
            "{\"id\":\"q_night\",\"order\":16,\"autoStart\":true," +
            "\"steps\":[{\"id\":\"s1\",\"type\":\"visit\",\"target\":\"gate_main\",\"minHour\":19}]}";

        private float _hour;
        private QuestSystem _quests;
        private List<string> _told;

        private static List<QuestData> Parse(params string[] jsons)
        {
            var list = new List<QuestData>();
            for (int i = 0; i < jsons.Length; i++)
            {
                list.Add(QuestData.FromJson(MiniJson.Deserialize(jsons[i]) as Dictionary<string, object>));
            }

            return list;
        }

        /// <summary>時計を _hour に向けた QuestSystem を作り、知らせたステップを「クエスト id/ステップ id」で数える。</summary>
        private void Make(float hour, params string[] jsons)
        {
            _hour = hour;
            _told = new List<string>();
            _quests = new QuestSystem { Clock = () => _hour };
            _quests.Load(Parse(jsons));
            _quests.StepTooEarly += (quest, step) => _told.Add(quest.Id + "/" + step.Id);
        }

        /// <summary>VisitZone.OnTriggerEnter と同じ、入った瞬間の報告。</summary>
        private void Arrive(string placeId)
        {
            _quests.ReportVisit(placeId, true);
        }

        /// <summary>時計を進めて、GameManager.Update と同じく Tick を 1 回呼ぶ。</summary>
        private void PassTimeTo(float hour)
        {
            _hour = hour;
            _quests.Tick(0f);
        }

        [Test]
        public void LeavingAndComingBackBeforeTheHour_TellsOnlyOnce()
        {
            Make(10f, SunsetJson);

            Arrive("library_pond");
            _quests.ReportVisit("library_pond", false);
            PassTimeTo(10.5f);
            Arrive("library_pond");
            PassTimeTo(15f);
            Arrive("library_pond");

            CollectionAssert.AreEqual(new[] { "q_sunset/s1" }, _told, "同じ日に入り直すたびに「また来よう」が出ている");
            Assert.AreEqual(0, _quests.Find("q_sunset").Steps[0].Progress, "時刻前なのに進んだ");
            Assert.IsTrue(_quests.IsActive("q_sunset"));
        }

        [Test]
        public void ComingBackAfterTheHour_StillCompletes()
        {
            Make(17f, SunsetJson);

            Arrive("library_pond");
            PassTimeTo(18.2f);
            Arrive("library_pond");

            Assert.IsTrue(_quests.IsCompleted("q_sunset"), "一度知らせたステップが時刻になっても達成にならない");
            CollectionAssert.AreEqual(new[] { "q_sunset/s1" }, _told);
        }

        [Test]
        public void NextMorning_TellsAgain_EvenLaterInTheDayThanTheFirstTime()
        {
            Make(10f, SunsetJson);
            Arrive("library_pond");

            // その日は水盤に戻らずに一日を終え、DayEndEvaluator.BeginNextDay が時計を朝に戻す。
            PassTimeTo(19.9f);
            PassTimeTo(DayRestart.DayStartHour);
            PassTimeTo(15f);

            // 1 日目に知らせた 10 時より遅い時刻でも、翌日なのでもう一度知らせる。
            Arrive("library_pond");
            Assert.AreEqual(2, _told.Count, "翌日に来ても「また来よう」が出ない");

            // 2 日目も、その日のうちは 1 回だけ。
            Arrive("library_pond");
            Assert.AreEqual(2, _told.Count, "翌日も入り直すたびに出ている");
        }

        [Test]
        public void ClockGoingBack_IsNoticedAtTheNextArrivalWithoutATick()
        {
            Make(15f, SunsetJson);
            Arrive("library_pond");

            _hour = 9f;
            Arrive("library_pond");

            Assert.AreEqual(2, _told.Count, "時計が戻ったあとの到着で知らせていない");
        }

        [Test]
        public void StayReportsBeforeTheFirstArrival_DoNotUseUpTheNotice()
        {
            // セーブを読んだ直後など、入った瞬間より先に留まっている報告が来ることがある。知らせ済みにしてはいけない。
            Make(10f, SunsetJson);

            _quests.ReportVisit("library_pond", false);
            _quests.ReportVisit("library_pond");
            Arrive("library_pond");

            CollectionAssert.AreEqual(new[] { "q_sunset/s1" }, _told);
        }

        [Test]
        public void EachStepIsToldOnItsOwn()
        {
            Make(12f, SunsetJson, PondLateJson, NightWalkJson);

            Arrive("library_pond");
            Arrive("gate_main");
            Arrive("library_pond");
            Arrive("gate_main");

            // 同じ水盤でも、時刻の違うステップはそれぞれ 1 回。場所が違えば別に 1 回。
            CollectionAssert.AreEqual(new[] { "q_sunset/s1", "q_pond_late/s1", "q_night/s1" }, _told);
        }

        [Test]
        public void RestoringASave_ForgetsTheNotice()
        {
            Make(10f, SunsetJson);
            Arrive("library_pond");

            _quests.Restore(_quests.Capture());
            Arrive("library_pond");

            Assert.AreEqual(2, _told.Count, "セーブを読み直したあとに来ても知らせない");
            Assert.IsTrue(_quests.IsActive("q_sunset"));
        }

        [Test]
        public void LoadingTheQuestsAgain_ForgetsTheNotice()
        {
            Make(10f, SunsetJson);
            Arrive("library_pond");

            // ResetForNewGame（はじめから）も Load を通る。
            _quests.Load(Parse(SunsetJson));
            Arrive("library_pond");

            Assert.AreEqual(2, _told.Count, "読み込み直したあとに来ても知らせない");
        }

        [Test]
        public void RealData_NightWalkAtTheMainGate_IsToldOnceADay()
        {
            // 実データの q_sq_night_walk（19 時以降に正門）。前提の q_sunset までを終えたセーブから。
            _hour = 12f;
            _told = new List<string>();
            _quests = new QuestSystem { Clock = () => _hour };
            _quests.LoadFromResources();
            var progress = new QuestProgress();
            string[] done = { "q_orientation", "q_library", "q_park", "q_sunset" };
            for (int i = 0; i < done.Length; i++)
            {
                progress.QuestIds.Add(done[i]);
                progress.StepsDone.Add(_quests.Find(done[i]).Steps.Count);
                progress.States.Add(QuestProgress.StateCompleted);
            }

            progress.QuestIds.Add("q_sq_night_walk");
            progress.StepsDone.Add(0);
            progress.States.Add(QuestProgress.StateActive);
            _quests.Restore(progress);
            Assert.IsTrue(_quests.IsActive("q_sq_night_walk"));
            _quests.StepTooEarly += (quest, step) => _told.Add(quest.Id + "/" + step.Id);

            Arrive("gate_main");
            PassTimeTo(14f);
            Arrive("gate_main");
            PassTimeTo(18.5f);
            Arrive("gate_main");

            CollectionAssert.AreEqual(new[] { "q_sq_night_walk/s1" }, _told, "昼に正門を通るたびに「また来よう」が出ている");

            PassTimeTo(19.2f);
            Arrive("gate_main");

            Assert.IsTrue(_quests.IsCompleted("q_sq_night_walk"), "19 時を過ぎて正門に来ても達成にならない");
            Assert.AreEqual(1, _told.Count);
        }
    }
}
