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
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int TalkHash = Animator.StringToHash("Talk");
        private static readonly int WaveHash = Animator.StringToHash("Wave");
        private static readonly int SitHash = Animator.StringToHash("Sit");

        [SerializeField] private Animator _animator;
        [SerializeField] private float _damping = 0.12f;

        private PlayerController _player;
        private bool _wasGrounded = true;
        private bool _hasParameters;
        private bool _hasSit;
        private SitPose _sitPose;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            _hasParameters = _animator != null && _animator.runtimeAnimatorController != null;
            _hasSit = _hasParameters && HasParameter("Sit");
            _sitPose = GetComponentInChildren<SitPose>();
            _player.SittingChanged += OnSittingChanged;
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.SittingChanged -= OnSittingChanged;
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

            _animator.SetFloat(SpeedHash, _player.PlanarSpeed, _damping, Time.deltaTime);
            _animator.SetBool(GroundedHash, _player.IsGrounded);

            if (_wasGrounded && !_player.IsGrounded)
            {
                _animator.SetTrigger(JumpHash);
            }

            _wasGrounded = _player.IsGrounded;

            if (KCDInput.WavePressed && !KCDInput.GameplayBlocked && !_player.IsSitting && _player.IsGrounded)
            {
                PlayWave();
                AudioManager.Instance?.PlaySe("wave");
            }
        }

        /// <summary>会話の開始 / 終了に合わせて Talk ステートを切り替える。</summary>
        public void SetTalking(bool talking)
        {
            if (_hasParameters)
            {
                _animator.SetBool(TalkHash, talking);
            }
        }

        /// <summary>手を振る。クエスト達成時の小さな演出に使う。</summary>
        public void PlayWave()
        {
            if (_hasParameters)
            {
                _animator.SetTrigger(WaveHash);
            }
        }
    }
}
