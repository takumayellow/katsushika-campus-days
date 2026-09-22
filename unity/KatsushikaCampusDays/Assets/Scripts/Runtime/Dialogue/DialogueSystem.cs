using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 会話の進行役。Resources/KCD/Dialogue から NPC ごとの会話を読み、
    /// 条件に合う話題を選んで 1 行ずつ送る。表示は HUD が担当する。
    /// </summary>
    public sealed class DialogueSystem : MonoBehaviour
    {
        public const string ResourceFolder = "KCD/Dialogue";

        private static DialogueSystem _instance;

        private readonly Dictionary<string, DialogueData> _data = new Dictionary<string, DialogueData>();
        private readonly HashSet<string> _spentTopics = new HashSet<string>();
        private readonly HashSet<string> _flags = new HashSet<string>();

        private DialogueTopic _topic;
        private string _topicKey = string.Empty;
        private int _lineIndex;
        private float _lineStartedAt;

        /// <summary>シーンに 1 つだけ存在する。</summary>
        public static DialogueSystem Instance => _instance;

        /// <summary>会話中か。</summary>
        public bool IsPlaying => _topic != null;

        /// <summary>表示中の行。会話していなければ null。</summary>
        public DialogueLine CurrentLine =>
            _topic != null && _lineIndex < _topic.Lines.Count ? _topic.Lines[_lineIndex] : null;

        /// <summary>行が切り替わったとき。</summary>
        public event Action<DialogueLine> LineChanged;

        /// <summary>会話が終わったとき。</summary>
        public event Action Finished;

        /// <summary>
        /// 送り可否の門。DialogueView が「文字送りが終わっているか」を返す。
        /// 途中なら送らせず、その入力は全文表示に使われる。
        /// </summary>
        public Func<bool> AdvanceGate { get; set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            LoadAll();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void LoadAll()
        {
            _data.Clear();
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            for (int i = 0; i < assets.Length; i++)
            {
                DialogueData parsed = ParseDialogue(assets[i]);
                if (parsed != null)
                {
                    _data[parsed.Id] = parsed;
                }
            }

            if (_data.Count == 0)
            {
                Debug.LogError("[KCD] 会話データが見つかりません: Resources/" + ResourceFolder);
            }
        }

        private static DialogueData ParseDialogue(TextAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            try
            {
                return DialogueData.FromJson(MiniJson.Deserialize(asset.text) as Dictionary<string, object>);
            }
            catch (FormatException error)
            {
                Debug.LogError("[KCD] 会話 JSON を解釈できません: " + asset.name + " / " + error.Message);
                return null;
            }
        }

        private void Update()
        {
            if (_topic == null)
            {
                return;
            }

            // 開いた直後の 1 フレームで送られてしまわないよう、わずかに間を置く。
            if (Time.unscaledTime - _lineStartedAt < 0.15f)
            {
                return;
            }

            if (KCDInput.InteractPressed || KCDInput.SubmitPressed)
            {
                if (AdvanceGate == null || AdvanceGate())
                {
                    Advance();
                }
            }
            else if (KCDInput.MenuPressed)
            {
                Finish();
            }
        }

        /// <summary>id の NPC に話しかける。条件に合う話題が無ければ false。</summary>
        public bool TalkTo(string dialogueId)
        {
            if (_topic != null || !_data.TryGetValue(dialogueId, out DialogueData data))
            {
                return false;
            }

            DialogueTopic chosen = SelectTopic(data);
            if (chosen == null || chosen.Lines.Count == 0)
            {
                return false;
            }

            _topic = chosen;
            _topicKey = data.Id + "/" + chosen.Id;
            _lineIndex = 0;
            _lineStartedAt = Time.unscaledTime;
            KCDInput.GameplayBlocked = true;
            LineChanged?.Invoke(CurrentLine);
            return true;
        }

        private DialogueTopic SelectTopic(DialogueData data)
        {
            QuestSystem quests = GameManager.Instance.Quests;
            DialogueTopic fallback = null;

            for (int i = 0; i < data.Topics.Count; i++)
            {
                DialogueTopic topic = data.Topics[i];
                string key = data.Id + "/" + topic.Id;

                if (topic.Once && _spentTopics.Contains(key))
                {
                    continue;
                }

                bool conditional = false;

                if (!string.IsNullOrEmpty(topic.RequiresActiveQuest))
                {
                    if (quests == null || !quests.IsActive(topic.RequiresActiveQuest))
                    {
                        continue;
                    }

                    conditional = true;
                }

                if (!string.IsNullOrEmpty(topic.RequiresCompletedQuest))
                {
                    if (quests == null || !quests.IsCompleted(topic.RequiresCompletedQuest))
                    {
                        continue;
                    }

                    conditional = true;
                }

                // 条件つきの話題を優先し、無条件のものは最後の受け皿にする。
                if (conditional)
                {
                    return topic;
                }

                if (fallback == null)
                {
                    fallback = topic;
                }
            }

            return fallback;
        }

        /// <summary>次の行へ。最後まで行けば会話を閉じる。</summary>
        public void Advance()
        {
            if (_topic == null)
            {
                return;
            }

            _lineIndex++;
            _lineStartedAt = Time.unscaledTime;

            if (_lineIndex >= _topic.Lines.Count)
            {
                Finish();
                return;
            }

            LineChanged?.Invoke(CurrentLine);
        }

        private void Finish()
        {
            DialogueTopic finished = _topic;
            string finishedKey = _topicKey;
            _topic = null;
            _topicKey = string.Empty;
            _lineIndex = 0;
            KCDInput.GameplayBlocked = false;
            // 最終行を送った Enter / E を、同じフレームで InteractionPrompt が拾って会話を再開しないようにする。
            KCDInput.MarkModalClosed();

            if (finished != null)
            {
                if (finished.Once)
                {
                    _spentTopics.Add(finishedKey);
                }

                QuestSystem quests = GameManager.Instance.Quests;
                if (quests != null && !string.IsNullOrEmpty(finished.StartsQuest))
                {
                    quests.StartQuest(finished.StartsQuest);
                }

                if (!string.IsNullOrEmpty(finished.SetsFlag))
                {
                    _flags.Add(finished.SetsFlag);
                    quests?.ReportFlag(finished.SetsFlag);
                }
            }

            Finished?.Invoke();
        }

        /// <summary>任意のフラグが立っているか。</summary>
        public bool HasFlag(string flag) => _flags.Contains(flag);
    }
}
