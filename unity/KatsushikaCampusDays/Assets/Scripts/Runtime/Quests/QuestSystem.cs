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

        /// <summary>制限時間のあるステップが時間切れになったとき。</summary>
        public event Action<QuestData> StepTimedOut;

        private readonly Dictionary<string, float> _timers = new Dictionary<string, float>();

        /// <summary>読み込み済みの全クエスト（order 昇順）。</summary>
        public IReadOnlyList<QuestData> All => _all;

        /// <summary>Resources から全クエストを読み込み、autoStart のものを開始する。</summary>
        public void LoadFromResources()
        {
            _all.Clear();
            _active.Clear();
            _completed.Clear();
            _timers.Clear();

            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            for (int i = 0; i < assets.Length; i++)
            {
                QuestData quest = ParseQuest(assets[i]);
                if (quest != null)
                {
                    _all.Add(quest);
                }
            }

            _all.Sort((a, b) => a.Order.CompareTo(b.Order));

            if (_all.Count == 0)
            {
                Debug.LogError("[KCD] クエストデータが見つかりません: Resources/" + ResourceFolder);
            }

            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].AutoStart)
                {
                    Activate(_all[i]);
                }
            }

            Changed?.Invoke();
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
            QuestStarted?.Invoke(quest);
        }

        /// <summary>HUD に出す「いま追いかけているクエスト」。受注中で order が最も小さいもの。</summary>
        public QuestData TrackedQuest
        {
            get
            {
                for (int i = 0; i < _all.Count; i++)
                {
                    if (_active.Contains(_all[i].Id))
                    {
                        return _all[i];
                    }
                }

                return null;
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

            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                if (!_active.Contains(quest.Id))
                {
                    continue;
                }

                QuestStep step = quest.CurrentStep;
                if (step == null || step.Kind != kind || step.Target != target)
                {
                    continue;
                }

                // 夕方にしか起きない出来事など、時刻の条件があるステップ。
                if (step.MinHour > 0f && GameManager.Instance.GameTimeHours < step.MinHour)
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
                _timers.Remove(quest.Id);

                if (quest.IsComplete)
                {
                    _active.Remove(quest.Id);
                    _completed.Add(quest.Id);
                    QuestCompleted?.Invoke(quest);
                    AutoStartUnlocked();
                }
            }

            if (dirty)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>制限時間つきステップの残り秒。無ければ負数を返す。</summary>
        public float RemainingSeconds(QuestData quest)
        {
            if (quest == null || !_timers.TryGetValue(quest.Id, out float elapsed))
            {
                return -1f;
            }

            QuestStep step = quest.CurrentStep;
            return step == null ? -1f : Mathf.Max(0f, step.TimeLimit - elapsed);
        }

        /// <summary>GameManager から毎フレーム呼ぶ。制限時間つきステップを見張る。</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                QuestData quest = _all[i];
                QuestStep step = quest.CurrentStep;

                if (!_active.Contains(quest.Id) || step == null || step.TimeLimit <= 0f)
                {
                    _timers.Remove(quest.Id);
                    continue;
                }

                _timers.TryGetValue(quest.Id, out float elapsed);
                elapsed += deltaTime;

                if (elapsed >= step.TimeLimit)
                {
                    // 時間切れ。進捗を戻してやり直させる。
                    _timers.Remove(quest.Id);
                    step.Progress = 0;
                    StepTimedOut?.Invoke(quest);
                    Changed?.Invoke();
                }
                else
                {
                    _timers[quest.Id] = elapsed;
                }
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
