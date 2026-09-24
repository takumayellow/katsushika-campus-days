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

                for (int i = 0; i < _all.Count; i++)
                {
                    if (_all[i].AutoStart)
                    {
                        Activate(_all[i]);
                    }
                }

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
        public void ReportVisit(string placeId) => Report(QuestStepKind.Visit, placeId);

        /// <summary>落とし物を拾った。</summary>
        public void ReportCollect(string itemId) => Report(QuestStepKind.Collect, itemId);

        /// <summary>任意のフラグが立った。</summary>
        public void ReportFlag(string flagId) => Report(QuestStepKind.Flag, flagId);

        private void Report(QuestStepKind kind, string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            bool dirty = false;
            List<QuestData> retryNeeded = null;

            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (!_active.Contains(quest.Id))
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
                if (step.MinHour > 0f && GameManager.Instance != null && GameManager.Instance.GameTimeHours < step.MinHour)
                {
                    continue;
                }

                step.Progress = Mathf.Min(step.Count, step.Progress + 1);
                dirty = true;

                if (step.Progress < step.Count)
                {
                    continue;
                }

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
                }
            }

            if (retryNeeded != null)
            {
                for (int i = 0; i < retryNeeded.Count; i++)
                {
                    ChallengeRetryNeeded?.Invoke(retryNeeded[i]);
                }
            }

            if (dirty)
            {
                Changed?.Invoke();
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
        /// </summary>
        public void Tick(float deltaTime)
        {
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
