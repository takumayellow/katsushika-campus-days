using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 一日を通しで遊び切れる (#19, #65)。「はじめから」で入り、受注しているクエストの今のステップを順にこなし、
    /// 依頼主に話しかけて次のクエストをもらい、隠しアイテムと写真スポットと建物を全部回り、
    /// 時刻の条件（夕日 18 時・夜の散歩 19 時）まで時計を進める。夜 20 時のリザルトで
    /// クエスト・隠しアイテム・写真・建物がすべて満点になり、ランク S が付く。途中でエラーのログも出ない。
    ///
    /// 操作は <see cref="CampusDriver"/> がシーンに置かれた物を E で調べるのと同じ手順で行い、
    /// QuestSystem に直接は報告しない。クエストの id とシーンの id が食い違えば、そこで止まって失敗文に出る。
    /// </summary>
    public sealed class FullDayPlaythroughTests : SceneTestBase
    {
        /// <summary>依頼主（話しかけるとクエストが始まる人）。データの giver と、話題で始まるクエストの持ち主。</summary>
        private static readonly string[] Givers = { "prof", "inari", "kaname", "sora" };

        /// <summary>解き手の周回の上限。クエスト 16 件 × ステップ数より十分に多い。</summary>
        private const int MaxRounds = 80;

        /// <summary>時刻の条件のあるステップへ時計を進めるとき、条件の時刻からどれだけ過ぎた時刻にするか（時）。</summary>
        private const float PastMinHour = 0.1f;

        /// <summary>最後に時計を置く時刻。result.json の day_end_hour（20:00）の手前で、越えるのは時計に任せる。</summary>
        private const float StopBeforeDayEnd = 19.98f;

        /// <summary>リザルトが出るまで待つ上限（実時間の秒）。</summary>
        private const float ResultTimeoutSeconds = 20f;

        /// <summary>
        /// このテストの上限（ミリ秒）。建物に出入りするたびに暗転と扉の待ち（出てから 3 秒）が入り、
        /// 一日で 30 回ほど出入りするので、ほかのシーンのテスト（<see cref="SceneTestBase.SceneTestTimeoutMs"/>）より長い。
        /// 手元の実測は 150 秒。
        /// </summary>
        private const int PlaythroughTimeoutMs = 600000;

        private QuestSystem _quests;
        private CampusDriver _driver;

        [UnityTest]
        [Timeout(PlaythroughTimeoutMs)]
        public IEnumerator Campus_PlayTheWholeDay_EndsWithEveryCountFullAndRankS()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            DayEndEvaluator evaluator = Object.FindAnyObjectByType<DayEndEvaluator>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsNotNull(cycle, "キャンパスに DayNightCycle が無い");
            Assert.IsNotNull(evaluator, "キャンパスに DayEndEvaluator が無い");
            Assert.IsNotNull(evaluator.Screen, "DayEndEvaluator に ResultScreen が差し込まれていない（SceneBuilder）");
            Assert.IsNotNull(InteriorLoader.Instance, "キャンパスに InteriorLoader が無い");
            Assert.IsNotNull(DialogueSystem.Instance, "キャンパスに DialogueSystem が無い");

            FormalEnding ending = Object.FindAnyObjectByType<FormalEnding>();
            Assert.IsNotNull(ending, "キャンパスに FormalEnding が無い");

            _quests = GameManager.Instance.Quests;
            _driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance, ending);

            // 時計は止めておき、時刻の条件があるステップの前だけ進める。歩き回るのに実時間がかかっても、
            // 夕日より先に一日が終わらない。
            cycle.Paused = true;
            yield return _driver.Settle();

            bool explored = false;
            for (int round = 0; round < MaxRounds; round++)
            {
                if (AllQuestsDone())
                {
                    break;
                }

                string before = Snapshot();
                yield return WorkOnActiveSteps(cycle.Hours);
                if (Snapshot() != before)
                {
                    continue;
                }

                foreach (string giver in Givers)
                {
                    yield return _driver.Talk(giver);
                }

                if (Snapshot() != before)
                {
                    continue;
                }

                if (!explored)
                {
                    explored = true;
                    yield return Explore();
                    continue;
                }

                float next = NextMinHour(cycle.Hours);
                if (next < 0f)
                {
                    break;
                }

                cycle.SetHours(next + PastMinHour);
                yield return null;
            }

            if (!explored)
            {
                yield return Explore();
            }

            Assert.IsTrue(AllQuestsDone(), "クエストを最後までこなせない:" + DescribeUnfinished());

            yield return _driver.GoTo(string.Empty);
            cycle.Paused = false;
            cycle.SetHours(StopBeforeDayEnd);
            float deadline = Time.realtimeSinceStartup + ResultTimeoutSeconds;
            while (!evaluator.Screen.IsOpen && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(evaluator.Screen.IsOpen, ResultTimeoutSeconds + " 秒待っても一日の終わりのリザルトが出ない");

            ResultData result = evaluator.Evaluate();
            string summary = Describe(result) + DescribeProblems();
            Assert.AreEqual(result.QuestTotal, result.Quests, "クエストが満点でない。" + summary);
            Assert.AreEqual(result.CollectibleTotal, result.Collectibles, "隠しアイテムが満点でない。" + summary);
            Assert.AreEqual(result.PhotoTotal, result.Photos, "写真が満点でない。" + summary);
            Assert.AreEqual(result.BuildingTotal, result.Buildings, "建物が満点でない。" + summary);
            Assert.AreEqual(100, result.Percent, summary);
            Assert.AreEqual("S", result.Rank, summary);

            // 写真はこのテストの置き場に保存を頼んでいること。batchmode の Editor では
            // ScreenCapture.CaptureScreenshot がファイルを書かないので、枚数までは見られない。
            string photoFolder = Path.Combine(SaveDirectory, PhotoSystem.FolderName);
            Assert.IsTrue(Directory.Exists(photoFolder), "写真の保存先が " + photoFolder + " になっていない");

            AssertNoErrors("一日の通しプレイ");
        }

        /// <summary>
        /// 受注しているクエストの今のステップを、並び順に 1 つずつこなす。時刻の条件にまだ届かないものは飛ばす。
        /// 時間切れで依頼主に話しかけ直すのを待っているものは、依頼主に話しかける。
        /// </summary>
        private IEnumerator WorkOnActiveSteps(float hour)
        {
            foreach (QuestData quest in ActiveQuests())
            {
                QuestStep step = quest.CurrentStep;
                if (step == null || step.MinHour > hour)
                {
                    continue;
                }

                if (step.AwaitingGiver)
                {
                    yield return _driver.Talk(step.Giver);
                    continue;
                }

                switch (step.Kind)
                {
                    case QuestStepKind.Enter:
                        yield return _driver.Enter(step.Target);
                        break;
                    case QuestStepKind.Visit:
                        yield return _driver.Visit(step.Target);
                        break;
                    case QuestStepKind.Talk:
                        yield return _driver.Talk(step.Target);
                        break;
                    case QuestStepKind.Collect:
                        yield return _driver.Collect(step.Target, Mathf.Max(1, step.Count - step.Progress));
                        break;
                }
            }
        }

        /// <summary>隠しアイテムを全部拾い、写真スポットで全部撮り、まだ入っていない建物に全部入る。</summary>
        private IEnumerator Explore()
        {
            // 建物ごとにまとめて回り、出入りの回数を減らす。
            CollectibleCatalog catalog = CollectibleCatalog.Instance;
            var items = new List<CatalogItem>(catalog.Items);
            items.Sort((a, b) => string.CompareOrdinal(a.Place.Building, b.Place.Building));
            foreach (CatalogItem item in items)
            {
                if (item.IsHidden && !DayStats.HasCollected(item.Id))
                {
                    yield return _driver.Collect(item.Id, 1);
                }
            }

            var spots = new List<CatalogPhotoSpot>(catalog.PhotoSpots);
            spots.Sort((a, b) => string.CompareOrdinal(a.Place.Building, b.Place.Building));
            foreach (CatalogPhotoSpot spot in spots)
            {
                yield return _driver.Photo(spot.Id);
            }

            var buildings = new List<string>();
            foreach (EntranceTrigger entrance in Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Include))
            {
                buildings.Add(entrance.BuildingId);
            }

            buildings.Sort(System.StringComparer.Ordinal);
            foreach (string id in buildings)
            {
                if (!DayStats.HasEntered(id))
                {
                    yield return _driver.Enter(id);
                }
            }

            yield return _driver.GoTo(string.Empty);
        }

        private List<QuestData> ActiveQuests()
        {
            var active = new List<QuestData>();
            foreach (QuestData quest in _quests.All)
            {
                if (_quests.IsActive(quest.Id))
                {
                    active.Add(quest);
                }
            }

            active.Sort((a, b) => a.Order.CompareTo(b.Order));
            return active;
        }

        /// <summary>結果に数えるクエスト（隠しクエストを除く）が全部終わったか。</summary>
        private bool AllQuestsDone()
        {
            foreach (QuestData quest in _quests.All)
            {
                if (!quest.Secret && !_quests.IsCompleted(quest.Id))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>受注しているステップのうち、いまより後の時刻の条件でいちばん早いもの。無ければ -1。</summary>
        private float NextMinHour(float hour)
        {
            float next = float.MaxValue;
            foreach (QuestData quest in ActiveQuests())
            {
                QuestStep step = quest.CurrentStep;
                if (step != null && step.MinHour > hour && step.MinHour < next)
                {
                    next = step.MinHour;
                }
            }

            return next == float.MaxValue ? -1f : next;
        }

        /// <summary>進み具合の指紋。周回の前後で変わらなければ、その手では何も進まなかった。</summary>
        private string Snapshot()
        {
            var text = new StringBuilder();
            foreach (QuestData quest in _quests.All)
            {
                text.Append(quest.Id).Append(_quests.IsActive(quest.Id) ? 'a' : '-')
                    .Append(_quests.IsCompleted(quest.Id) ? 'c' : '-');
                foreach (QuestStep step in quest.Steps)
                {
                    text.Append(step.Completed ? '1' : '0').Append(step.Progress);
                }

                text.Append(';');
            }

            text.Append(DayStats.CollectedCount).Append('/').Append(DayStats.PhotoSpotCount).Append('/')
                .Append(DayStats.BuildingCount);
            return text.ToString();
        }

        private string DescribeUnfinished()
        {
            var text = new StringBuilder();
            foreach (QuestData quest in _quests.All)
            {
                if (quest.Secret || _quests.IsCompleted(quest.Id))
                {
                    continue;
                }

                text.Append("\n  ").Append(quest.Id).Append(_quests.IsActive(quest.Id) ? "（受注中）" : "（未受注）");
                QuestStep step = quest.CurrentStep;
                if (step != null && _quests.IsActive(quest.Id))
                {
                    text.Append(" 今のステップ ").Append(step.Kind).Append(' ').Append(step.Target)
                        .Append(" ").Append(step.Progress).Append('/').Append(step.Count)
                        .Append(" → ").Append(CampusDriver.Locate(step.Kind, step.Target));
                }
            }

            return text + DescribeProblems();
        }

        private string DescribeProblems()
        {
            if (_driver == null || _driver.Problems.Count == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder("\n進めなかった操作:");
            foreach (string problem in _driver.Problems)
            {
                text.Append("\n  ").Append(problem);
            }

            return text.ToString();
        }

        private static string Describe(ResultData result)
        {
            return "クエスト " + result.Quests + "/" + result.QuestTotal +
                   "、隠しアイテム " + result.Collectibles + "/" + result.CollectibleTotal +
                   "、写真 " + result.Photos + "/" + result.PhotoTotal +
                   "、建物 " + result.Buildings + "/" + result.BuildingTotal +
                   "、達成率 " + result.Percent + "%、ランク " + result.Rank +
                   "\n拾った物: " + string.Join(", ", DayStats.SortedCollectedIds()) +
                   "\n撮った場所: " + string.Join(", ", DayStats.SortedPhotoSpotIds()) +
                   "\n入った建物: " + string.Join(", ", DayStats.SortedBuildingIds());
        }
    }
}
