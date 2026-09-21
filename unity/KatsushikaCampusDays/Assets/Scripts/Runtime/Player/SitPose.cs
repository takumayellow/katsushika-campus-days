using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 座りポーズ。専用クリップが無いので、腰を落として足と手を IK で座面の前に置く。
    /// Animator の IK Pass（AnimatorFactory が有効にする）から OnAnimatorIK で呼ばれる。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class SitPose : MonoBehaviour
    {
        [SerializeField] private float _blendTime = 0.25f;
        [SerializeField] private float _hipDrop = 0.34f;
        [SerializeField] private Vector3 _footOffset = new Vector3(0.13f, 0.02f, 0.38f);
        [SerializeField] private Vector3 _kneeOffset = new Vector3(0.13f, 0.45f, 0.55f);
        [SerializeField] private Vector3 _handOffset = new Vector3(0.16f, 0.52f, 0.25f);

        private Animator _animator;
        private float _weight;

        /// <summary>true にすると座り、false で立つ。ブレンドは自動。</summary>
        public bool Sitting { get; set; }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            float target = Sitting ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / Mathf.Max(0.01f, _blendTime));
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _weight <= 0f)
            {
                return;
            }

            _animator.bodyPosition -= transform.up * (_hipDrop * _weight);

            Place(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, Mirror(_footOffset), Mirror(_kneeOffset));
            Place(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, _footOffset, _kneeOffset);
            PlaceHand(AvatarIKGoal.LeftHand, Mirror(_handOffset));
            PlaceHand(AvatarIKGoal.RightHand, _handOffset);
        }

        private static Vector3 Mirror(Vector3 local)
        {
            return new Vector3(-local.x, local.y, local.z);
        }

        private void Place(AvatarIKGoal goal, AvatarIKHint hint, Vector3 localGoal, Vector3 localHint)
        {
            _animator.SetIKPositionWeight(goal, _weight);
            _animator.SetIKPosition(goal, transform.TransformPoint(localGoal));
            _animator.SetIKRotationWeight(goal, _weight);
            _animator.SetIKRotation(goal, transform.rotation);
            _animator.SetIKHintPositionWeight(hint, _weight);
            _animator.SetIKHintPosition(hint, transform.TransformPoint(localHint));
        }

        private void PlaceHand(AvatarIKGoal goal, Vector3 local)
        {
            _animator.SetIKPositionWeight(goal, _weight * 0.8f);
            _animator.SetIKPosition(goal, transform.TransformPoint(local));
        }
    }
}
