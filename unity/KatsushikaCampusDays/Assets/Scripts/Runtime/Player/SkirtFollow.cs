using UnityEngine;

namespace KCD
{
    /// <summary>
    /// スカート・袴の前と後ろの中央を腿について行かせる（#49）。
    /// 中央の布は SkirtFront / SkirtBack の骨に付いている（blender/kcd_chara/rig.py）。この 2 本は Humanoid の外の骨で、
    /// Humanoid のクリップには曲線が残らないので、毎フレームここで腿の角度から回す。
    /// 式は blender/kcd_chara/anim.py の skirt_rotations と同じ。SkirtFront は前へいちばん出ている腿の振り角だけ前へ、
    /// SkirtBack は後ろへいちばん出ている腿の振り角だけ後ろへ回す。その向きへ出ている腿が無ければ腰と同じ姿勢のまま。
    /// レストの向きは SkinnedMeshRenderer の bindposes から読むので、いつ（どの姿勢で）付けても同じ結果になる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkirtFollow : MonoBehaviour
    {
        public const string FrontBone = "SkirtFront";
        public const string BackBone = "SkirtBack";

        /// <summary>腿の振り角の何倍だけ回すか。anim.py の SKIRT_FOLLOW と同じ値にする。</summary>
        public const float Follow = 1f;

        private static readonly string[] UpperLegs = { "LeftUpperLeg", "RightUpperLeg" };
        private static readonly string[] LowerLegs = { "LeftLowerLeg", "RightLowerLeg" };

        private bool _bound;
        private bool _usable;
        private SkinnedMeshRenderer _renderer;

        // 以下は _renderer の座標系（bindposes と同じ）で持つ。
        private Transform _hips;
        private Transform _front;
        private Transform _back;
        private readonly Transform[] _upper = new Transform[2];
        private readonly Transform[] _lower = new Transform[2];
        private readonly Vector3[] _thighRest = new Vector3[2];
        private readonly Vector3[] _axes = new Vector3[2];
        private Vector3 _axis;
        private Quaternion _hipsRest;
        private Quaternion _frontRest;
        private Quaternion _backRest;

        /// <summary>
        /// 前と後ろの骨で布が動く体か（無い体に付けても何もしない）。骨はスカートの無い体の肌にも
        /// ウェイト 0 で載っているので、ウェイトが乗っているかまで見る。メッシュは読み出し不可で取り込むので、
        /// ウェイトを見られるのはエディタ（シーンを組むとき）だけ。
        /// </summary>
        public static bool HasSkirtBones(GameObject body)
        {
            SkinnedMeshRenderer smr = FindRenderer(body);
            if (smr == null)
            {
                return false;
            }

            if (!smr.sharedMesh.isReadable && !Application.isEditor)
            {
                return true;
            }

            return Weighs(smr, FrontBone) || Weighs(smr, BackBone);
        }

        /// <summary>
        /// 腿のレストの向き rest から今の向き now への振り角（ラジアン）。axis に垂直な面へ落として測り、
        /// axis = rest × 前 にすると前へ振ったときが正になる。anim.py の _signed_angle と同じ。
        /// </summary>
        public static float SwingAngle(Vector3 rest, Vector3 now, Vector3 axis)
        {
            Vector3 a = rest - axis * Vector3.Dot(rest, axis);
            Vector3 b = now - axis * Vector3.Dot(now, axis);
            return Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(a, b)), Vector3.Dot(a, b));
        }

        private void LateUpdate()
        {
            // 画面外では見えないので回さない。映った最初のフレームでその姿勢から当て直すので、遅れは出ない。
            if (_renderer != null && !_renderer.isVisible)
            {
                return;
            }

            Apply();
        }

        /// <summary>今の腿の姿勢から 2 本の骨を回す。Animator を手で進めたとき（プレビュー・テスト）はその後に呼ぶ。</summary>
        public void Apply()
        {
            if (!_bound)
            {
                _usable = Bind();
                _bound = true;
            }

            if (!_usable)
            {
                return;
            }

            Transform space = _renderer.transform;
            Quaternion toSpace = Quaternion.Inverse(space.rotation);
            Quaternion hipsDelta = toSpace * _hips.rotation * Quaternion.Inverse(_hipsRest);
            // 腰ごと傾いた分を外し、腰から見た腿の角度だけを見る。
            Quaternion undo = Quaternion.Inverse(hipsDelta);

            float forward = 0f;
            float backward = 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 now = undo * space.InverseTransformDirection(_lower[i].position - _upper[i].position);
                float angle = SwingAngle(_thighRest[i], now, _axes[i]);
                forward = Mathf.Max(forward, angle);
                backward = Mathf.Min(backward, angle);
            }

            Quaternion parent = space.rotation * hipsDelta;
            _front.rotation = parent * Quaternion.AngleAxis(forward * Follow * Mathf.Rad2Deg, _axis) * _frontRest;
            _back.rotation = parent * Quaternion.AngleAxis(backward * Follow * Mathf.Rad2Deg, _axis) * _backRest;
        }

        private bool Bind()
        {
            _renderer = FindRenderer(gameObject);
            if (_renderer == null)
            {
                return false;
            }

            Transform[] bones = _renderer.bones;
            Matrix4x4[] bindposes = _renderer.sharedMesh.bindposes;
            if (!Rest(bones, bindposes, "Hips", out _hips, out Matrix4x4 hips)
                || !Rest(bones, bindposes, FrontBone, out _front, out Matrix4x4 front)
                || !Rest(bones, bindposes, BackBone, out _back, out Matrix4x4 back))
            {
                return false;
            }

            _hipsRest = RotationOf(hips);
            _frontRest = RotationOf(front);
            _backRest = RotationOf(back);

            // 体の前（+Z）を肌の座標系で見た向き。肌は体に固定なので一度だけ測ればよい。
            Vector3 ahead = _renderer.transform.InverseTransformDirection(transform.forward);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                if (!Rest(bones, bindposes, UpperLegs[i], out _upper[i], out Matrix4x4 upper)
                    || !Rest(bones, bindposes, LowerLegs[i], out _lower[i], out Matrix4x4 lower))
                {
                    return false;
                }

                _thighRest[i] = (Vector3)(lower.GetColumn(3) - upper.GetColumn(3));
                _axes[i] = Vector3.Cross(_thighRest[i], ahead).normalized;
                sum += _axes[i];
            }

            _axis = sum.normalized;
            return _axis != Vector3.zero;
        }

        /// <summary>前の骨と腿・腰を全部持つ肌。輪郭線の複製（_outline）は同じ骨を持つが、取り込みでメッシュを空にしてあるので除く。</summary>
        private static SkinnedMeshRenderer FindRenderer(GameObject body)
        {
            foreach (SkinnedMeshRenderer smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && smr.sharedMesh.vertexCount > 0
                    && IndexOf(smr.bones, FrontBone) >= 0 && IndexOf(smr.bones, BackBone) >= 0)
                {
                    return smr;
                }
            }

            return null;
        }

        private static bool Weighs(SkinnedMeshRenderer smr, string bone)
        {
            int index = IndexOf(smr.bones, bone);
            foreach (BoneWeight1 w in smr.sharedMesh.GetAllBoneWeights())
            {
                if (w.boneIndex == index && w.weight > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOf(Transform[] bones, string name)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i].name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>骨と、そのレストの姿勢（肌の座標系）。bindpose は「肌 → 骨」なので逆にする。</summary>
        private static bool Rest(Transform[] bones, Matrix4x4[] bindposes, string name, out Transform bone, out Matrix4x4 rest)
        {
            int index = IndexOf(bones, name);
            if (index < 0 || index >= bindposes.Length)
            {
                bone = null;
                rest = Matrix4x4.identity;
                return false;
            }

            bone = bones[index];
            rest = bindposes[index].inverse;
            return true;
        }

        /// <summary>拡大を含む行列からも回転だけを取り出す（FBX の取り込みで単位の倍率が乗ることがある）。</summary>
        private static Quaternion RotationOf(Matrix4x4 m)
        {
            return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
        }
    }
}
