using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// クエストの読み込みと進行管理。MonoBehaviour ではなく GameManager が寿命を持つ。
    /// データは Assets/Data/Quests/*.json を DataBundler が Resources/KCD/Quests へ写したものを読む。
    /// </summary>
    public sealed partial class QuestSystem
    {
        public const string ResourceFolder = "KCD/Quests";

        private readonly List<QuestData> _all = new List<QuestData>();
        private readonly HashSet<string> _active = new HashSet<string>();
        private readonly HashSet<string> _completed = new HashSet<string>();

        /// <summary>進行に変化があったとき。HUD が購読して表示を更新する。</summary>
        public event Action Changed;

        /// <summary>クエストが完了したとき。引数は完了したクエスト。</summary>
        public event Action<QuestData> QuestCompleted;

        /// <summary>クエストを受注したとき。Changed より先に呼ぶ。</summary>
        public event Action<QuestData> QuestStarted;

        /// <summary>
        /// 制限時間のあるステップが時間切れで失敗したとき。1 回の挑戦につき 1 回だけ呼ぶ。
        /// 自動では数え直さない。クエストは受注中のまま止まり、依頼主（giver）に話しかけると再挑戦になる。
        /// </summary>
        public event Action<QuestData> StepTimedOut;

        /// <summary>制限時間のあるステップの計時が始まったとき（受注した瞬間と、再挑戦のたび）。</summary>
        public event Action<QuestData> TimerStarted;

        /// <summary>
        /// 制限時間つきステップの目的地（建物・地点など）に、計時していないときに着いた（時間切れのあと・ロード直後）。
        /// 達成にはならないので、追跡表示が「依頼主に話しかけると再挑戦できる」と知らせる。Changed より先に呼ぶ。
        /// </summary>
        public event Action<QuestData> ChallengeRetryNeeded;

        /// <summary>
        /// 時刻の条件（minHour）があるステップの場所に、その時刻より前に着いた (#66)。
        /// 黙って何も起きないと「来たのに進まない」だけに見えるので、追跡表示が「◯時ごろにまた来よう」と知らせる。
        /// 着いた瞬間（<see cref="ReportVisit(string, bool)"/> の arriving が true）にだけ呼び、
        /// 留まっているあいだの報告し直しでは呼ばない。同じステップには一度呼んだら、出直して入り直しても
        /// 時計が巻き戻る（翌朝・セーブの読み込み）まで呼ばない (#54)。Changed より先に呼ぶ。
        /// </summary>
        public event Action<QuestData, QuestStep> StepTooEarly;

        /// <summary>
        /// ゲーム内の時刻（時）を返す時計。null なら GameManager の時刻を使う。テストから差し込む。
        /// </summary>
        public Func<float> Clock { get; set; }

        /// <summary>読み込み済みの全クエスト（order 昇順）。</summary>
        public IReadOnlyList<QuestData> All => _all;

        /// <summary>
        /// 外から <see cref="All"/> のステップを直接書き換えたあと、購読側に知らせる。
        /// 翌日に移るときの計時の戻し（<c>DayEndEvaluator</c>）がこれを呼ばないと、
        /// 追跡表示に前日の赤い「時間切れ」が残ったままになる。
        /// </summary>
        public void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>
        /// 読み込みの最中にイベントを出さないか。「はじめから」(#53) はタイトル画面で進行を作り直すが、
        /// AudioManager は同じ QuestSystem を購読したまま（AudioManager.Scene の AttachQuests は
        /// インスタンスが同じなら付け替えない）なので、黙らせないとタイトルで quest_start が鳴る。
        /// </summary>
        private bool _silent;

        /// <summary>
        /// 「◯時ごろにまた来よう」（<see cref="StepTooEarly"/>）を知らせ済みのステップ (#54)。
        /// 以前は入るたびに知らせていたので、時刻前に同じ場所を出入りするとそのたびにトーストが出た。
        /// 時計が巻き戻ったとき（<see cref="ObserveClock"/>）と、読み込み直したときに忘れる。
        /// </summary>
        private readonly HashSet<QuestStep> _toldTooEarly = new HashSet<QuestStep>();

        /// <summary>最後に見た時計（時）。巻き戻りを見つけるために覚える。まだ見ていなければ NaN。</summary>
        private float _lastSeenHour = float.NaN;

        /// <summary>Resources から全クエストを読み込み、autoStart のものを開始する。</summary>
        public void LoadFromResources()
        {
            LoadFromResources(false);
        }

        /// <summary>
        /// 「はじめから」で進行を初期状態に戻す (#53)。QuestSystem は GameManager と寿命を共にするので、
        /// シーンを読み直しても達成済みクエストも途中のステップも残る。読み込み直すのが確実。
        /// このとき知らせる相手はいない（HUD は次のシーンで作られ、作られたときに読み直す）ので、
        /// イベントは出さずに黙って入れ替える。
        /// </summary>
        public void ResetForNewGame()
        {
            LoadFromResources(true);
        }

        /// <summary>読み込みの本体。silent ならイベントを出さない。</summary>
        private void LoadFromResources(bool silent)
        {
            var parsed = new List<QuestData>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            for (int i = 0; i < assets.Length; i++)
            {
                QuestData quest = ParseQuest(assets[i]);
                if (quest != null)
                {
                    parsed.Add(quest);
                }
            }

            if (parsed.Count == 0)
            {
                Debug.LogError("[KCD] クエストデータが見つかりません: Resources/" + ResourceFolder);
            }

            Load(parsed, silent);
        }

        /// <summary>組み立て済みのクエストで中身を置き換え、autoStart のものを開始する。テストからも使う。</summary>
        public void Load(IEnumerable<QuestData> quests)
        {
            Load(quests, false);
        }

        /// <summary>
        /// 中身の置き換えの本体。silent なら受注・計時開始・変化のどのイベントも出さない。
        /// 「はじめから」の作り直しでタイトル画面に音や通知を漏らさないため (#53)。
        /// </summary>
        public void Load(IEnumerable<QuestData> quests, bool silent)
        {
            _silent = silent;
            try
            {
                _all.Clear();
                _active.Clear();
                _completed.Clear();
                _held.Clear();
                ForgetTooEarlyNotices();

                if (quests != null)
                {
                    foreach (QuestData quest in quests)
                    {
                        if (quest != null)
                        {
                            _all.Add(quest);
                        }
                    }
                }

                _all.Sort((a, b) => a.Order.CompareTo(b.Order));

                // autoStart でも前提（prerequisites）がそろうまでは受注しない (#65)。以前はここで前提を見ておらず、
                // q_park・q_sunset・q_sq_night_walk・q_sq_photo_walk が最初から受注中になっていた。
                // そのため 19 時に正門へ行くだけで、夕暮れも見ずに「一日を歩き切った」(ach_full_day) になった。
                // 前提がそろったあとは AutoStartUnlocked が受注する。
                AutoStartUnlocked();

                if (!_silent)
                {
                    Changed?.Invoke();
                }
            }
            finally
            {
                // 途中で例外が出ても黙ったままにしない（以後の受注音が消えてしまう）。
                _silent = false;
            }
        }

        private static QuestData ParseQuest(TextAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            try
            {
                return QuestData.FromJson(MiniJson.Deserialize(asset.text) as Dictionary<string, object>);
            }
            catch (FormatException error)
            {
                Debug.LogError("[KCD] クエスト JSON を解釈できません: " + asset.name + " / " + error.Message);
                return null;
            }
        }

        /// <summary>id からクエストを引く。無ければ null。</summary>
        public QuestData Find(string questId)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Id == questId)
                {
                    return _all[i];
                }
            }

            return null;
        }

        /// <summary>受注中か。</summary>
        public bool IsActive(string questId) => _active.Contains(questId);

        /// <summary>完了済みか。</summary>
        public bool IsCompleted(string questId) => _completed.Contains(questId);

        /// <summary>前提クエストをすべて終えているか。</summary>
        public bool PrerequisitesMet(QuestData quest)
        {
            if (quest == null)
            {
                return false;
            }

            for (int i = 0; i < quest.Prerequisites.Count; i++)
            {
                if (!_completed.Contains(quest.Prerequisites[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>クエストを受注する。前提未達・完了済みなら何もしない。</summary>
        public bool StartQuest(string questId)
        {
            QuestData quest = Find(questId);
            if (quest == null || _completed.Contains(questId) || _active.Contains(questId) || !PrerequisitesMet(quest))
            {
                return false;
            }

            Activate(quest);
            Changed?.Invoke();
            return true;
        }

        /// <summary>受注中に加えて開始イベントを出す。手動受注も自動開始もここを通す。</summary>
        private void Activate(QuestData quest)
        {
            _active.Add(quest.Id);
            if (!_silent)
            {
                QuestStarted?.Invoke(quest);
            }

            // 制限時間は受注した瞬間から 1 回だけ数え始める。
            StartTimerIfTimed(quest);

            if (_reportDepth > 0)
            {
                _activatedDuringReport.Add(quest.Id);
            }

            // 受注する前に拾っていた物を数える (#65)。
            CatchUpHeld(quest);
        }

        /// <summary>
        /// HUD に出す「いま追いかけているクエスト」。制限時間を数えている最中のものを最優先し、
        /// 次に時間切れ・ロード直後で再挑戦を待っている制限時間つきのもの、
        /// どちらも無ければ受注中で order が最も小さいもの。
        /// order だけで選ぶと、先に受けたクエスト（図書館・食堂など）の陰に隠れて残り時間や
        /// 「依頼主に話しかけると再挑戦できる」の案内が見えなくなる。
        /// </summary>
        public QuestData TrackedQuest
        {
            get
            {
                QuestData first = null;
                QuestData waiting = null;
                for (int i = 0; i < _all.Count; i++)
                {
                    QuestData quest = _all[i];
                    if (!_active.Contains(quest.Id))
                    {
                        continue;
                    }

                    QuestStep step = quest.CurrentStep;
                    if (step != null && step.Timer.IsRunning)
                    {
                        return quest;
                    }

                    if (waiting == null && step != null && step.IsTimed && (step.Timer.HasFailed || step.AwaitingGiver))
                    {
                        waiting = quest;
                    }

                    if (first == null)
                    {
                        first = quest;
                    }
                }

                return waiting ?? first;
            }
        }

        /// <summary>NPC と会話した。</summary>
        public void ReportTalk(string npcId) => Report(QuestStepKind.Talk, npcId);

        /// <summary>建物に入った。</summary>
        public void ReportEnter(string buildingId) => Report(QuestStepKind.Enter, buildingId);

        /// <summary>地点に到達した。</summary>
        public void ReportVisit(string placeId) => Report(QuestStepKind.Visit, placeId, false);

        /// <summary>
        /// 地点に到達した。arriving は入った瞬間なら true、留まっているあいだの報告し直しなら false。
        /// 時刻の条件より前に着いたときの案内（<see cref="StepTooEarly"/>）は、入った瞬間にだけ出す。
        /// </summary>
        public void ReportVisit(string placeId, bool arriving) => Report(QuestStepKind.Visit, placeId, arriving);

        /// <summary>落とし物を拾った。受注していないクエストのぶんも、拾った数として覚えておく (#65)。</summary>
        public void ReportCollect(string itemId)
        {
            NoteHeld(itemId);
            Report(QuestStepKind.Collect, itemId);
        }

        /// <summary>任意のフラグが立った。</summary>
        public void ReportFlag(string flagId) => Report(QuestStepKind.Flag, flagId);

        private void Report(QuestStepKind kind, string target)
        {
            Report(kind, target, false);
        }

        /// <summary>いまのゲーム内時刻（時）。</summary>
        private float CurrentHour()
        {
            return Clock != null ? Clock() : GameManager.Instance.GameTimeHours;
        }

        /// <summary>
        /// 時計を見て、前に見たときより戻っていたら「また来よう」の知らせ済みを忘れる (#54)。
        /// 一日の時計は朝から夜へ進む。戻るのは翌朝に戻したとき（DayEndEvaluator）や前の時刻のセーブを読んだときなので、
        /// 戻ったら新しい一日として数え、その日に来たらもう一度知らせる。
        /// </summary>
        private void ObserveClock(float hour)
        {
            if (hour < _lastSeenHour)
            {
                _toldTooEarly.Clear();
            }

            _lastSeenHour = hour;
        }

        /// <summary>いまが時刻の条件より前か。見た時計は巻き戻りの判定（<see cref="ObserveClock"/>）にも回す。</summary>
        private bool IsBeforeMinHour(QuestStep step)
        {
            float hour = CurrentHour();
            ObserveClock(hour);
            return hour < step.MinHour;
        }

        /// <summary>「また来よう」の知らせ済みと、見た時計を捨てる。読み込み直すときに呼ぶ。</summary>
        private void ForgetTooEarlyNotices()
        {
            _toldTooEarly.Clear();
            _lastSeenHour = float.NaN;
        }

        private void Report(QuestStepKind kind, string target, bool arriving)
        {
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            bool dirty = false;
            List<QuestData> retryNeeded = null;
            List<QuestData> tooEarly = null;

            _reportDepth++;
            try
            {
                dirty = ReportToActive(kind, target, arriving, ref retryNeeded, ref tooEarly);
            }
            finally
            {
                if (--_reportDepth == 0)
                {
                    _activatedDuringReport.Clear();
                }
            }

            if (retryNeeded != null)
            {
                for (int i = 0; i < retryNeeded.Count; i++)
                {
                    ChallengeRetryNeeded?.Invoke(retryNeeded[i]);
                }
            }

            if (tooEarly != null)
            {
                for (int i = 0; i < tooEarly.Count; i++)
                {
                    StepTooEarly?.Invoke(tooEarly[i], tooEarly[i].CurrentStep);
                }
            }

            if (dirty)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>受注中のクエストに報告を配る。何か進んだら true。案内のイベントは呼び出し側でまとめて出す。</summary>
        private bool ReportToActive(QuestStepKind kind, string target, bool arriving,
            ref List<QuestData> retryNeeded, ref List<QuestData> tooEarly)
        {
            bool dirty = false;
            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (!_active.Contains(quest.Id))
                {
                    continue;
                }

                // この報告の途中で受注したクエストは、受注のときの追いつき（CatchUpHeld）で
                // 今回拾った分をもう数えている。ここで足すと 1 個を 2 個に数える。
                if (kind == QuestStepKind.Collect && _activatedDuringReport.Contains(quest.Id))
                {
                    continue;
                }

                QuestStep step = quest.CurrentStep;
                if (step == null)
                {
                    continue;
                }

                // 制限時間つきのステップは計時中しか達成にしない（時間切れのあとに着いても失敗のまま）。
                if (step.Kind != kind || step.Target != target || !step.AcceptsProgress)
                {
                    // 計時していないのに目的地へ着いた。黙って何も起きないと「入ったのに達成にならない」だけに見えるので、
                    // 再挑戦のしかたを知らせる（イベントはループの後でまとめて出す）。
                    if (step.Kind == kind && step.Target == target && step.IsTimed && !step.Completed)
                    {
                        if (retryNeeded == null)
                        {
                            retryNeeded = new List<QuestData>();
                        }

                        retryNeeded.Add(quest);
                    }

                    // 依頼主に話しかけたら、はじめから数え直す。時間切れからの再挑戦も、ロード直後の再開もここ。
                    // 依頼主はスタート地点に立っているので、計時中に戻ってきて話しかけた場合も、そこから数え直してよい。
                    // 会話中は計時が止まっている（Tick を参照）ので、実際に減り始めるのは会話を閉じてから。
                    if (kind == QuestStepKind.Talk && step.IsTimed && !string.IsNullOrEmpty(step.Giver) &&
                        step.Giver == target)
                    {
                        StartTimer(quest, step);
                        dirty = true;
                    }

                    continue;
                }

                // 夕方にしか起きない出来事など、時刻の条件があるステップ。
                // 早く着いたら進めない。入った瞬間なら「◯時ごろにまた来よう」を知らせる（イベントはループの後）。
                // 知らせるのはステップごとに 1 回。出て入り直しても繰り返さない (#54)。
                if (step.MinHour > 0f && IsBeforeMinHour(step))
                {
                    if (arriving && _toldTooEarly.Add(step))
                    {
                        if (tooEarly == null)
                        {
                            tooEarly = new List<QuestData>();
                        }

                        tooEarly.Add(quest);
                    }

                    continue;
                }

                step.Progress = Mathf.Min(step.Count, step.Progress + 1);
                dirty = true;

                if (step.Progress < step.Count)
                {
                    continue;
                }

                CompleteStep(quest, step);
            }

            return dirty;
        }

        /// <summary>
        /// ステップを達成にする。クエストが終われば完了にして、前提のそろった autoStart を受注する。
        /// 続きがあれば、次のステップの計時を始め、次が collect なら持っている数まで進める。
        /// </summary>
        private void CompleteStep(QuestData quest, QuestStep step)
        {
            step.Completed = true;
            step.Timer.Clear();

            if (quest.IsComplete)
            {
                _active.Remove(quest.Id);
                _completed.Add(quest.Id);
                QuestCompleted?.Invoke(quest);
                AutoStartUnlocked();
            }
            else
            {
                // 次のステップが制限時間つきなら、ここから数える。
                StartTimerIfTimed(quest);
                CatchUpHeld(quest);
            }
        }

        /// <summary>制限時間つきステップを数えている最中なら残り秒。数えていなければ負数を返す。</summary>
        public float RemainingSeconds(QuestData quest)
        {
            QuestStep step = quest?.CurrentStep;
            if (step == null || !_active.Contains(quest.Id) || !step.Timer.IsRunning)
            {
                return -1f;
            }

            return step.Timer.Remaining;
        }

        /// <summary>
        /// GameManager から毎フレーム呼ぶ。制限時間つきステップを見張る。
        /// 会話・クエストログ・写真モードなど操作を止めている間は数えない（ポーズ中は timeScale 0 で deltaTime も 0）。
        /// 時計も毎フレーム見る。着いたときに見るだけだと、1 日目の 10 時に知らせて 2 日目の 15 時に来たとき、
        /// 時計が戻ったことに気づけず「また来よう」を出しそびれる (#54)。
        /// </summary>
        public void Tick(float deltaTime)
        {
            ObserveClock(CurrentHour());
            Tick(deltaTime, KCDInput.GameplayBlocked);
        }

        /// <summary>Tick の本体。paused なら何も進めない。テストから直接呼ぶ。</summary>
        public void Tick(float deltaTime, bool paused)
        {
            if (paused)
            {
                return;
            }

            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (!_active.Contains(quest.Id))
                {
                    continue;
                }

                QuestStep step = quest.CurrentStep;
                if (step == null || !step.Timer.Tick(deltaTime))
                {
                    continue;
                }

                // 時間切れ。以前はここで計時を捨て、次のフレームから黙って数え直していたため、
                // 失敗にならず「毎回リセットされるだけ」に見えた。いまは失敗のまま止め、再挑戦は依頼主に話しかけてから。
                step.Progress = 0;
                StepTimedOut?.Invoke(quest);
                Changed?.Invoke();
            }
        }

        /// <summary>現在のステップが制限時間つきなら、はじめから数え始める。</summary>
        private void StartTimerIfTimed(QuestData quest)
        {
            QuestStep step = quest.CurrentStep;
            if (step != null && step.IsTimed)
            {
                StartTimer(quest, step);
            }
        }

        private void StartTimer(QuestData quest, QuestStep step)
        {
            step.Progress = 0;
            step.Timer.Start(step.TimeLimit);
            if (!_silent)
            {
                TimerStarted?.Invoke(quest);
            }
        }

        private void AutoStartUnlocked()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (quest.AutoStart && !_completed.Contains(quest.Id) && !_active.Contains(quest.Id) &&
                    PrerequisitesMet(quest))
                {
                    Activate(quest);
                }
            }
        }
    }
}
