using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 話しかけられる NPC。会話データの id と結びつき、話しかけられると DialogueSystem に会話を投げる。
    /// QuestSystem への「会った」の報告は DialogueSystem.TalkTo が話題を選ぶのと一緒に行う (#94)。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class NPCTalker : Interactable
    {
        [SerializeField] private string _npcId = "inari";
        [SerializeField] private string _displayName = "稲荷先輩";
        [SerializeField] private float _turnSpeed = 6f;

        /// <summary>近づいたときの操作の表示の辞書キー。人なら「話す」、扉のような物なら「調べる」(#175)。</summary>
        [SerializeField] private string _promptKey = TalkPromptKey;

        public const string TalkPromptKey = "ui.interact.talk";

        private NPCWander _wander;
        private PlayerAnimatorDriver _playerAnimator;
        private Transform _facingTarget;
        private bool _subscribed;

        /// <summary>会話データ / クエストターゲットとしての id。</summary>
        public string NpcId
        {
            get => _npcId;
            set => _npcId = value;
        }

        /// <summary>会話ウィンドウに出す名前。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>操作の表示の辞書キー（<see cref="TalkPromptKey"/> など）。</summary>
        public string PromptKey
        {
            get => _promptKey;
            set
            {
                _promptKey = string.IsNullOrEmpty(value) ? TalkPromptKey : value;
                RefreshPrompt();
            }
        }

        /// <summary>話しかけた人へ向き直る速さ。0 以下なら向き直らない（扉や掲示板など）。</summary>
        public float TurnSpeed
        {
            get => _turnSpeed;
            set => _turnSpeed = value;
        }

        public override bool CanInteract =>
            isActiveAndEnabled && (DialogueSystem.Instance == null || !DialogueSystem.Instance.IsPlaying);

        private void Awake()
        {
            _wander = GetComponent<NPCWander>();
        }

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
            RefreshPrompt();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
            Unsubscribe();
        }

        /// <summary>いまの言語で「話す」（または PromptKey の表示）を出す。言語を切り替えたら出し直す。</summary>
        private void RefreshPrompt()
        {
            PromptLabel = _promptKey == TalkPromptKey || string.IsNullOrEmpty(_promptKey)
                ? L.Get("ui.interact.talk", "話す")
                : L.Get(_promptKey, _promptKey);
        }

        public override void Interact(GameObject interactor)
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null || dialogue.IsPlaying)
            {
                return;
            }

            if (!dialogue.TalkTo(_npcId))
            {
                return;
            }

            _facingTarget = interactor != null ? interactor.transform : null;
            _playerAnimator = interactor != null ? interactor.GetComponent<PlayerAnimatorDriver>() : null;
            _playerAnimator?.SetTalking(true);

            if (_wander != null)
            {
                _wander.Paused = true;
            }

            dialogue.Finished += OnDialogueFinished;
            _subscribed = true;

            // 称号「顔なじみ」(ach_friends) は話した NPC の数で決まる (#65)。
            DayStats.NoteTalk(_npcId);
        }

        private void Update()
        {
            if (_facingTarget == null || _turnSpeed <= 0f)
            {
                return;
            }

            Vector3 delta = _facingTarget.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.01f)
            {
                return;
            }

            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(delta), _turnSpeed * Time.deltaTime);
        }

        private void OnDialogueFinished()
        {
            Unsubscribe();
            _facingTarget = null;

            _playerAnimator?.SetTalking(false);
            _playerAnimator = null;

            if (_wander != null)
            {
                _wander.Paused = false;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed && DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Finished -= OnDialogueFinished;
            }

            _subscribed = false;
        }
    }
}
