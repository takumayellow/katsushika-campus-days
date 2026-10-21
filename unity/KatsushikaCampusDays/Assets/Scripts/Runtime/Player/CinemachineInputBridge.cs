#if KCD_CINEMACHINE
using Unity.Cinemachine;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Cinemachine の軌道カメラ（CinemachineOrbitalFollow）の軸を KCDInput につなぐ。
    /// 既定の CinemachineInputAxisController は InputActionReference アセットを要求するため、
    /// コードだけでシーンを組む都合に合わせて軸を直接動かす。
    /// 軸は名指しで動かす。OrbitalFollow が軸に付ける目安（Hint）は役割を表さず、
    /// 半径の軸（Orbit Scale）にも縦の軸と同じ Y が付いている。
    /// 起動時に、同じカメラの壁よけを CinemachineCameraGuard にそろえる (#12)。
    /// </summary>
    public sealed class CinemachineInputBridge : MonoBehaviour
    {
        [SerializeField] private float _lookSensitivity = 2.2f;

        /// <summary>
        /// ズームの量（<see cref="KCDInput.Zoom"/>）に掛ける感度。掛けた量 z で、軌道の半径の倍率を e^-z 倍にする
        /// （<see cref="CameraMath.ZoomedRadialScale"/>）。
        /// </summary>
        [SerializeField] private float _zoomSensitivity = 3.5f;

        [SerializeField] private bool _invertVertical = true;

        private CinemachineOrbitalFollow _orbital;

        /// <summary>会話中などに視点操作を止める。</summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            EnsureCameraGuard(gameObject);
        }

        private void OnEnable()
        {
            _orbital = GetComponentInChildren<CinemachineOrbitalFollow>();
        }

        /// <summary>
        /// 壁よけを CinemachineCameraGuard にそろえる。作り直す前のシーン（CinemachineDeoccluder が付いたまま
        /// 保存されている）でも、起動時に Deoccluder を止めて Guard を足す。二重に寄せないよう Deoccluder は止めたままにする。
        /// </summary>
        public static CinemachineCameraGuard EnsureCameraGuard(GameObject camera)
        {
            if (camera == null)
            {
                return null;
            }

            CinemachineDeoccluder deoccluder = camera.GetComponent<CinemachineDeoccluder>();
            if (deoccluder != null)
            {
                deoccluder.enabled = false;
            }

            CinemachineCameraGuard guard = camera.GetComponent<CinemachineCameraGuard>();
            if (guard == null)
            {
                guard = camera.AddComponent<CinemachineCameraGuard>();
            }

            return guard;
        }

        private void Update()
        {
            if (!InputEnabled || _orbital == null || KCDInput.LookBlocked || KCDInput.PhotoMode)
            {
                return;
            }

            // 設定画面の感度と上下の反転 (#6) を、シーンに置いた感度と向きに重ねる。
            float lookSensitivity = _lookSensitivity * CameraPrefs.SensitivityScale;
            float verticalSign = (_invertVertical ? -1f : 1f) * CameraPrefs.VerticalSign;
            Vector2 look = KCDInput.Look;
            float zoom = KCDInput.Zoom * _zoomSensitivity;

            Nudge(ref _orbital.HorizontalAxis, look.x * lookSensitivity);
            Nudge(ref _orbital.VerticalAxis, look.y * lookSensitivity * verticalSign);
            if (Mathf.Abs(zoom) >= 0.0001f)
            {
                ref InputAxis radial = ref _orbital.RadialAxis;
                radial.Value = radial.ClampValue(CameraMath.ZoomedRadialScale(radial.Value, zoom));
            }
        }

        private static void Nudge(ref InputAxis axis, float delta)
        {
            if (Mathf.Abs(delta) < 0.0001f)
            {
                return;
            }

            axis.Value = axis.ClampValue(axis.Value + delta);
        }
    }
}
#endif
