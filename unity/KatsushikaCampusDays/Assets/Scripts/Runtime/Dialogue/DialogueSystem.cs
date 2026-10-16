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

        /// <summary>話しかけた NPC の会話データの id。好感度はこの id で動かす。NPC の決まらない会話（メモ）では空。</summary>
        private string _dialogueId = string.Empty;

        /// <summary>送っている行。話題の行か、選んだ選択肢の返事の行。</summary>
        private List<DialogueLine> _lines;
        private int _lineIndex;

        /// <summary>選択肢を出していて、まだ選んでいないか (#9)。</summary>
        private bool _choosing;
        private int _choiceIndex;
        private int _choicesShownFrame = -1;

        /// <summary>
        /// 選択肢を出したあとに指を新しく置いたか。最後の行を送った指を離したときの叩きで選ばないための印。
        /// 会話の板は指を置くと E になるので、選択肢を出したフレームより後の E で立てる。
        /// </summary>
        private bool _tapArmed;
        private float _lineStartedAt;

        /// <summary>シーンに 1 つだけ存在する。</summary>
        public static DialogueSystem Instance => _instance;

        /// <summary>会話中か。</summary>
        public bool IsPlaying => _topic != null;

        /// <summary>表示中の行。会話していなければ null。</summary>
        public DialogueLine CurrentLine =>
            _topic != null && !_choosing && _lines != null && _lineIndex < _lines.Count ? _lines[_lineIndex] : null;

        /// <summary>選択肢を出しているか (#9)。</summary>
        public bool IsChoosing => _choosing;

        /// <summary>いま選んでいる選択肢の番号。</summary>
        public int ChoiceIndex => _choiceIndex;

        /// <summary>出している選択肢。選ばせていなければ空。</summary>
        public IReadOnlyList<DialogueChoice> Choices =>
            _choosing ? _topic.Choices : (IReadOnlyList<DialogueChoice>)Array.Empty<DialogueChoice>();

        /// <summary>選択肢を出したときと、選んでいる選択肢が変わったとき。</summary>
        public event Action ChoicesChanged;

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
            KCDInput.Unblock(this);
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

            if (_choosing && InputMode.IsTouch && KCDInput.InteractPressed && Time.frameCount > _choicesShownFrame)
            {
                _tapArmed = true;
            }

            // 開いた直後の 1 フレームで送られてしまわないよう、わずかに間を置く。
            if (Time.unscaledTime - _lineStartedAt < 0.15f)
            {
                return;
            }

            if (_choosing)
            {
                UpdateChoosing();
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

        /// <summary>
        /// id の NPC に話しかけ、クエストに talk を報告する。条件に合う話題が無ければ何も報告せず false。
        /// </summary>
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
            _lines = chosen.Lines;
            _lineIndex = 0;
            _lineStartedAt = Time.unscaledTime;

            // 会話の封鎖は会話のもの。建物の出入りや落下からの復帰の暗転が終わっても外れない (#40)。
            KCDInput.Block(this);

            // talk の報告は封鎖のあと。「〜と話す」で達成したときに PlayerAnimatorDriver が会話中だと分かって手を振らない。
            _topic = ReportTalkAndSelect(data, GameManager.Instance.Quests, _spentTopics, chosen);
            _lines = _topic.Lines;
            _topicKey = data.Id + "/" + _topic.Id;
            _dialogueId = data.Id;
            LineChanged?.Invoke(CurrentLine);
            return true;
        }

        private DialogueTopic SelectTopic(DialogueData data)
        {
            return SelectTopic(data, GameManager.Instance.Quests, _spentTopics);
        }

        /// <summary>
        /// 条件に合う話題を選ぶ。隠しアイテムの数と持ち物は、いま拾っているもの（DayStats）で判定する。
        /// spentTopics は「NPC id/話題 id」の集合で、once の話題を二度出さないために使う。
        /// </summary>
        public static DialogueTopic SelectTopic(DialogueData data, QuestSystem quests, ICollection<string> spentTopics)
        {
            return SelectTopic(data, quests, spentTopics, LiveHiddenCount(), DayStats.HasCollected);
        }

        /// <summary>
        /// 条件に合う話題を選ぶ（テストから呼ぶ純関数）。条件つきの話題を上から優先し、無条件のものは最後の受け皿。
        /// hiddenCount は拾った隠しアイテムの種類数、hasItem はそのアイテムを拾っているか (#175)。
        /// </summary>
        public static DialogueTopic SelectTopic(DialogueData data, QuestSystem quests, ICollection<string> spentTopics,
            int hiddenCount, Func<string, bool> hasItem)
        {
            if (data == null)
            {
                return null;
            }

            DialogueTopic fallback = null;

            for (int i = 0; i < data.Topics.Count; i++)
            {
                DialogueTopic topic = data.Topics[i];
                string key = data.Id + "/" + topic.Id;

                if (topic.Once && spentTopics != null && spentTopics.Contains(key))
                {
                    continue;
                }

                // 選択肢に答えた話題はセーブに載るので、ロードのあとも二度出さない（好感度を何度も上げさせない）(#9)。
                if (topic.HasChoices && NpcAffinity.HasAnswered(key))
                {
                    continue;
                }

                // クエストを頼む話題は、そのクエストを受注中・完了済みなら出さない。
                // 使い終えた話題（_spentTopics）はセーブに載らないので、ロード直後に同じ依頼をもう一度されるのを防ぐ。
                if (!string.IsNullOrEmpty(topic.StartsQuest) && quests != null &&
                    (quests.IsActive(topic.StartsQuest) || quests.IsCompleted(topic.StartsQuest)))
                {
                    continue;
                }

                // 前提のそろっていない依頼も出さない (#65)。出すと会話の終わりの StartQuest が前提で断られ、
                // once の話題は使い終えた扱いになるので、その依頼は二度と受けられなくなる
                // （オリエンテーションの前に空と話すと、コーヒーも実験ノートも達成できなかった）。
                if (!string.IsNullOrEmpty(topic.StartsQuest) && quests != null)
                {
                    QuestData requested = quests.Find(topic.StartsQuest);
                    if (requested != null && !quests.PrerequisitesMet(requested))
                    {
                        continue;
                    }
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

                if (topic.HasItemCondition)
                {
                    if (!ItemConditionMet(topic, hiddenCount, hasItem))
                    {
                        continue;
                    }

                    conditional = true;
                }

                if (topic.RequiresAffinity > 0)
                {
                    if (NpcAffinity.Get(data.Id) < topic.RequiresAffinity)
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

        /// <summary>
        /// talk を報告し、この話しかけで達成したクエストのお礼の話題があればそれを、無ければ chosen を返す（テストから呼ぶ）(#94)。
        /// chosen は報告の前に <see cref="SelectTopic(DialogueData, QuestSystem, ICollection{string})"/> で選んだもの。
        ///
        /// 以前は話題を選んでから報告していたので、牛乳を届けた会話で要が「ファミマは共創棟の 1 階」と
        /// 受注中のヒント（requiresActiveQuest）を言い、お礼（requiresCompletedQuest）は次に話したときに出ていた。
        /// 報告のあとに選び直すだけだと、オリエンテーションを終えた会話で教授の最初のあいさつ（無条件の t_welcome）より
        /// 体育館の依頼（t_gym_start）が先に出てしまうので、差し替えるのはこの報告で達成になったクエストのお礼だけにする。
        /// 依頼（startsQuest）の話題はお礼として扱わない。
        /// </summary>
        public static DialogueTopic ReportTalkAndSelect(
            DialogueData data, QuestSystem quests, ICollection<string> spentTopics, DialogueTopic chosen)
        {
            return ReportTalkAndSelect(data, quests, spentTopics, chosen, LiveHiddenCount(), DayStats.HasCollected);
        }

        /// <summary>
        /// <see cref="ReportTalkAndSelect(DialogueData, QuestSystem, ICollection{string}, DialogueTopic)"/> の、
        /// 隠しアイテムの数と持ち物を渡せる版（テストから呼ぶ）。
        /// </summary>
        public static DialogueTopic ReportTalkAndSelect(DialogueData data, QuestSystem quests,
            ICollection<string> spentTopics, DialogueTopic chosen, int hiddenCount, Func<string, bool> hasItem)
        {
            if (data == null || quests == null)
            {
                return chosen;
            }

            var completedBefore = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < data.Topics.Count; i++)
            {
                string required = data.Topics[i].RequiresCompletedQuest;
                if (!string.IsNullOrEmpty(required) && quests.IsCompleted(required))
                {
                    completedBefore.Add(required);
                }
            }

            quests.ReportTalk(data.Id);

            for (int i = 0; i < data.Topics.Count; i++)
            {
                DialogueTopic topic = data.Topics[i];
                string required = topic.RequiresCompletedQuest;
                if (string.IsNullOrEmpty(required) || completedBefore.Contains(required) || !quests.IsCompleted(required))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(topic.StartsQuest) || topic.Lines.Count == 0)
                {
                    continue;
                }

                if (topic.Once && spentTopics != null && spentTopics.Contains(data.Id + "/" + topic.Id))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(topic.RequiresActiveQuest) && !quests.IsActive(topic.RequiresActiveQuest))
                {
                    continue;
                }

                if (topic.HasItemCondition && !ItemConditionMet(topic, hiddenCount, hasItem))
                {
                    continue;
                }

                if (topic.RequiresAffinity > 0 && NpcAffinity.Get(data.Id) < topic.RequiresAffinity)
                {
                    continue;
                }

                if (topic.HasChoices && NpcAffinity.HasAnswered(data.Id + "/" + topic.Id))
                {
                    continue;
                }

                return topic;
            }

            return chosen;
        }

        /// <summary>話題の、隠しアイテムの数と持ち物の条件を満たしているか。条件が無ければ true。</summary>
        public static bool ItemConditionMet(DialogueTopic topic, int hiddenCount, Func<string, bool> hasItem)
        {
            if (topic == null)
            {
                return false;
            }

            if (topic.RequiresHiddenCount > 0 && hiddenCount < topic.RequiresHiddenCount)
            {
                return false;
            }

            return string.IsNullOrEmpty(topic.RequiresItem) || (hasItem != null && hasItem(topic.RequiresItem));
        }

        private static int LiveHiddenCount()
        {
            return DayStats.HiddenCollectedCount(CollectibleCatalog.Instance);
        }

        /// <summary>会話データ dialogueId の中の話題を id で引く。無ければ null。</summary>
        public DialogueTopic FindTopic(string dialogueId, string topicId)
        {
            if (string.IsNullOrEmpty(dialogueId) || !_data.TryGetValue(dialogueId, out DialogueData data))
            {
                return null;
            }

            return data.Topics.Find(t => t.Id == topicId);
        }

        /// <summary>
        /// NPC に話しかけずに会話を出す（拾ったメモの文面など）(#175)。talk の報告はしない。
        /// once は覚えない（どの NPC の話題か決まらないので）。依頼（startsQuest）とフラグは話しかけたときと同じく終わりに扱う。
        /// 会話中なら何もせず false。
        /// </summary>
        public bool Play(DialogueTopic topic)
        {
            if (_topic != null || topic == null || topic.Lines.Count == 0)
            {
                return false;
            }

            _topic = topic;
            _lines = topic.Lines;
            _topicKey = string.Empty;
            _dialogueId = string.Empty;
            _lineIndex = 0;
            _lineStartedAt = Time.unscaledTime;
            KCDInput.Block(this);
            LineChanged?.Invoke(CurrentLine);
            return true;
        }

        /// <summary>
        /// NPC に話しかけずに、その NPC の話題を出す（夕日のあとのエンディング）(#176)。talk の報告はしない。
        /// once の話題は話しかけたときと同じく覚えるので、あとで話しかけても同じ話は出ない。
        /// 話題が無いか会話中なら何もせず false。
        /// </summary>
        public bool PlayTopic(string dialogueId, string topicId)
        {
            DialogueTopic topic = FindTopic(dialogueId, topicId);
            if (!Play(topic))
            {
                return false;
            }

            _topicKey = dialogueId + "/" + topic.Id;
            _dialogueId = dialogueId;
            return true;
        }

        /// <summary>次の行へ。話題の最後の行のあとは選択肢があれば選ばせ、無ければ（返事の最後の行のあとも）会話を閉じる。</summary>
        public void Advance()
        {
            if (_topic == null || _choosing)
            {
                return;
            }

            _lineIndex++;
            _lineStartedAt = Time.unscaledTime;

            if (_lineIndex < _lines.Count)
            {
                LineChanged?.Invoke(CurrentLine);
                return;
            }

            if (_lines == _topic.Lines && _topic.HasChoices)
            {
                _choosing = true;
                _choiceIndex = 0;
                _choicesShownFrame = Time.frameCount;
                _tapArmed = false;
                ChoicesChanged?.Invoke();
                return;
            }

            Finish();
        }

        /// <summary>
        /// 選択肢の入力。↑↓ で選び、E / Enter で決める。指で遊んでいるときは選択肢を叩いて決める
        /// （画面のどこを叩いても E になる板があるので、E では決めない）。Esc は選ばずに閉じる。
        /// </summary>
        private void UpdateChoosing()
        {
            int step = KCDInput.MenuVertical;
            if (step != 0)
            {
                _choiceIndex = StepChoice(_choiceIndex, step, _topic.Choices.Count);
                AudioManager.Instance?.PlayUi("ui_move");
                ChoicesChanged?.Invoke();
            }
            else if (!InputMode.IsTouch && (KCDInput.InteractPressed || KCDInput.SubmitPressed))
            {
                Choose(_choiceIndex);
            }
            else if (KCDInput.MenuPressed)
            {
                Finish();
            }
        }

        /// <summary>上下で 1 つずらした先。下が +1 で、端から先は反対の端へ回り込む。</summary>
        public static int StepChoice(int index, int step, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((index + step) % count + count) % count;
        }

        /// <summary>指で選択肢を叩いたとき。選択肢を出したあとに置いた指でなければ選ばない。</summary>
        public bool TapChoice(int index)
        {
            return _tapArmed && Choose(index);
        }

        /// <summary>
        /// 選択肢を選ぶ。好感度を動かし、この話題を答えた話題として覚え、返事の行を送り始める（無ければ閉じる）。
        /// 選ばせていないときと範囲の外は何もせず false。
        /// </summary>
        public bool Choose(int index)
        {
            if (!_choosing || index < 0 || index >= _topic.Choices.Count)
            {
                return false;
            }

            DialogueChoice choice = _topic.Choices[index];
            _choosing = false;
            _tapArmed = false;
            _choiceIndex = index;
            AudioManager.Instance?.PlayUi("ui_confirm");

            ApplyChoice(_dialogueId, _topicKey, choice);
            if (!string.IsNullOrEmpty(choice.SetsFlag))
            {
                _flags.Add(choice.SetsFlag);
                GameManager.Instance.Quests?.ReportFlag(choice.SetsFlag);
            }

            _lines = choice.Lines;
            _lineIndex = 0;
            _lineStartedAt = Time.unscaledTime;
            if (_lines.Count == 0)
            {
                Finish();
                return true;
            }

            LineChanged?.Invoke(CurrentLine);
            return true;
        }

        /// <summary>選んだ選択肢の好感度を NPC に足し、話題を答えた話題として覚える（テストから呼ぶ）。</summary>
        public static void ApplyChoice(string dialogueId, string topicKey, DialogueChoice choice)
        {
            if (choice == null)
            {
                return;
            }

            NpcAffinity.Add(dialogueId, choice.Affinity);
            NpcAffinity.NoteAnswered(topicKey);
        }

        /// <summary>
        /// 閉じたときに once の話題を使い終えたことにするか。選択肢の話題は答えたときだけ使い終える
        /// （選ばずに Esc で閉じたら、次に話しかけたときにもう一度聞く）。
        /// </summary>
        public static bool SpendsOnFinish(DialogueTopic topic, string topicKey)
        {
            if (topic == null || !topic.Once || string.IsNullOrEmpty(topicKey))
            {
                return false;
            }

            return !topic.HasChoices || NpcAffinity.HasAnswered(topicKey);
        }

        private void Finish()
        {
            DialogueTopic finished = _topic;
            string finishedKey = _topicKey;
            _topic = null;
            _topicKey = string.Empty;
            _dialogueId = string.Empty;
            _lines = null;
            _lineIndex = 0;
            _choosing = false;
            _choiceIndex = 0;
            _tapArmed = false;
            KCDInput.Unblock(this);
            // 最終行を送った Enter / E を、同じフレームで InteractionPrompt が拾って会話を再開しないようにする。
            KCDInput.MarkModalClosed();

            if (finished != null)
            {
                if (SpendsOnFinish(finished, finishedKey))
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
