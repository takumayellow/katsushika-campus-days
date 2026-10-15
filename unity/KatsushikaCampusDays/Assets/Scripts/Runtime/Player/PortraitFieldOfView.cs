using UnityEngine;
#if KCD_CINEMACHINE
using Unity.Cinemachine;
#endif

namespace KCD
{
    /// <summary>
    /// 縦長の画面では縦の画角を広げ、横の画角を <see cref="_minHorizontal"/> 度より狭くしない。
    /// 縦の画角 55 度のままスマホを縦に持つ（幅と高さの比 0.46）と、横は約 27 度しか見えず、目の前の建物しか映らない。
    /// 広げすぎると端がゆがむので、縦の画角は <see cref="_maxVertical"/> 度で止める。
    /// 同じ物の CinemachineCamera があればそのレンズを、無ければ Camera を書き換える。
    /// 写真モードの間は <see cref="PhotoFreeCamera"/> が止める（止めないと、写真モードのズームを毎フレーム戻してしまう）。
    /// </summary>
    public sealed class PortraitFieldOfView : MonoBehaviour
    {
        [SerializeField] private float _minHorizontal = 50f;
        [SerializeField] private float _maxVertical = 80f;

        private float _baseVertical;
        private Camera _camera;
#if KCD_CINEMACHINE
        private CinemachineCamera _vcam;
#endif

        /// <summary>
        /// 幅と高さの比 aspect の画面での縦の画角。横長（aspect ≥ 1）なら baseVertical のまま。
        /// 縦長なら横の画角が minHorizontal になる縦の画角まで広げ、maxVertical で止める。baseVertical より狭くはしない。
        /// </summary>
        public static float VerticalFor(float baseVertical, float aspect, float minHorizontal, float maxVertical)
        {
            if (aspect <= 0f || aspect >= 1f)
            {
                return baseVertical;
            }

            float halfHorizontal = minHorizontal * 0.5f * Mathf.Deg2Rad;
            float needed = 2f * Mathf.Atan(Mathf.Tan(halfHorizontal) / aspect) * Mathf.Rad2Deg;
            return Mathf.Max(baseVertical, Mathf.Min(needed, maxVertical));
        }

        private void Awake()
        {
#if KCD_CINEMACHINE
            _vcam = GetComponent<CinemachineCamera>();
            if (_vcam != null)
            {
                _baseVertical = _vcam.Lens.FieldOfView;
                return;
            }
#endif
            _camera = GetComponent<Camera>();
            _baseVertical = _camera != null ? _camera.fieldOfView : 0f;
        }

        private void LateUpdate()
        {
            if (Screen.height <= 0)
            {
                return;
            }

            float aspect = (float)Screen.width / Screen.height;
            float fov = VerticalFor(_baseVertical, aspect, _minHorizontal, _maxVertical);
#if KCD_CINEMACHINE
            if (_vcam != null)
            {
                if (!Mathf.Approximately(_vcam.Lens.FieldOfView, fov))
                {
                    _vcam.Lens.FieldOfView = fov;
                }

                return;
            }
#endif
            if (_camera != null && !Mathf.Approximately(_camera.fieldOfView, fov))
            {
                _camera.fieldOfView = fov;
            }
        }
    }
}
