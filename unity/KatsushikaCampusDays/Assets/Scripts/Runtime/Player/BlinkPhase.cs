using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Locomotion の周期の始まりをキャラクターごとにずらす。
    /// Idle クリップのまばたきは周期の決まった位置にあり、全員が同じフレームで Locomotion を始めるので、
    /// ずらさないと全員が同時に目を閉じる（#193 タイトルの 3 人が揃って目をつむった写真になった）。
    /// AnimatorFactory が Locomotion の Cycle Offset をこのパラメータにつなぐ。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class BlinkPhase : MonoBehaviour
    {
        /// <summary>Locomotion の Cycle Offset に使う Animator パラメータ。</summary>
        public const string ParameterName = "CycleOffset";

        /// <summary>Idle のまばたきで目が閉じかけている幅（周期に対する割合）。blender/kcd_chara/anim.py の 0.60〜0.68。</summary>
        public const float BlinkWidth = 0.08f;

        /// <summary>
        /// ずらす順番。並びの番号に黄金比を掛けて周期に散らすので、先頭に近いほど互いに離れる。
        /// タイトルに並ぶ 3 人（GameManager.PlayableCharacterIds）を先頭に置き、NPC、モブだけの体（student_m）が続く。
        /// </summary>
        public static readonly string[] CharacterOrder = Concat(
            GameManager.PlayableCharacterIds, new[] { "inari", "kaname", "prof", "sora", "student_m" });

        // 黄金比の小数部。番号に掛けて周期に散らすと、何人並べても近い 2 人が出にくい。
        private const float GoldenFraction = 0.618034f;

        // 並びに無い id（モブの体など）に割り当てる番号の幅。並びの後ろに散らす。
        private const int UnlistedSlots = 64;

        private static readonly int ParameterId = Animator.StringToHash(ParameterName);

        [SerializeField] private float _offset;

        private Animator _animator;

        // パラメータを確かめたコントローラ。毎フレーム parameters を作り直さないよう、変わったときだけ調べる。
        private RuntimeAnimatorController _parameterChecked;
        private bool _hasParameter;

        /// <summary>周期のずらし幅（0 以上 1 未満）。</summary>
        public float Offset
        {
            get => _offset;
            set => _offset = Mathf.Repeat(value, 1f);
        }

        /// <summary>キャラクターごとのずらし幅。並びに無い id は名前から決める。</summary>
        public static float OffsetFor(string characterId)
        {
            int index = System.Array.IndexOf(CharacterOrder, characterId);
            if (index < 0)
            {
                index = CharacterOrder.Length + StableHash(characterId) % UnlistedSlots;
            }

            return Mathf.Repeat(index * GoldenFraction, 1f);
        }

        /// <summary>周期の上で 2 つのずらし幅がどれだけ離れているか（0〜0.5）。</summary>
        public static float CircularDistance(float a, float b)
        {
            float d = Mathf.Repeat(a - b, 1f);
            return Mathf.Min(d, 1f - d);
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        // Animator は初期化の前に入れた値を落とし、無効にすると（keepAnimatorStateOnDisable でなければ）
        // パラメータが既定値に戻る。どちらも外から知らせが来ないので、毎フレーム確かめて入れ直す。
        private void Update()
        {
            Apply(_animator);
        }

        /// <summary>Animator のパラメータがずらし幅と違えば入れる。初期化前やパラメータの無いコントローラでは何もしない。</summary>
        public bool Apply(Animator animator)
        {
            if (animator == null || !animator.isInitialized || !HasParameter(animator))
            {
                return false;
            }

            if (animator.GetFloat(ParameterId) != _offset)
            {
                animator.SetFloat(ParameterId, _offset);
            }

            return true;
        }

        private bool HasParameter(Animator animator)
        {
            if (_parameterChecked == animator.runtimeAnimatorController)
            {
                return _hasParameter;
            }

            _parameterChecked = animator.runtimeAnimatorController;
            _hasParameter = false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == ParameterId)
                {
                    _hasParameter = true;
                    break;
                }
            }

            return _hasParameter;
        }

        private static string[] Concat(string[] head, string[] tail)
        {
            var all = new string[head.Length + tail.Length];
            head.CopyTo(all, 0);
            tail.CopyTo(all, head.Length);
            return all;
        }

        // string.GetHashCode は実行ごとに変わり得るので、ビルドをまたいで同じ値になる FNV-1a を使う。
        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619;
                }

                return (int)(hash & 0x7fffffff);
            }
        }
    }
}
