using System.Collections.Generic;
using UnityEngine;
#if KCD_CINEMACHINE
using Unity.Cinemachine;
#endif

namespace KCD
{
    /// <summary>
    /// フォトモードの自由カメラ (#12)。PhotoSystem が入るときに Begin、抜けるときに End を呼ぶ。
    /// 入ると肩越しカメラ（CinemachineBrain か ThirdPersonCamera）を止めてメインカメラを直接動かし、
    /// 抜けると入ったときの位置・向き・画角に戻してから肩越しカメラを動かし直す。
    /// 動かすのは実時間（timeScale 0 の間も進む）。プレイヤーの見る点から <see cref="MaxRange"/> の内側に留め、
    /// 壁・床・天井・木の幹は <see cref="PhotoCameraMotion"/> の掃引で抜けない。プレイヤーの体の中にも入らない。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PhotoFreeCamera : MonoBehaviour
    {
        /// <summary>見る点からカメラを離せる距離（m）。入ったときにもっと離れていたら、その距離まで。</summary>
        public const float MaxRange = 8f;

        /// <summary>移動の速さ（m/s）と、速く動かすときの速さ。</summary>
        public const float MoveSpeed = 3f;
        public const float FastMoveSpeed = 8f;

        /// <summary>見上げ・見下ろしの上限（度）。真上・真下で向きが回らないよう 90 度の手前で止める。</summary>
        public const float MaxPitch = 80f;

        /// <summary>画角の範囲（度）とズーム 1 目盛りの変化。</summary>
        public const float MinFieldOfView = 20f;
        public const float MaxFieldOfView = 80f;
        public const float ZoomStepDegrees = 3f;

        // 構図を合わせやすいよう、肩越しカメラ（2.2）より遅く回す。
        private const float LookSensitivity = 1.5f;

        // フォーカスの切り替えなどで飛んできた大きな差分は視点操作として扱わない（ThirdPersonCamera と同じ）。
        private const float LookSpikeLimit = 40f;

        // 見る点が見つからないときの、足元からの高さ（PlayerAppearance の背丈 1.6 m × 0.92）。
        private const float FallbackAimHeight = 1.47f;

        // 実時間は上限が掛からない（Web 版でタブから戻った直後などは数秒になる）。1 フレームの移動をこの秒数分までにする。
        private const float MaxStepTime = 0.1f;

        private readonly List<Behaviour> _paused = new List<Behaviour>();
        private readonly CameraNearHider _nearHider = new CameraNearHider();

        private Camera _camera;
        private Transform _player;
        private Vector3 _feet;
        private Vector3 _anchor;
        private float _range;
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private float _startFieldOfView;
        private float _yaw;
        private float _pitch;

        /// <summary>自由カメラで動かしているか。</summary>
        public bool IsActive { get; private set; }

        /// <summary>メインカメラを自由カメラにする。メインカメラが無ければ何もせず false。</summary>
        public bool Begin()
        {
            if (IsActive)
            {
                return true;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            _camera = camera;
            PauseOrbitCamera(camera.gameObject);

            Transform cameraTransform = camera.transform;
            _startPosition = cameraTransform.position;
            _startRotation = cameraTransform.rotation;
            _startFieldOfView = camera.fieldOfView;
            SetAngles(_startRotation);

            GameObject player = GameObject.FindWithTag("Player");
            _player = player != null ? player.transform : null;
            if (_player != null)
            {
                Transform aim = _player.Find(PlayerAppearance.AimName);
                _feet = _player.position;
                _anchor = aim != null ? aim.position : _feet + Vector3.up * FallbackAimHeight;
            }
            else
            {
                _feet = _startPosition;
                _anchor = _startPosition;
            }

            _range = Mathf.Max(MaxRange, Vector3.Distance(_anchor, _startPosition));
            IsActive = true;
            ApplyNearHider(_startPosition);
            return true;
        }

        /// <summary>入ったときの位置・向き・画角に戻し、肩越しカメラを動かし直す。入っていなければ何もしない。</summary>
        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            _nearHider.Show();

            if (_camera != null)
            {
                _camera.transform.SetPositionAndRotation(_startPosition, _startRotation);
                _camera.fieldOfView = _startFieldOfView;
            }

            foreach (Behaviour behaviour in _paused)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }

            _paused.Clear();
            _camera = null;
            _player = null;
        }

        private void OnDisable()
        {
            End();
        }

        private void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            if (_camera == null)
            {
                // カメラが消えた（シーンの切り替えなど）。止めた肩越しカメラだけ戻す。
                End();
                return;
            }

            Transform cameraTransform = _camera.transform;
            if (KCDInput.PhotoResetPressed)
            {
                cameraTransform.SetPositionAndRotation(_startPosition, _startRotation);
                _camera.fieldOfView = _startFieldOfView;
                SetAngles(_startRotation);
                ApplyNearHider(_startPosition);
                return;
            }

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MaxStepTime);
            Vector2 look = KCDInput.PhotoLook;
            if (look.sqrMagnitude <= LookSpikeLimit * LookSpikeLimit)
            {
                _yaw = Mathf.Repeat(_yaw + look.x * LookSensitivity, 360f);
                _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, -MaxPitch, MaxPitch);
            }

            Vector3 position = cameraTransform.position;
            Vector3 motion = MotionFor(_yaw, KCDInput.PhotoMove, KCDInput.PhotoVertical,
                KCDInput.PhotoFast ? FastMoveSpeed : MoveSpeed, deltaTime);
            if (motion.sqrMagnitude > 1e-10f)
            {
                Vector3 desired = position + motion;
                if (_player != null)
                {
                    desired = CameraMath.PushOutOfCapsule(desired, _feet, _anchor, PhotoCameraMotion.PlayerClearance);
                }

                position = PhotoCameraMotion.Constrain(position, desired, _anchor, _range);
            }

            float zoom = KCDInput.PhotoZoom;
            if (Mathf.Abs(zoom) > 0.0001f)
            {
                _camera.fieldOfView = Mathf.Clamp(
                    _camera.fieldOfView - zoom * ZoomStepDegrees, MinFieldOfView, MaxFieldOfView);
            }

            cameraTransform.SetPositionAndRotation(position, Quaternion.Euler(_pitch, _yaw, 0f));
            ApplyNearHider(position);
        }

        /// <summary>
        /// このフレームの移動量。前後左右は水平（向きのヨーだけ使う）、上下は真上。斜めでも速さは変えない。
        /// </summary>
        public static Vector3 MotionFor(float yaw, Vector2 move, float vertical, float speed, float deltaTime)
        {
            if (deltaTime <= 0f || speed <= 0f)
            {
                return Vector3.zero;
            }

            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 direction = heading * new Vector3(move.x, 0f, move.y) + Vector3.up * vertical;
            return Vector3.ClampMagnitude(direction, 1f) * (speed * deltaTime);
        }

        private void SetAngles(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -MaxPitch, MaxPitch);
        }

        private void PauseOrbitCamera(GameObject cameraObject)
        {
            _paused.Clear();
#if KCD_CINEMACHINE
            // 肩越しカメラが寄りすぎで体を隠していたら先に出す。止めたあとは隠し直さない。
            CinemachineCameraGuard guard = FindAnyObjectByType<CinemachineCameraGuard>();
            if (guard != null)
            {
                guard.ShowPlayer();
            }

            Pause(cameraObject.GetComponent<CinemachineBrain>());
#endif
            Pause(cameraObject.GetComponent<ThirdPersonCamera>());
        }

        private void Pause(Behaviour behaviour)
        {
            if (behaviour != null && behaviour.enabled)
            {
                behaviour.enabled = false;
                _paused.Add(behaviour);
            }
        }

        private void ApplyNearHider(Vector3 position)
        {
            if (_player == null)
            {
                _nearHider.Show();
                return;
            }

            _nearHider.Apply(_player, CameraMath.DistanceToSegment(position, _feet, _anchor));
        }
    }
}
