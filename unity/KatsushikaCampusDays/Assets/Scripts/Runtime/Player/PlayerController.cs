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
        /// <summary>歩く速さの既定値（m/s）。Locomotion ブレンドツリーで歩きのクリップを置く Speed もこの値（AnimatorFactory）。</summary>
        public const float DefaultWalkSpeed = 2.6f;

        /// <summary>
        /// 接地中に 1 フレームで下りられる段差（m）。地面の層の段（最大 2.1 cm）も入口の石張り（11.4 cm）も
        /// これで吸い付く。重力だけだと 1 フレームの落下量は 60 fps で 4.8 cm、240 fps で 1.2 cm しかなく、
        /// 縁を越えるたびに一瞬浮いて偽のジャンプと着地音が出ていた（#30）。
        /// フレーム時間で割って速度にするので、fps が変わっても 1 フレームの下り幅は同じ。
        /// </summary>
        public const float GroundSnap = 0.1f;

        /// <summary>
        /// Landed を出す最短の滞空時間（秒）。小さな段差を下りただけの 1〜2 フレームの浮きでは着地音を出さない。
        /// 本物のジャンプは _lastGroundedTime を -999 にするので必ず超える。
        /// </summary>
        public const float MinAirTimeForLanding = 0.15f;

        [Header("移動")]
        [SerializeField] private float _walkSpeed = DefaultWalkSpeed;
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
        private float _planarSpeed;

        /// <summary>座っているか。ベンチの SeatInteractable が SitAt で立てる。</summary>
        public bool IsSitting => _sitting;

        /// <summary>座った / 立った。アニメーション駆動が読む。</summary>
        public event System.Action<bool> SittingChanged;

        /// <summary>ジャンプした瞬間。</summary>
        public event System.Action Jumped;

        /// <summary>空中から接地した瞬間。</summary>
        public event System.Action Landed;

        /// <summary>
        /// 実際に進んだ水平方向の速さ。アニメーション駆動と足音が読む。
        /// 入力の速さではないので、壁に押し付けて止まっているときは 0 に近く、その場で走り続けない。
        /// </summary>
        public float PlanarSpeed => _planarSpeed;

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
            motion.y = SnappedVerticalMotion(_verticalVelocity, _wasGrounded, deltaTime);
            Vector3 before = transform.position;
            _controller.Move(motion * deltaTime);
            _planarSpeed = AchievedPlanarSpeed(_horizontalVelocity, transform.position - before, deltaTime);
            RecoverIfStuck();
        }

        /// <summary>
        /// Move に渡す縦方向の速さ（m/s）。接地していて上がっていない間は、1 フレームで GroundSnap まで
        /// 下りられるだけの速さにする。空中では重力そのまま（落下は速くしない）。
        /// </summary>
        /// <param name="verticalVelocity">重力を積んだ今の縦速度。</param>
        /// <param name="wasGrounded">このフレームの頭で接地していたか。</param>
        /// <param name="deltaTime">フレーム時間（秒）。</param>
        public static float SnappedVerticalMotion(float verticalVelocity, bool wasGrounded, float deltaTime)
        {
            if (!wasGrounded || verticalVelocity > 0f || deltaTime <= 0f)
            {
                return verticalVelocity;
            }

            return Mathf.Min(verticalVelocity, -GroundSnap / deltaTime);
        }

        /// <summary>
        /// 着地イベントを出すか。接地した瞬間で、かつ滞空が MinAirTimeForLanding 以上のときだけ出す。
        /// 段差を下りて 1〜2 フレーム浮いただけでは着地音を鳴らさない（#30）。
        /// </summary>
        /// <param name="grounded">今のフレームで接地しているか。</param>
        /// <param name="wasGrounded">前のフレームで接地していたか。</param>
        /// <param name="airTime">最後に接地していた時刻からの経過秒。ジャンプ直後は非常に大きい値になる。</param>
        public static bool ShouldFireLanded(bool grounded, bool wasGrounded, float airTime)
        {
            return grounded && !wasGrounded && airTime >= MinAirTimeForLanding;
        }

        /// <summary>
        /// 1 フレームで実際に進んだ水平の速さ。壁に正面から当たれば 0、斜めに当たれば壁に沿って滑った分になる。
        /// 入力した速さを上限にするので、段差の上り下りで一瞬速く見えることはない。
        /// </summary>
        public static float AchievedPlanarSpeed(Vector3 commandedVelocity, Vector3 displacement, float deltaTime)
        {
            float commanded = new Vector2(commandedVelocity.x, commandedVelocity.z).magnitude;
            if (deltaTime <= 0f)
            {
                return commanded;
            }

            float achieved = new Vector2(displacement.x, displacement.z).magnitude / deltaTime;
            return Mathf.Min(achieved, commanded);
        }

        /// <summary>
        /// 当たり判定に食い込んだら押し出す。屋内の合成メッシュに噛むと Move が
        /// どの方向にも進まなくなるので、重なりを見つけ次第その場で直す。
        /// 壁や地面の継ぎ目に押し付けているだけ（StuckRecovery.ContactBand 以内の接触）なら何もしない。
        /// 前に進めないこと自体は判定に使わない。壁に当たったら押し出さずにそのまま止める。
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
                return;
            }

            // 周りに空きが無いまま半秒ほど経ったら、最後の手段として来た方向へ半歩戻して少し持ち上げる。
            // 何度か繰り返せばいずれ空きに出るので、閉じ込められたままにはならない（#30）。
            if (_stuckFrames >= LastResortFrames)
            {
                Relocate(transform.position - transform.forward * 0.5f + Vector3.up * 0.3f, yaw);
                _stuckFrames = 0;
            }
        }

        /// <summary>空き場所も見つからないとき、後ろへ戻すまでのフレーム数。</summary>
        public const int LastResortFrames = 30;

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
            if (ShouldFireLanded(grounded, _wasGrounded, Time.time - _lastGroundedTime))
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
            _planarSpeed = 0f;
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
            _planarSpeed = 0f;
        }
    }
}
