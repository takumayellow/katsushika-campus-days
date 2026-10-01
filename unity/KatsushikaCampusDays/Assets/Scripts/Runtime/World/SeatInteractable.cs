using UnityEngine;

namespace KCD
{
    /// <summary>
    /// ベンチや長椅子。E で一番近い座面に座る。立つのは移動キーかジャンプか E（PlayerController 側）。
    /// </summary>
    public sealed class SeatInteractable : Interactable
    {
        [SerializeField] private Transform[] _anchors;
        [SerializeField] private string _flagId = "sit_bench";

        /// <summary>座る位置。前方（+Z）が座ったときの向き。</summary>
        public Transform[] Anchors
        {
            get => _anchors;
            set => _anchors = value;
        }

        public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.1f;

        public override bool CanInteract
        {
            get
            {
                if (!isActiveAndEnabled || _anchors == null || _anchors.Length == 0)
                {
                    return false;
                }

                PlayerController player = FindPlayer();
                return player == null || !player.IsSitting;
            }
        }

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
            RefreshPrompt();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
        }

        /// <summary>いまの言語で「座る」を出す。言語を切り替えたら出し直す。</summary>
        private void RefreshPrompt()
        {
            PromptLabel = L.Get("ui.interact.sit", "座る");
        }

        public override void Interact(GameObject interactor)
        {
            PlayerController player = interactor != null ? interactor.GetComponentInParent<PlayerController>() : null;
            if (player == null || player.IsSitting)
            {
                return;
            }

            Transform anchor = Nearest(interactor.transform.position);
            if (anchor == null)
            {
                return;
            }

            player.SitAt(anchor);
            AudioManager.Instance?.PlaySe("sit");
            if (!string.IsNullOrEmpty(_flagId))
            {
                GameManager.Instance?.Quests?.ReportFlag(_flagId);
            }
        }

        private Transform Nearest(Vector3 from)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (Transform anchor in _anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                float distance = (anchor.position - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchor;
                }
            }

            return best;
        }

        private static PlayerController FindPlayer()
        {
            GameObject player = GameObject.FindWithTag("Player");
            return player != null ? player.GetComponent<PlayerController>() : null;
        }
    }
}
