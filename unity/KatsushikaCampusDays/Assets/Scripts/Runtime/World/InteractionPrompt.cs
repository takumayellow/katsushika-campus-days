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

            if (ShouldInteract(_current != null, KCDInput.InteractPressed, KCDInput.GameplayBlocked,
                    KCDInput.ModalClosedThisFrame))
            {
                _current.Interact(gameObject);
            }
        }

        /// <summary>
        /// E で調べてよいか。対象がいて、会話やメニューで操作が封鎖されておらず、
        /// モーダルを閉じたそのフレームでもないとき（会話の最終行を送った E で同じ相手に話しかけ直さない）。
        /// </summary>
        public static bool ShouldInteract(bool hasTarget, bool interactPressed, bool gameplayBlocked,
            bool modalClosedThisFrame)
        {
            return hasTarget && interactPressed && !gameplayBlocked && !modalClosedThisFrame;
        }

        /// <summary>
        /// 候補の点数（小さいほど優先: 近くて、より正面）。高さは見ず、平面の距離が range を超えるか、
        /// 正面度（forward との内積）が facingDot より小さい（背中側・真横）なら候補にしない。
        /// 足もと（平面で 5 cm 未満）は正面にあるものとして扱う。
        /// </summary>
        public static bool TryScore(Vector3 origin, Vector3 forward, Vector3 target, float range, float facingDot,
            out float score)
        {
            score = float.MaxValue;
            Vector3 delta = target - origin;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > range)
            {
                return false;
            }

            float facing = distance < 0.05f ? 1f : Vector3.Dot(forward, delta / distance);
            if (facing < facingDot)
            {
                return false;
            }

            // 近くて、より正面にあるものを優先する。
            score = distance - facing;
            return true;
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

                if (!TryScore(transform.position, forward, candidate.transform.position, candidate.InteractionRange,
                        _facingDot, out float score))
                {
                    continue;
                }

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
