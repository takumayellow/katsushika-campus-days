using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 講師の右手を黒板へ伸ばす IK（講義棟の教室, #439）。書くときは板の面に、指すときは板の少し手前に手を置き、
    /// 振り返ったときは手を下ろす。目標と重みは <see cref="Classroom"/> が毎フレーム決め、ここでは重みを滑らかにつなぐ。
    /// Animator のベースレイヤの IK Pass（AnimatorFactory が立てる）から呼ばれる。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class LecturerGesture : MonoBehaviour
    {
        /// <summary>手の重みを 0 から 1 へ変える秒数。</summary>
        private const float BlendSeconds = 0.35f;

        private Animator _animator;
        private float _weight;
        private float _targetWeight;
        private Vector3 _hand;
        private Vector3 _look;
        private bool _hasLook;

        /// <summary>右手の目標（ワールド座標）と、届かせる度合い（0 = 下ろす, 1 = 目標に置く）。</summary>
        public void Reach(Vector3 hand, float weight)
        {
            _hand = hand;
            _targetWeight = Mathf.Clamp01(weight);
        }

        /// <summary>顔を向ける点（ワールド座標）。</summary>
        public void LookAt(Vector3 point)
        {
            _look = point;
            _hasLook = true;
        }

        /// <summary>手を下ろす。</summary>
        public void Release()
        {
            _targetWeight = 0f;
        }

        /// <summary>いまの手の重み（0〜1）。</summary>
        public float Weight => _weight;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            _weight = Mathf.MoveTowards(_weight, _targetWeight, Time.deltaTime / BlendSeconds);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _animator == null)
            {
                return;
            }

            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _weight);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, _hand);
            if (_hasLook)
            {
                _animator.SetLookAtWeight(0.6f, 0.2f, 0.7f, 0f, 0.6f);
                _animator.SetLookAtPosition(_look);
            }
        }
    }
}
