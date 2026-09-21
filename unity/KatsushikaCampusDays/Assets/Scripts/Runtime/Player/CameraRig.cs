#if KCD_CINEMACHINE
using Unity.Cinemachine;
#endif
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// カメラをプレイヤーの背後へ即座に回り込ませる。シーン開始・ワープ・ロードの直後に使う。
    /// Cinemachine の軌道カメラと自前の ThirdPersonCamera のどちらが載っていても同じ呼び方で済ませる。
    /// </summary>
    public static class CameraRig
    {
        private const float DefaultPitch = 12f;

        /// <summary>対象の向きの真後ろへカメラを置く。減衰は打ち切る。</summary>
        public static void SnapBehind(Transform target)
        {
            if (target == null)
            {
                return;
            }

            ThirdPersonCamera follow = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (follow != null)
            {
                follow.SnapBehindTarget();
                return;
            }

#if KCD_CINEMACHINE
            SnapOrbital(target);
#endif
        }

#if KCD_CINEMACHINE
        private static void SnapOrbital(Transform target)
        {
            CinemachineOrbitalFollow orbital = Object.FindAnyObjectByType<CinemachineOrbitalFollow>();
            if (orbital == null)
            {
                return;
            }

            // 軌道の水平軸はワールド基準（BindingMode.WorldSpace）なので、対象のヨーをそのまま入れる
            float yaw = Mathf.DeltaAngle(0f, target.eulerAngles.y);
            orbital.HorizontalAxis.Value = orbital.HorizontalAxis.ClampValue(yaw);
            orbital.VerticalAxis.Value = orbital.VerticalAxis.ClampValue(DefaultPitch);

            CinemachineCamera vcam = orbital.GetComponent<CinemachineCamera>();
            if (vcam == null)
            {
                return;
            }

            Transform anchor = vcam.Follow != null ? vcam.Follow : target;
            Quaternion rotation = Quaternion.Euler(DefaultPitch, yaw, 0f);
            Vector3 position = anchor.position + rotation * new Vector3(0f, 0f, -orbital.Radius);
            vcam.ForceCameraPosition(position, rotation);
        }
#endif
    }
}
