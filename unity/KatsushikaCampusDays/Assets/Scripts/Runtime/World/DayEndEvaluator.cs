using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    /// 「もう一日歩く」を選んだら、暗転して最初と同じスポーンへ戻し、時刻と一日ごとの状態を朝に戻してから再び監視に戻る (#16)。
    /// </summary>
    public sealed class DayEndEvaluator : MonoBehaviour
    {
        private const string ResourcePath = "KCD/Ending/result";
        private const float DayStartHour = DayRestart.DayStartHour;

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
        [SerializeField] private float _fadeSeconds = 0.35f;

        private CanvasGroup _fade;
        private bool _restarting;
        private bool _hasSpawn;
        private Vector3 _spawnPosition;
        private float _spawnYaw;

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

        /// <summary>
        /// day_end_hour を過ぎたのを覚えておく掛け金。会話などの封鎖中も更新するので、
        /// 封鎖中に 24 時をまたいで時計が 0 時に巻き戻っても、封鎖が外れたところでその日を終えられる (#62)。
        /// </summary>
        private bool _dayEndLatched;

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
            _dayEndLatched = false;
            _screen.Show(data, OnContinue, OnToTitle);
        }

        // ---- 純関数（Unity を起動せずにテストする, #62）----

        /// <summary>
        /// 日の終わりの掛け金を今の時刻で更新する。封鎖中も毎フレーム呼ぶ。
        /// ・dayEnd 以上 24 未満なら立てる。
        /// ・朝から dayEnd まで（dayStart 以上 dayEnd 未満）なら落とす。翌朝への巻き戻しやロードで残さない。
        /// ・0 時から朝まで（dayStart 未満）は変えない。封鎖中に 24 時をまたいだ夜は立ったまま残り、
        ///   夜明け前の時刻から始めた場合（スモークの -kcd-time 5 など）は立たない。
        /// </summary>
        public static bool LatchDayEnd(bool latched, float hours, float dayStart, float dayEnd)
        {
            if (hours >= dayEnd && hours < 24f)
            {
                return true;
            }

            if (hours >= dayStart && hours < dayEnd)
            {
                return false;
            }

            return latched;
        }

        /// <summary>
        /// いまリザルトを出すか。掛け金が立っていて、操作の封鎖も裏エンドも無いときだけ。
        /// 裏エンドは閉じるときに ReturnToTitle が封鎖をまとめて外すので、シーンが切り替わるまでの
        /// フレームは封鎖だけでは止まらない。<see cref="DormEnding.IsAnyShowing"/> はシーンが消えるまで立っている。
        /// </summary>
        public static bool ShouldEndDay(bool latched, bool blocked, bool dormEndingShowing)
        {
            return latched && !blocked && !dormEndingShowing;
        }

        /// <summary>
        /// リザルトの「歩いた時間」。24 時をまたいで 0 時台に終わったときは、その夜の分も足す
        /// （引き算だけだと 0 時間になる）。
        /// </summary>
        public static float WalkedHours(float hours, float dayStart)
        {
            float walked = hours >= dayStart ? hours - dayStart : hours + 24f - dayStart;
            return Mathf.Clamp(walked, 0f, 24f);
        }

        private void Awake()
        {
            _fade = BuildFadeOverlay();
            CaptureSpawn();
        }

        /// <summary>
        /// シーンに置かれたままのプレイヤーの位置＝はじめてキャンパスへ入ったときのスポーン（正門側、u = 210）。
        /// セーブの読み込みやワープで動く前に覚える。WorldBounds も同じ手で起動時の位置を覚えている。
        /// </summary>
        private void CaptureSpawn()
        {
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged == null)
            {
                return;
            }

            _spawnPosition = tagged.transform.position;
            _spawnYaw = tagged.transform.eulerAngles.y;
            _hasSpawn = true;
        }

        /// <summary>暗転の途中で止められたら、封鎖と暗幕を残さない（InteriorLoader・WorldBounds と同じ）。</summary>
        private void OnDisable()
        {
            KCDInput.Unblock(this);
            if (_restarting && _fade != null)
            {
                _fade.alpha = 0f;
            }

            _restarting = false;
        }

        private void Update()
        {
            if (!_armed || _restarting || _screen == null || _screen.IsOpen)
            {
                return;
            }

            GameManager manager = GameManager.Instance;
            if (manager == null || !manager.HasEnteredCampus)
            {
                return;
            }

            // 会話やメニューの上には出さない。ただし 20 時を過ぎたことは封鎖中も覚えておき、
            // 封鎖が外れた最初のフレームで出す (#62)。
            EnsureLoaded();
            _dayEndLatched = LatchDayEnd(_dayEndLatched, manager.GameTimeHours, DayStartHour, _dayEndHour);
            if (ShouldEndDay(_dayEndLatched, KCDInput.GameplayBlocked, DormEnding.IsAnyShowing))
            {
                EndDayNow();
            }
        }

        private void OnContinue()
        {
            if (_restarting)
            {
                return;
            }

            StartCoroutine(RestartDay());
        }

        private void OnToTitle()
        {
            // 次に入るときは朝から。位置はシーンを読み直すのでスポーンに戻る。
            // 一日ごとの状態も「もう一日歩く」と同じように戻し、進行（クエスト・拾った物・写真）はメモリに残す。
            BeginNextDay();
            GameManager.Instance.ReturnToTitle();
        }

        /// <summary>
        /// 翌朝から歩き直す。暗転 → 屋内なら外へ → 最初と同じスポーンへ戻す → 時計と一日ごとの状態を朝に戻す → 明転。
        /// その場で朝にすると時間だけ飛んだように見えるので、入口から歩き直させる (#16)。
        /// </summary>
        private IEnumerator RestartDay()
        {
            _restarting = true;

            // 自分の封鎖だけを掛けて外す。暗転中に開いた画面の封鎖は残す (#40)。
            KCDInput.Block(this);

            yield return Fade(1f);

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null && player.IsSitting)
            {
                player.StandUp();
            }

            InteriorLoader interior = InteriorLoader.Instance;
            if (DayRestart.NeedsInteriorExit(interior != null, interior != null && interior.IsInside))
            {
                // 出入り係も自前で暗転するが、こちらが先に真っ黒にしてあるので画面は黒いまま。
                // 出入りの最中だと Exit は空振りするので、外に出るまで毎フレーム頼む。
                float waited = 0f;
                while (interior != null && interior.IsInside && waited < DayRestart.ExitTimeoutSeconds)
                {
                    interior.Exit();
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (player != null)
            {
                Vector3 position = DayRestart.ReturnPoint(_hasSpawn, _spawnPosition, player.transform.position);
                float yaw = DayRestart.ReturnYaw(_hasSpawn, _spawnYaw, player.transform.eulerAngles.y);
                player.Teleport(position, yaw);
                Physics.SyncTransforms();
                CameraRig.SnapBehind(player.transform);
            }

            int day = BeginNextDay();

            yield return null;
            yield return Fade(0f);

            KCDInput.Unblock(this);
            _restarting = false;
            HUD.Instance?.ShowToast(L.Format("ui.hud.new_day", day));
        }

        /// <summary>
        /// 時計と一日ごとの状態を朝に戻し、何日目かを 1 つ進める。
        /// チャイムは AudioManager.ShouldChime が時刻の巻き戻しでは鳴らさないので、ここでは触らない。
        /// </summary>
        private int BeginNextDay()
        {
            GameManager manager = GameManager.Instance;

            DayNightCycle cycle = FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.SetHours(DayStartHour);
            }
            else
            {
                manager.GameTimeHours = DayStartHour;
            }

            ResetChallengeTimers(manager.Quests);
            // 戻したことを追跡表示に伝える。呼ばないと前日の赤い「時間切れ」が残る。
            manager.Quests?.NotifyChanged();
            manager.DayNumber = DayRestart.NextDay(manager.DayNumber);
            _dayEndLatched = false;
            _armed = true;
            return manager.DayNumber;
        }

        /// <summary>
        /// 制限時間つきステップ（体育館まで 60 秒）は一日ごとの挑戦。数えかけと時間切れを朝に戻す。
        /// 依頼主がいるものは数える前に戻して「話しかけたら再挑戦」に、いないものはその場で数え直す。
        /// </summary>
        private static void ResetChallengeTimers(QuestSystem quests)
        {
            if (quests == null)
            {
                return;
            }

            foreach (QuestData quest in quests.All)
            {
                foreach (QuestStep step in quest.Steps)
                {
                    DayTimerAction action = DayRestart.TimerAction(
                        step.IsTimed, step.Completed, step.Timer.IsRunning, step.Timer.HasFailed,
                        !string.IsNullOrEmpty(step.Giver));
                    if (action == DayTimerAction.Keep)
                    {
                        continue;
                    }

                    step.Progress = 0;
                    if (action == DayTimerAction.Reset)
                    {
                        step.Timer.Reset();
                    }
                    else
                    {
                        step.Timer.Start(step.TimeLimit);
                    }
                }
            }
        }

        private IEnumerator Fade(float target)
        {
            if (_fade == null)
            {
                yield break;
            }

            float start = _fade.alpha;
            float elapsed = 0f;
            while (elapsed < _fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _fade.alpha = Mathf.Lerp(start, target, elapsed / _fadeSeconds);
                yield return null;
            }

            _fade.alpha = target;
        }

        /// <summary>暗転用の真っ黒な板。InteriorLoader・WorldBounds のものと同じ作り。</summary>
        private CanvasGroup BuildFadeOverlay()
        {
            var go = new GameObject("DayEndFadeCanvas");
            go.transform.SetParent(transform, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var panel = new GameObject("Black", typeof(RectTransform));
            panel.transform.SetParent(go.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = panel.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return group;
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
                HoursWalked = manager != null ? WalkedHours(manager.GameTimeHours, DayStartHour) : 0f
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
