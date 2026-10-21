using System.Collections;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// カメラが落ち着くまで待つ。軌道カメラは入力のあとも減衰しながら動き続ける。
    /// 注視点との距離が基準から 1 mm 以内、向きが基準から 0.02 度以内のまま、0.3 秒と 5 フレームの両方を超えたら、
    /// 落ち着いたとみなす。縦や横に回すだけでは距離がほとんど変わらないので、向きも見る。
    /// フレームが速い（-nographics など）と 1 フレームあたりの動きが小さくなるので、フレームの数だけでは決めない。
    /// </summary>
    internal sealed class CameraSettle
    {
        private const float MinSeconds = 1.5f;
        private const float MaxSeconds = 8f;
        private const float StillSeconds = 0.3f;
        private const int StillFrames = 5;
        private const float Tolerance = 0.001f;

        /// <summary>
        /// 向きの許容（度）。単位ベクトルの差の長さで比べる。小さい角ではラジアンの角度と同じで、
        /// 内積の逆余弦（Vector3.Angle）より細かく測れる。Vector3.Angle は float では 0.02 度ほどの刻みになる。
        /// </summary>
        private const float AngleTolerance = 0.02f;

        private readonly Transform _camera;
        private readonly Transform _aim;

        public CameraSettle(Transform camera, Transform aim)
        {
            _camera = camera;
            _aim = aim;
        }

        /// <summary>最後に待ったときの、カメラと注視点の距離（m）。打ち切ったときは、打ち切った時点の距離。</summary>
        public float Distance { get; private set; }

        /// <summary>最後に待ったとき、落ち着く前に 8 秒が過ぎて打ち切ったか。</summary>
        public bool TimedOut { get; private set; }

        /// <summary>
        /// 落ち着くまで待つ。入力を入れた直後に呼ぶ。入力が効き始めるまでの数フレームで止まらないよう、
        /// 少なくとも 1.5 秒は待つ。8 秒を過ぎたら打ち切り、<see cref="TimedOut"/> を立てる。
        /// </summary>
        public IEnumerator Wait()
        {
            float start = Time.realtimeSinceStartup;
            float anchor = Measure();
            Vector3 anchorForward = _camera.forward;
            float anchorTime = start;
            float now = anchor;
            int frames = 0;
            TimedOut = true;
            while (Time.realtimeSinceStartup - start < MaxSeconds)
            {
                yield return null;
                now = Measure();
                Vector3 forward = _camera.forward;
                float time = Time.realtimeSinceStartup;
                if (Mathf.Abs(now - anchor) >= Tolerance ||
                    Vector3.Distance(forward, anchorForward) >= AngleTolerance * Mathf.Deg2Rad)
                {
                    anchor = now;
                    anchorForward = forward;
                    anchorTime = time;
                    frames = 0;
                    continue;
                }

                frames++;
                if (frames >= StillFrames && time - anchorTime >= StillSeconds && time - start >= MinSeconds)
                {
                    TimedOut = false;
                    break;
                }
            }

            Distance = now;
        }

        private float Measure()
        {
            return Vector3.Distance(_camera.position, _aim.position);
        }
    }
}
