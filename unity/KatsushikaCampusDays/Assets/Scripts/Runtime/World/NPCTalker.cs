using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 話しかけられる NPC。会話データの id と結びつき、話しかけられると
    /// DialogueSystem に会話を投げ、QuestSystem に「会った」ことを報告する。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class NPCTalker : Interactable
    {
        [SerializeField] private string _npcId = "inari";
        [SerializeField] private string _displayName = "稲荷先輩";
        [SerializeField] private float _turnSpeed = 6f;

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

        public override bool CanInteract =>
            isActiveAndEnabled && (DialogueSystem.Instance == null || !DialogueSystem.Instance.IsPlaying);

        private void Awake()
        {
            _wander = GetComponent<NPCWander>();
            PromptLabel = L.Get("ui.interact.talk", "話す");
        }

        private void OnDisable()
        {
            Unsubscribe();
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

            GameManager.Instance.Quests?.ReportTalk(_npcId);
        }

        private void Update()
        {
            if (_facingTarget == null)
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
