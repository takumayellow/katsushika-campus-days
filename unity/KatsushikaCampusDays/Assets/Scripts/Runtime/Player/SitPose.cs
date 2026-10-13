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

        /// <summary>標準の体の脚の付け根の左右の開き（右脚側, m）。ボーンが読めないときに使う。</summary>
        private const float ReferenceLegHalfWidth = 0.06f;

        /// <summary>膝のヒントを脚の付け根より内側へ寄せる割合。膝をそろえ、外へ開かせない。</summary>
        private const float KneeInset = 0.8f;

        /// <summary>標準の体で、膝の上に置く手の左右の位置の基準 (m)。手はこれより 0.03 m 外、腿の外側に置く。</summary>
        private const float HandSpread = 0.13f;

        /// <summary>標準の体の腕の長さ (m)。ボーンが読めないときに使う。</summary>
        private const float ReferenceArm = 0.55f;

        /// <summary>肩から手首までの長さに対する、鍵盤へ伸ばす割合。伸び切らない。</summary>
        private const float ArmExtension = 0.92f;

        /// <summary>鍵盤へ身を乗り出す最大の距離 (m)。鍵盤の箱に胴が刺さらない範囲。</summary>
        private const float MaxApproach = 0.25f;

        /// <summary>手を鍵盤へ上げ下ろす秒数。</summary>
        private const float KeyboardBlendSeconds = 0.4f;

        /// <summary>小物のボーン名（blender/kcd_chara/rig.py の BAG_BONE）。</summary>
        private const string BagBone = "Bag";

        [SerializeField] private float _blendTime = 0.25f;

        private Animator _animator;
        private Transform _bag;
        private Vector3 _bagScale = Vector3.one;
        private float _weight;
        private float _keysWeight;
        private bool _keysActive;
        private Vector3 _keysLeft;
        private Vector3 _keysRight;
        private Layout _layout = Solve(ReferenceHip, ReferenceKnee, 0f);
        private float _armLength = ReferenceArm;
        private float _approach;
        private float _appliedApproach;

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

        /// <summary>右手の目標が腕の届く範囲を超えているぶん (m)。0 なら届く。</summary>
        public float RightHandShortfall => (ReachableKeys(_keysRight, HumanBodyBones.RightUpperArm) - _keysRight).magnitude;

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
        /// legHalfWidth は脚の付け根の左右の開き（右脚側、m。測れなければ 0）。
        /// 足は脚の付け根の真下の前に置き、膝のヒントはそれより少し内側に置く。腿と脛が前へまっすぐ出て、
        /// 膝が足より外へ開かない。標準の体（Hips 0.80 m・膝 0.434 m・付け根 0.06 m）では腰を 0.34 m 下げ、
        /// 足 (0.06, 0.026, 0.38)・膝 (0.048, 0.45, 0.55)・手 (0.16, 0.52, 0.25) に置く。
        /// 手は膝の上の鞄が腿に重ならないよう、腿の外側に置く。
        /// </summary>
        public static Layout Solve(float standingHip, float standingKnee, float legHalfWidth)
        {
            float scale = Mathf.Clamp(standingHip / ReferenceHip, 0.2f, 1.5f);
            float sitHip = SeatSurface + HipAboveSeat * scale;
            float legX = legHalfWidth > 0f ? legHalfWidth : ReferenceLegHalfWidth * scale;
            float handX = Mathf.Max(HandSpread * scale, legX) + 0.03f * scale;

            return new Layout
            {
                HipShift = sitHip - standingHip,
                Foot = new Vector3(legX, Mathf.Max(MinFootHeight, sitHip - standingKnee), 0.38f * scale),
                Knee = new Vector3(legX * KneeInset, sitHip - 0.01f, 0.55f * scale),
                Hand = new Vector3(handX, sitHip + 0.06f * scale, 0.25f * scale)
            };
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _bag = FindBag(transform);
            _bagScale = _bag != null ? _bag.localScale : Vector3.one;
        }

        /// <summary>手に持つ・担ぐ小物（Blender の Bag ボーン）。持たない体では null。</summary>
        public Transform Bag => _bag;

        /// <summary>
        /// 鍵盤を弾いている間、小物（鞄・風呂敷）をスケール 0 で消す。手が鍵盤へ上がると、手に掛けた
        /// 鞄が譜面台のあたりに浮いて鍵盤に重なる。手を戻したら元の大きさに戻す。
        /// </summary>
        private void LateUpdate()
        {
            if (_bag == null)
            {
                return;
            }

            bool hide = _keysActive || _keysWeight > 0f;
            Vector3 scale = hide ? Vector3.zero : _bagScale;
            if (_bag.localScale != scale)
            {
                _bag.localScale = scale;
            }
        }

        private static Transform FindBag(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == BagBone)
                {
                    return t;
                }
            }

            return null;
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
            if (Sitting && _keysActive)
            {
                _approach = Mathf.Lerp(_approach, ApproachGoal(), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }

        /// <summary>
        /// 右手が鍵盤の目標へ届くよう、肩を前へ出す距離 (m)。頭身の低い体は腕が短いので、座面を鍵盤へ寄せるだけでは届かない。
        /// 今の肩の位置から前回の乗り出しぶんを引いて測るので、結果は乗り出しに依らない。
        /// </summary>
        private float ApproachGoal()
        {
            Transform shoulder = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            if (shoulder == null)
            {
                return 0f;
            }

            Vector3 d = _keysRight - (shoulder.position - transform.forward * _appliedApproach);
            float lateral = Vector3.Dot(d, transform.right);
            float reach = ArmReach;
            float reachForward = Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateral * lateral - d.y * d.y));
            return Mathf.Clamp(Vector3.Dot(d, transform.forward) - reachForward, 0f, MaxApproach);
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
            _approach = 0f;

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

            _appliedApproach = _approach * _keysWeight * _weight;
            _animator.bodyPosition += transform.up * (_layout.HipShift * _weight) + transform.forward * _appliedApproach;

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
