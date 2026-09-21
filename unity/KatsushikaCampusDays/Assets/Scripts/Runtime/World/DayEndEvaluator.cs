using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>リザルトの集計結果。</summary>
    public sealed class ResultData
    {
        public string Rank = "C";
        public string Title = string.Empty;
        public string Comment = string.Empty;
        public int Percent;
        public int Quests;
        public int QuestTotal;
        public int Collectibles;
        public int CollectibleTotal;
        public int Photos;
        public int PhotoTotal;
        public int Buildings;
        public int BuildingTotal;
        public float HoursWalked;
    }

    /// <summary>
    /// 一日の終わり。Ending/result.json の day_end_hour を過ぎたら集計してリザルト画面を出す。
    /// 「もう一日歩く」で時刻を朝に戻すと再び監視に戻る。
    /// </summary>
    public sealed class DayEndEvaluator : MonoBehaviour
    {
        private const string ResourcePath = "KCD/Ending/result";
        private const float DayStartHour = 8.5f;

        private sealed class RankEntry
        {
            public string Rank;
            public float MinPercent;
            public string TitleJa;
            public string TitleEn;
            public string CommentJa;
            public string CommentEn;
        }

        [SerializeField] private ResultScreen _screen;

        private float _dayEndHour = 20f;
        private float _wQuests = 0.45f;
        private float _wCollectibles = 0.30f;
        private float _wPhotos = 0.15f;
        private float _wBuildings = 0.10f;
        private int _tQuests = 13;
        private int _tCollectibles = 20;
        private int _tPhotos = 6;
        private int _tBuildings = 9;
        private readonly List<RankEntry> _ranks = new List<RankEntry>();
        private bool _armed = true;
        private bool _loaded;

        /// <summary>リザルト画面。SceneBuilder が差し込む。</summary>
        public ResultScreen Screen
        {
            get => _screen;
            set => _screen = value;
        }

        /// <summary>今すぐ集計して表示する（スモークやメニューから）。</summary>
        public void EndDayNow()
        {
            EnsureLoaded();
            if (_screen == null)
            {
                return;
            }

            ResultData data = Evaluate();
            _armed = false;
            _screen.Show(data, OnContinue, OnToTitle);
        }

        private void Update()
        {
            if (!_armed || _screen == null || _screen.IsOpen)
            {
                return;
            }

            // 会話やメニューの上には出さない。閉じた次のフレームで出す。
            GameManager manager = GameManager.Instance;
            if (manager == null || !manager.HasEnteredCampus || KCDInput.GameplayBlocked)
            {
                return;
            }

            EnsureLoaded();
            float hours = manager.GameTimeHours;
            if (hours >= _dayEndHour && hours < 24f)
            {
                EndDayNow();
            }
        }

        private void OnContinue()
        {
            DayNightCycle cycle = FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.SetHours(DayStartHour);
            }
            else
            {
                GameManager.Instance.GameTimeHours = DayStartHour;
            }

            _armed = true;
        }

        private void OnToTitle()
        {
            // 次に入るときは朝から。進行（クエスト・拾った物）はメモリに残す。
            GameManager.Instance.GameTimeHours = DayStartHour;
            _armed = true;
            GameManager.Instance.ReturnToTitle();
        }

        /// <summary>達成率 = Σ weight × (達成 / 総数) × 100。ranks を上から見て最初に届いたものを採る。</summary>
        public ResultData Evaluate()
        {
            EnsureLoaded();

            GameManager manager = GameManager.Instance;
            QuestSystem quests = manager != null ? manager.Quests : null;
            int done = 0;
            if (quests != null)
            {
                foreach (QuestData quest in quests.All)
                {
                    if (quests.IsCompleted(quest.Id))
                    {
                        done++;
                    }
                }
            }

            var data = new ResultData
            {
                Quests = Mathf.Min(done, _tQuests),
                QuestTotal = _tQuests,
                Collectibles = Mathf.Min(DayStats.CollectedCount, _tCollectibles),
                CollectibleTotal = _tCollectibles,
                Photos = Mathf.Min(DayStats.PhotoSpotCount, _tPhotos),
                PhotoTotal = _tPhotos,
                Buildings = Mathf.Min(DayStats.BuildingCount, _tBuildings),
                BuildingTotal = _tBuildings,
                HoursWalked = manager != null ? Mathf.Max(0f, manager.GameTimeHours - DayStartHour) : 0f
            };

            float percent = 100f * (
                _wQuests * Ratio(data.Quests, data.QuestTotal) +
                _wCollectibles * Ratio(data.Collectibles, data.CollectibleTotal) +
                _wPhotos * Ratio(data.Photos, data.PhotoTotal) +
                _wBuildings * Ratio(data.Buildings, data.BuildingTotal));
            data.Percent = Mathf.Clamp(Mathf.RoundToInt(percent), 0, 100);

            foreach (RankEntry entry in _ranks)
            {
                if (data.Percent >= entry.MinPercent)
                {
                    data.Rank = entry.Rank;
                    data.Title = L.Pick(entry.TitleJa, entry.TitleEn);
                    data.Comment = L.Pick(entry.CommentJa, entry.CommentEn);
                    break;
                }
            }

            return data;
        }

        private static float Ratio(int value, int total) => total <= 0 ? 0f : (float)value / total;

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("DayEndEvaluator: " + ResourcePath + " が無いので既定値で進める。");
                return;
            }

            var root = MiniJson.Deserialize(asset.text) as Dictionary<string, object>;
            if (root == null)
            {
                return;
            }

            _dayEndHour = MiniJson.GetFloat(root, "day_end_hour", _dayEndHour);
            LoadWeights(MiniJson.GetObject(root, "weights"));
            LoadTotals(MiniJson.GetObject(root, "totals"));
            LoadRanks(MiniJson.GetArray(root, "ranks"));
        }

        private void LoadWeights(Dictionary<string, object> weights)
        {
            if (weights == null)
            {
                return;
            }

            _wQuests = MiniJson.GetFloat(weights, "quests", _wQuests);
            _wCollectibles = MiniJson.GetFloat(weights, "collectibles", _wCollectibles);
            _wPhotos = MiniJson.GetFloat(weights, "photos", _wPhotos);
            _wBuildings = MiniJson.GetFloat(weights, "buildings", _wBuildings);
        }

        private void LoadTotals(Dictionary<string, object> totals)
        {
            if (totals == null)
            {
                return;
            }

            _tQuests = MiniJson.GetInt(totals, "quests", _tQuests);
            _tCollectibles = MiniJson.GetInt(totals, "collectibles", _tCollectibles);
            _tPhotos = MiniJson.GetInt(totals, "photos", _tPhotos);
            _tBuildings = MiniJson.GetInt(totals, "buildings", _tBuildings);
        }

        private void LoadRanks(List<object> ranks)
        {
            _ranks.Clear();
            foreach (object item in ranks)
            {
                if (!(item is Dictionary<string, object> node))
                {
                    continue;
                }

                _ranks.Add(new RankEntry
                {
                    Rank = MiniJson.GetString(node, "rank", "C"),
                    MinPercent = MiniJson.GetFloat(node, "min_percent", 0f),
                    TitleJa = MiniJson.GetString(node, "title_ja"),
                    TitleEn = MiniJson.GetString(node, "title_en"),
                    CommentJa = MiniJson.GetString(node, "comment_ja"),
                    CommentEn = MiniJson.GetString(node, "comment_en")
                });
            }

            // min_percent の高い順に並べておくと、上から最初に届いたものを採るだけで済む。
            _ranks.Sort((a, b) => b.MinPercent.CompareTo(a.MinPercent));
        }
    }
}
