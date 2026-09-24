using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// PlayerController の状態を Animator パラメータへ流す。
    /// Animator や Controller が無くても落ちないようにしてある（FBX 未着でも動く）。
    /// パラメータ名は DESIGN §2 の Action 名に対応させている。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerAnimatorDriver : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GaitRateHash = Animator.StringToHash("GaitRate");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int TalkHash = Animator.StringToHash("Talk");
        private static readonly int WaveHash = Animator.StringToHash("Wave");
        private static readonly int SitHash = Animator.StringToHash("Sit");

        [SerializeField] private Animator _animator;
        [SerializeField] private float _damping = 0.12f;

        /// <summary>踏み切りを拾えなかったとき（段差から落ちたときなど）に Jump へ入るまでの猶予（秒）。</summary>
        public const float FallTriggerSeconds = 0.15f;

        private PlayerController _player;
        private bool _hasParameters;
        private bool _hasSit;
        private bool _hasGaitRate;
        private float _leftGroundAt = -1f;
        private bool _jumpTriggered;
        private SitPose _sitPose;
        private QuestSystem _quests;
        private readonly List<Vector3> _npcPositions = new List<Vector3>();

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            if (_animator == null)
            {
                // PlayerAppearance（実行順 -200）が選ばれていない体を先に消しているので、
                // 引数なしの GetComponentInChildren は選ばれた体の Animator だけを拾う (#6)。
                _animator = GetComponentInChildren<Animator>();
            }

            RefreshAnimatorFlags();
            _sitPose = GetComponentInChildren<SitPose>();
            _player.SittingChanged += OnSittingChanged;
            _player.Jumped += OnJumped;
        }

        /// <summary>
        /// 体を差し替えたあとに呼ぶ（PlayerAppearance.Apply が毎回呼ぶ, #6）。
        /// 消えた体の Animator を掴んだままだと、歩いても走っても何も動かない。
        /// この Awake より先に呼ばれることもある（PlayerAppearance の実行順は -200）ので、
        /// _player がまだ null でも落ちないようにしてある。
        /// </summary>
        public void RebindAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            RefreshAnimatorFlags();
            _sitPose = GetComponentInChildren<SitPose>();

            bool sitting = _player != null && _player.IsSitting;
            if (_sitPose != null)
            {
                _sitPose.Sitting = sitting;
            }

            if (_hasSit)
            {
                _animator.SetBool(SitHash, sitting);
            }
        }

        /// <summary>いま掴んでいる Animator にどのパラメータがあるかを覚え直す。</summary>
        private void RefreshAnimatorFlags()
        {
            _hasParameters = _animator != null && _animator.runtimeAnimatorController != null;
            _hasSit = _hasParameters && HasParameter("Sit");
            _hasGaitRate = _hasParameters && HasParameter("GaitRate");
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.SittingChanged -= OnSittingChanged;
                _player.Jumped -= OnJumped;
            }

            AttachQuests(null);
        }

        /// <summary>クエストを達成したら手を振る。「はじめから」で QuestSystem が作り直されるので毎フレーム確かめる。</summary>
        private void AttachQuests(QuestSystem quests)
        {
            if (quests == _quests)
            {
                return;
            }

            if (_quests != null)
            {
                _quests.QuestCompleted -= OnQuestCompleted;
            }

            _quests = quests;
            if (_quests != null)
            {
                _quests.QuestCompleted += OnQuestCompleted;
            }
        }

        private void OnQuestCompleted(QuestData quest)
        {
            // 「〜と話す」クエストは会話の始まりと同じフレームで達成になる。Talk 中に積んだ
            // トリガーは会話が終わった瞬間に遅れて出るので、会話・メニュー中は振らない。
            if (_player != null && !KCDInput.GameplayBlocked && !_player.IsSitting && _player.IsGrounded)
            {
                PlayWave();
            }
        }

        /// <summary>
        /// Q で手を振る。前にいる近くの人（NPCWander）は立ち止まってこちらを向き、振り返してくれる。
        /// 以前は手を振るだけで、誰も何も反応しなかった。
        /// </summary>
        public void WaveHello()
        {
            PlayWave();
            AudioManager.Instance?.PlaySe("wave");
            NPCWander[] npcs = FindObjectsByType<NPCWander>(FindObjectsInactive.Exclude);
            _npcPositions.Clear();
            foreach (NPCWander npc in npcs)
            {
                _npcPositions.Add(npc.transform.position);
            }

            int pick = NPCWander.PickWaveTarget(
                transform.position, transform.forward, _npcPositions, NPCWander.WaveReach);
            if (pick >= 0)
            {
                npcs[pick].WaveBack(transform.position);
            }
        }

        /// <summary>
        /// 踏み切った瞬間に Jump を出す。
        /// 以前は「前のフレームまで接地していて、いま接地していない」で出していたが、
        /// 踏み切り直後の 1 フレームはまだ isGrounded が立っていることがあり、そこで取りこぼしていた（#43）。
        /// </summary>
        private void OnJumped()
        {
            _jumpTriggered = true;
            if (_hasParameters)
            {
                _animator.SetTrigger(JumpHash);
            }
        }

        private bool HasParameter(string name)
        {
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.name == name)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnSittingChanged(bool sitting)
        {
            if (_sitPose != null)
            {
                _sitPose.Sitting = sitting;
            }

            if (_hasSit)
            {
                _animator.SetBool(SitHash, sitting);
            }
        }

        private void Update()
        {
            if (!_hasParameters)
            {
                return;
            }

            SolveGait(_player.PlanarSpeed, out float blend, out float rate);
            if (_hasGaitRate)
            {
                _animator.SetFloat(GaitRateHash, rate, _damping, Time.deltaTime);
            }
            else
            {
                // GaitRate の無い古いコントローラ（AnimatorFactory.Upgrade 前）では再生速度を掛けられない。
                // 歩幅だけ伸ばした blend をそのまま渡すと足が前に滑るので、素の速さに戻す。
                blend = Mathf.Max(0f, _player.PlanarSpeed);
            }

            _animator.SetFloat(SpeedHash, blend, _damping, Time.deltaTime);

            _animator.SetBool(GroundedHash, _player.IsGrounded);
            AttachQuests(GameManager.Instance != null ? GameManager.Instance.Quests : null);
            UpdateAirborne();

            if (KCDInput.WavePressed && !KCDInput.GameplayBlocked && !_player.IsSitting && _player.IsGrounded)
            {
                WaveHello();
            }
        }

        /// <summary>踏み切りを拾えなかった落下（段差を踏み外した等）も、少し遅れて Jump にする。</summary>
        private void UpdateAirborne()
        {
            if (_player.IsGrounded)
            {
                _leftGroundAt = -1f;
                _jumpTriggered = false;
                return;
            }

            if (_leftGroundAt < 0f)
            {
                _leftGroundAt = Time.time;
                return;
            }

            if (!_jumpTriggered && Time.time - _leftGroundAt >= FallTriggerSeconds)
            {
                _jumpTriggered = true;
                _animator.SetTrigger(JumpHash);
            }
        }

        /// <summary>
        /// 移動の速さから、ブレンドツリーに渡す値（blend）とクリップの再生速度（rate）を出す。
        ///
        /// 歩きのクリップは 2.6 m/s ぶんの歩幅と歩調で作ってある。1.1 m/s の NPC にそのまま
        /// 2.6 を渡すと足が毎秒 1.5 m 滑り、逆に 1.1 を渡すと待機との中間になって歩幅だけ縮む。
        /// 歩幅と歩調を同じ割合（速さの平方根）で落とすと、足が滑らないまま歩き方も自然になる。
        /// blend = sqrt(speed * 2.6)、rate = speed / blend = sqrt(speed / 2.6)。
        /// 歩き以上の速さでは歩き / 走りのクリップが両端で一致しているので、速さをそのまま渡す。
        /// </summary>
        public static void SolveGait(float speed, out float blend, out float rate)
        {
            float walk = PlayerController.DefaultWalkSpeed;
            speed = Mathf.Max(0f, speed);
            if (speed >= walk)
            {
                blend = speed;
                rate = 1f;
                return;
            }

            blend = Mathf.Sqrt(speed * walk);
            rate = blend > 0.01f ? speed / blend : 1f;
        }

        /// <summary>会話の開始 / 終了に合わせて Talk ステートを切り替える。</summary>
        public void SetTalking(bool talking)
        {
            if (_hasParameters)
            {
                _animator.SetBool(TalkHash, talking);
            }
        }

        /// <summary>手を振る。Q と、クエスト達成時の小さな演出に使う。</summary>
        public void PlayWave()
        {
            if (_hasParameters)
            {
                _animator.SetTrigger(WaveHash);
            }
        }
    }
}
