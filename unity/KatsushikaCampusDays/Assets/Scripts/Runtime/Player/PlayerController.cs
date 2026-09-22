using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 三人称の移動。カメラ相対の WASD / 矢印キー、Shift ダッシュ、Space ジャンプ。
    /// 段差や斜面で引っかからないよう stepOffset / slopeLimit を明示し、
    /// 接地時にわずかに下向きの力を与えて浮きを防ぐ。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("移動")]
        [SerializeField] private float _walkSpeed = 2.6f;
        [SerializeField] private float _runSpeed = 5.4f;
        [SerializeField] private float _acceleration = 14f;
        [SerializeField] private float _turnSmoothTime = 0.09f;

        [Header("ジャンプと重力")]
        [SerializeField] private float _jumpHeight = 1.1f;
        [SerializeField] private float _gravity = -22f;
        [SerializeField] private float _coyoteTime = 0.12f;

        [Header("接地")]
        [SerializeField] private float _stepOffset = 0.4f;
        [SerializeField] private float _slopeLimit = 45f;
        [SerializeField] private float _groundStick = -2.5f;

        private CharacterController _controller;
        private Transform _cameraTransform;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _turnVelocity;
        private float _lastGroundedTime;
        private bool _wasGrounded = true;
        private bool _sitting;
        private float _sitTime;
        private int _solidMask;
        private int _stuckFrames;

        /// <summary>座っているか。ベンチの SeatInteractable が SitAt で立てる。</summary>
        public bool IsSitting => _sitting;

        /// <summary>座った / 立った。アニメーション駆動が読む。</summary>
        public event System.Action<bool> SittingChanged;

        /// <summary>ジャンプした瞬間。</summary>
        public event System.Action Jumped;

        /// <summary>空中から接地した瞬間。</summary>
        public event System.Action Landed;

        /// <summary>水平方向の速さ。アニメーション駆動が読む。</summary>
        public float PlanarSpeed => _horizontalVelocity.magnitude;

        /// <summary>ダッシュ相当の速さで動いているか。</summary>
        public bool IsRunning => PlanarSpeed > _walkSpeed + 0.4f;

        /// <summary>接地しているか。</summary>
        public bool IsGrounded => _controller != null && _controller.isGrounded;

        /// <summary>カメラ相対移動の基準。ThirdPersonCamera が設定する。</summary>
        public Transform CameraTransform
        {
            get => _cameraTransform;
            set => _cameraTransform = value;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _controller.stepOffset = _stepOffset;
            _controller.slopeLimit = _slopeLimit;
            _controller.skinWidth = Mathf.Max(0.02f, _controller.radius * 0.1f);
            _controller.minMoveDistance = 0f;

            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_sitting)
            {
                bool wantsUp = KCDInput.MoveRequested || KCDInput.JumpRequested || KCDInput.InteractPressed;
                if (Time.time - _sitTime > 0.3f && !KCDInput.GameplayBlocked && wantsUp)
                {
                    StandUp();
                }

                return;
            }

            UpdateHorizontal(deltaTime);
            UpdateVertical(deltaTime);

            Vector3 motion = _horizontalVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * deltaTime);
            RecoverIfStuck();
        }

        /// <summary>
        /// 当たり判定に食い込んだら押し出す。屋内の合成メッシュに噛むと Move が
        /// どの方向にも進まなくなるので、重なりを見つけ次第その場で直す。
        /// 壁に押し付けているだけ（skinWidth の帯で止まっている）なら何もしない。
        /// </summary>
        private void RecoverIfStuck()
        {
            if (_solidMask == 0)
            {
                _solidMask = StuckRecovery.SolidMask(gameObject);
            }

            if (!StuckRecovery.IsPenetrating(_controller, _solidMask))
            {
                _stuckFrames = 0;
                return;
            }

            // 1 フレームだけの重なりは PhysX 側が次の Move で直すことが多いので待つ。
            _stuckFrames++;
            if (_stuckFrames < 2)
            {
                return;
            }

            float yaw = transform.eulerAngles.y;
            if (_stuckFrames < 6 && StuckRecovery.TryDepenetrate(_controller, _solidMask, out Vector3 correction))
            {
                Relocate(transform.position + correction, yaw);
                return;
            }

            if (StuckRecovery.TryFindFreeSpot(_controller, _solidMask, out Vector3 free))
            {
                Relocate(free, yaw);
                _stuckFrames = 0;
            }
        }

        /// <summary>速度を保ったまま位置だけ直す。CharacterController は切らないと座標を戻す。</summary>
        private void Relocate(Vector3 position, float yawDegrees)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            _controller.enabled = true;
        }

        private void UpdateHorizontal(float deltaTime)
        {
            Vector2 input = KCDInput.Move;
            Vector3 desired = Vector3.zero;

            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 forward = Vector3.forward;
                Vector3 right = Vector3.right;

                if (_cameraTransform != null)
                {
                    forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
                    right = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized;
                    if (forward.sqrMagnitude < 0.0001f)
                    {
                        forward = Vector3.forward;
                        right = Vector3.right;
                    }
                }

                Vector3 direction = (forward * input.y + right * input.x).normalized;
                float targetSpeed = KCDInput.Sprint ? _runSpeed : _walkSpeed;
                desired = direction * (targetSpeed * Mathf.Clamp01(input.magnitude));

                float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float angle = Mathf.SmoothDampAngle(
                    transform.eulerAngles.y, targetAngle, ref _turnVelocity, _turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, desired, _acceleration * deltaTime);
        }

        private void UpdateVertical(float deltaTime)
        {
            bool grounded = _controller.isGrounded;
            if (grounded && !_wasGrounded)
            {
                Landed?.Invoke();
            }

            _wasGrounded = grounded;
            if (grounded)
            {
                _lastGroundedTime = Time.time;
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = _groundStick;
                }
            }

            bool canJump = Time.time - _lastGroundedTime <= _coyoteTime;
            if (canJump && KCDInput.JumpPressed)
            {
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
                _lastGroundedTime = -999f;
                _wasGrounded = false;
                Jumped?.Invoke();
            }

            _verticalVelocity += _gravity * deltaTime;
            _verticalVelocity = Mathf.Max(_verticalVelocity, _gravity * 2f);
        }

        /// <summary>座る。anchor の位置に置き、anchor の前方を向く。移動は MovementLocked で止める。</summary>
        public void SitAt(Transform anchor)
        {
            if (anchor == null || _sitting)
            {
                return;
            }

            Vector3 forward = anchor.forward;
            forward.y = 0f;
            float yaw = forward.sqrMagnitude > 0.001f
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
                : transform.eulerAngles.y;

            _controller.enabled = false;
            transform.SetPositionAndRotation(anchor.position, Quaternion.Euler(0f, yaw, 0f));
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _sitting = true;
            _sitTime = Time.time;
            KCDInput.MovementLocked = true;
            SittingChanged?.Invoke(true);
        }

        /// <summary>立つ。座面の少し前に出て CharacterController を戻す。</summary>
        public void StandUp()
        {
            if (!_sitting)
            {
                return;
            }

            _sitting = false;
            KCDInput.MovementLocked = false;
            Teleport(transform.position + transform.forward * 0.55f, transform.eulerAngles.y);
            SittingChanged?.Invoke(false);
        }

        private void OnDisable()
        {
            if (_sitting)
            {
                _sitting = false;
                KCDInput.MovementLocked = false;
            }
        }

        /// <summary>ワープ。CharacterController を一度切らないと座標が戻される。</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            _controller.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
