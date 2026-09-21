using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 肩越しの三人称カメラ。Cinemachine が無くても単体で成立する自前実装。
    /// 追従点はキャラクターの頭のやや上に置き、肩オフセットで画面の中心を少しずらす。
    /// 壁に潜り込まないよう SphereCast で寄せ、視界が抜けたらなめらかに戻す。
    /// </summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("追従")]
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _targetOffset = new Vector3(0f, 1.45f, 0f);
        [SerializeField] private Vector2 _shoulderOffset = new Vector2(0.45f, 0f);
        [SerializeField] private float _followDamping = 12f;

        [Header("距離")]
        [SerializeField] private float _distance = 4.2f;
        [SerializeField] private float _minDistance = 1.4f;
        [SerializeField] private float _maxDistance = 7.5f;
        [SerializeField] private float _zoomSpeed = 6f;

        [Header("回転")]
        [SerializeField] private float _yaw = 0f;
        [SerializeField] private float _pitch = 12f;
        [SerializeField] private float _minPitch = -28f;
        [SerializeField] private float _maxPitch = 62f;
        [SerializeField] private float _sensitivity = 2.2f;

        [Header("遮蔽回避")]
        [SerializeField] private float _collisionRadius = 0.28f;
        [SerializeField] private LayerMask _collisionMask = ~0;
        [SerializeField] private float _collisionRecoverSpeed = 7f;

        private float _currentDistance;
        private float _occludedDistance;
        private Vector3 _pivot;
        private float _lookGraceUntil;
        private bool _pivotInitialised;

        /// <summary>追従対象。SceneBuilder がプレイヤーを差し込む。</summary>
        public Transform Target
        {
            get => _target;
            set
            {
                _target = value;
                _pivotInitialised = false;
            }
        }

        /// <summary>会話中などにカメラ操作を止める。</summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            _currentDistance = _distance;
            _occludedDistance = _distance;

            // Ground / Building だけを遮蔽判定に使う（NPC やトリガーでカメラが跳ねないように）。
            int ground = LayerMask.NameToLayer("Ground");
            int building = LayerMask.NameToLayer("Building");
            if (ground >= 0 && building >= 0)
            {
                _collisionMask = (1 << ground) | (1 << building);
            }
        }

        private void OnEnable()
        {
            if (_target != null)
            {
                _yaw = _target.eulerAngles.y;
            }

            // ウィンドウ生成直後はカーソルの位置合わせで大きな差分が来るので、少しのあいだ視点入力を捨てる
            _lookGraceUntil = Time.unscaledTime + 0.6f;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            ApplyLookInput();
            UpdatePivot();

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 desiredPosition = _pivot + rotation * new Vector3(_shoulderOffset.x, _shoulderOffset.y, -_distance);

            float allowed = ResolveOcclusion(_pivot, desiredPosition);
            _currentDistance = allowed < _currentDistance
                ? allowed
                : Mathf.Lerp(_currentDistance, allowed, _collisionRecoverSpeed * Time.deltaTime);

            Vector3 finalPosition = _pivot + rotation * new Vector3(_shoulderOffset.x, _shoulderOffset.y, -_currentDistance);
            transform.SetPositionAndRotation(finalPosition, rotation);
        }

        private void ApplyLookInput()
        {
            if (!InputEnabled)
            {
                return;
            }

            if (Time.unscaledTime < _lookGraceUntil)
            {
                return;
            }

            Vector2 look = KCDInput.Look;
            if (look.sqrMagnitude > 40f * 40f)
            {
                // フォーカス切り替えやカーソルの位置合わせで飛んだ差分は視点操作ではない
                SmokeProbe.Log("look-dropped " + look.ToString("F1") + " t=" + Time.unscaledTime.ToString("F2"));
                return;
            }

            if (look.sqrMagnitude > 4f)
            {
                SmokeProbe.Log("look " + look.ToString("F1") + " t=" + Time.unscaledTime.ToString("F2"));
            }

            _yaw += look.x * _sensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * _sensitivity, _minPitch, _maxPitch);

            float zoom = KCDInput.Zoom;
            if (Mathf.Abs(zoom) > 0.0001f)
            {
                _distance = Mathf.Clamp(_distance - zoom * _zoomSpeed, _minDistance, _maxDistance);
            }
        }

        private void UpdatePivot()
        {
            Vector3 targetPivot = _target.position + _targetOffset;
            if (!_pivotInitialised)
            {
                _pivot = targetPivot;
                _pivotInitialised = true;
                _yaw = _target.eulerAngles.y;
                return;
            }

            _pivot = Vector3.Lerp(_pivot, targetPivot, 1f - Mathf.Exp(-_followDamping * Time.deltaTime));
        }

        private float ResolveOcclusion(Vector3 pivot, Vector3 desiredPosition)
        {
            Vector3 direction = desiredPosition - pivot;
            float length = direction.magnitude;
            if (length < 0.0001f)
            {
                return _distance;
            }

            direction /= length;

            if (Physics.SphereCast(pivot, _collisionRadius, direction, out RaycastHit hit, length,
                    _collisionMask, QueryTriggerInteraction.Ignore))
            {
                _occludedDistance = Mathf.Max(_minDistance * 0.6f, hit.distance - _collisionRadius);
                return _occludedDistance;
            }

            return _distance;
        }

        /// <summary>プレイヤーの正面へ即座に回り込む。ワープやシーン開始時に使う。</summary>
        public void SnapBehindTarget()
        {
            if (_target == null)
            {
                return;
            }

            _yaw = _target.eulerAngles.y;
            _pitch = 12f;
            _pivotInitialised = false;
            _currentDistance = _distance;
            _lookGraceUntil = Time.unscaledTime + 0.6f;
            LateUpdate();
        }
    }
}
