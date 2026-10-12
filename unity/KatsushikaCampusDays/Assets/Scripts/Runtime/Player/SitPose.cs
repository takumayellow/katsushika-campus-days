using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 座りポーズ。専用クリップが無いので、腰を座面の高さへ動かし、足と手を IK で座面の前に置く。
    /// Animator の IK Pass（AnimatorFactory が有効にする）から OnAnimatorIK で呼ばれる。
    /// 腰の動かし幅と足・手の位置は体格から出す。頭身の低い体（坊っちゃん・マドンナ）は
    /// 立ったときの腰が座面より低いので、腰を持ち上げて足を前に垂らす。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class SitPose : MonoBehaviour
    {
        /// <summary>座面の高さ (m)。ベンチ 0.45・ラウンジチェアのクッション 0.43 の間。</summary>
        public const float SeatSurface = 0.44f;

        /// <summary>みらい（標準の体）の Hips ボーンの高さ (m)。足や手の置き場はこの体で合わせた。</summary>
        public const float ReferenceHip = 0.80f;

        /// <summary>標準の体で、座ったときの Hips が座面より上にある高さ (m)。クッションに少し沈む。</summary>
        private const float HipAboveSeat = 0.02f;

        /// <summary>標準の体の膝の高さ (m)。ボーンが読めないときに使う。</summary>
        private const float ReferenceKnee = 0.434f;

        private const float MinFootHeight = 0.02f;

        /// <summary>標準の体の腕の長さ (m)。ボーンが読めないときに使う。</summary>
        private const float ReferenceArm = 0.55f;

        /// <summary>肩から手首までの長さに対する、鍵盤へ伸ばす割合。伸び切らない。</summary>
        private const float ArmExtension = 0.92f;

        /// <summary>手を鍵盤へ上げ下ろす秒数。</summary>
        private const float KeyboardBlendSeconds = 0.4f;

        [SerializeField] private float _blendTime = 0.25f;

        private Animator _animator;
        private float _weight;
        private float _keysWeight;
        private bool _keysActive;
        private Vector3 _keysLeft;
        private Vector3 _keysRight;
        private Layout _layout = Solve(ReferenceHip, ReferenceKnee, 0f);
        private float _armLength = ReferenceArm;

        /// <summary>true にすると座り、false で立つ。ブレンドは自動。</summary>
        public bool Sitting { get; set; }

        /// <summary>
        /// 両手をワールド座標の left / right へ置く（鍵盤を弾く手）。座っている間だけ効き、毎フレーム呼んで動かす。
        /// ReleaseKeyboardHands で膝の上に戻る。
        /// </summary>
        public void SetKeyboardHands(Vector3 left, Vector3 right)
        {
            _keysActive = true;
            _keysLeft = left;
            _keysRight = right;
        }

        public void ReleaseKeyboardHands()
        {
            _keysActive = false;
        }

        /// <summary>鍵盤に手を置いている度合い（0 = 膝の上, 1 = 鍵盤）。</summary>
        public float KeyboardWeight => _keysWeight;

        /// <summary>右手が向かう位置（ワールド座標）。鍵盤が遠い体では、肩から腕の届く範囲に収める。</summary>
        public Vector3 RightHandGoal => ReachableKeys(_keysRight, HumanBodyBones.RightUpperArm);

        /// <summary>腕を伸ばして届く距離 (m)。座り始めに体から測る。</summary>
        public float ArmReach => _armLength * ArmExtension;

        /// <summary>座ったときの腰の上下と、足・膝・手の置き場（体の根元から見た位置, m）。</summary>
        public struct Layout
        {
            public float HipShift;
            public Vector3 Foot;
            public Vector3 Knee;
            public Vector3 Hand;
        }

        /// <summary>
        /// 体格から座り方を決める。standingHip / standingKnee は立ったときの Hips と膝の高さ、
        /// legHalfWidth は脚の付け根の左右の開き（右脚側、m）。標準の体（Hips 0.80 m・膝 0.434 m）では
        /// 腰を 0.34 m 下げ、足 (0.13, 0.026, 0.38)・膝 (0.13, 0.45, 0.55)・手 (0.16, 0.52, 0.25) に置く。
        /// </summary>
        public static Layout Solve(float standingHip, float standingKnee, float legHalfWidth)
        {
            float scale = Mathf.Clamp(standingHip / ReferenceHip, 0.2f, 1.5f);
            float sitHip = SeatSurface + HipAboveSeat * scale;
            float legX = Mathf.Max(0.13f * scale, legHalfWidth);

            return new Layout
            {
                HipShift = sitHip - standingHip,
                Foot = new Vector3(legX, Mathf.Max(MinFootHeight, sitHip - standingKnee), 0.38f * scale),
                Knee = new Vector3(legX, sitHip - 0.01f, 0.55f * scale),
                Hand = new Vector3(legX + 0.03f * scale, sitHip + 0.06f * scale, 0.25f * scale)
            };
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            // 座り始めに今の体を測る。体を差し替えたり縮めたりしていても、その時点の大きさで座る。
            if (Sitting && _weight <= 0f)
            {
                Measure();
            }

            float target = Sitting ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / Mathf.Max(0.01f, _blendTime));
            _keysWeight = Mathf.MoveTowards(_keysWeight, Sitting && _keysActive ? 1f : 0f, Time.deltaTime / KeyboardBlendSeconds);
        }

        private void Measure()
        {
            if (_animator == null || !_animator.isHuman)
            {
                return;
            }

            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform knee = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Transform thigh = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (hips == null || knee == null)
            {
                return;
            }

            float legHalfWidth = thigh != null ? Mathf.Abs(Vector3.Dot(thigh.position - transform.position, transform.right)) : 0f;
            _layout = Solve(Height(hips), Height(knee), legHalfWidth);

            Transform upper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lower = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (upper != null && lower != null && hand != null)
            {
                _armLength = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            }
        }

        private float Height(Transform bone)
        {
            return Vector3.Dot(bone.position - transform.position, transform.up);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || _weight <= 0f)
            {
                return;
            }

            _animator.bodyPosition += transform.up * (_layout.HipShift * _weight);

            Place(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, Mirror(_layout.Foot), Mirror(_layout.Knee));
            Place(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, _layout.Foot, _layout.Knee);
            PlaceHand(AvatarIKGoal.LeftHand, Mirror(_layout.Hand), ReachableKeys(_keysLeft, HumanBodyBones.LeftUpperArm));
            PlaceHand(AvatarIKGoal.RightHand, _layout.Hand, ReachableKeys(_keysRight, HumanBodyBones.RightUpperArm));
        }

        private static Vector3 Mirror(Vector3 local)
        {
            return new Vector3(-local.x, local.y, local.z);
        }

        /// <summary>体の根元から見た位置（m、拡大縮小を掛けない）をワールド座標にする。</summary>
        private Vector3 World(Vector3 local)
        {
            return transform.position + transform.rotation * local;
        }

        private void Place(AvatarIKGoal goal, AvatarIKHint hint, Vector3 localGoal, Vector3 localHint)
        {
            _animator.SetIKPositionWeight(goal, _weight);
            _animator.SetIKPosition(goal, World(localGoal));
            _animator.SetIKRotationWeight(goal, _weight);
            _animator.SetIKRotation(goal, transform.rotation);
            _animator.SetIKHintPositionWeight(hint, _weight);
            _animator.SetIKHintPosition(hint, World(localHint));
        }

        /// <summary>その手の肩から腕の届く距離までに収めた鍵盤の位置。</summary>
        private Vector3 ReachableKeys(Vector3 keys, HumanBodyBones upperArm)
        {
            Transform shoulder = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(upperArm) : null;
            if (shoulder == null)
            {
                return keys;
            }

            Vector3 offset = keys - shoulder.position;
            return shoulder.position + Vector3.ClampMagnitude(offset, ArmReach);
        }

        private void PlaceHand(AvatarIKGoal goal, Vector3 local, Vector3 keys)
        {
            _animator.SetIKPositionWeight(goal, Mathf.Lerp(_weight * 0.8f, _weight, _keysWeight));
            _animator.SetIKPosition(goal, Vector3.Lerp(World(local), keys, _keysWeight));
        }
    }
}
