using UnityEngine;

namespace KCD
{
    /// <summary>
    /// プレイヤーに付けて、近くの Interactable を探し出して HUD にプロンプトを出す。
    /// 距離だけでなく「正面にいるか」も見るので、背中側のものを拾わない。
    /// </summary>
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [SerializeField] private float _searchRadius = 3.0f;
        [SerializeField] private float _facingDot = 0.15f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly Collider[] _hits = new Collider[16];
        private Interactable _current;

        /// <summary>いま対象になっている Interactable。無ければ null。</summary>
        public Interactable Current => _current;

        private void Awake()
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            int npcLayer = LayerMask.NameToLayer("NPC");
            if (interactableLayer >= 0 && npcLayer >= 0)
            {
                _mask = (1 << interactableLayer) | (1 << npcLayer);
            }
        }

        private void Update()
        {
            _current = FindBest();

            HUD hud = HUD.Instance;
            if (hud != null)
            {
                hud.ShowInteractionPrompt(_current);
            }

            if (_current != null && KCDInput.InteractPressed && !KCDInput.GameplayBlocked
                && !KCDInput.ModalClosedThisFrame)
            {
                _current.Interact(gameObject);
            }
        }

        private Interactable FindBest()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _searchRadius, _hits, _mask, QueryTriggerInteraction.Collide);

            Interactable best = null;
            float bestScore = float.MaxValue;
            Vector3 forward = transform.forward;

            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i];
                if (hit == null)
                {
                    continue;
                }

                var candidate = hit.GetComponentInParent<Interactable>();
                if (candidate == null || !candidate.CanInteract)
                {
                    continue;
                }

                Vector3 delta = candidate.transform.position - transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance > candidate.InteractionRange)
                {
                    continue;
                }

                float facing = distance < 0.05f ? 1f : Vector3.Dot(forward, delta / distance);
                if (facing < _facingDot)
                {
                    continue;
                }

                // 近くて、より正面にあるものを優先する。
                float score = distance - facing;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
