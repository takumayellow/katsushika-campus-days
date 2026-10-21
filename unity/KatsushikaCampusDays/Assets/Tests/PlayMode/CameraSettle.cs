using System.Collections;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// カメラと注視点の距離が落ち着くまで待つ。軌道カメラは入力のあとも減衰しながら動き続ける。
    /// 基準にした距離から 1 mm 以内のまま 0.3 秒と 5 フレームの両方を超えたら、落ち着いたとみなす。
    /// フレームが速い（-nographics など）と 1 フレームあたりの動きが小さくなるので、フレームの数だけでは決めない。
    /// </summary>
    internal sealed class CameraSettle
    {
        private const float MinSeconds = 1.5f;
        private const float MaxSeconds = 8f;
        private const float StillSeconds = 0.3f;
        private const int StillFrames = 5;
        private const float Tolerance = 0.001f;

        private readonly Transform _camera;
        private readonly Transform _aim;

        public CameraSettle(Transform camera, Transform aim)
        {
            _camera = camera;
            _aim = aim;
        }

        /// <summary>最後に待ったときの、落ち着いた距離（m）。</summary>
        public float Distance { get; private set; }

        /// <summary>
        /// 落ち着くまで待つ。入力を入れた直後に呼ぶ。入力が効き始めるまでの数フレームで止まらないよう、
        /// 少なくとも 1.5 秒は待つ。8 秒を過ぎたら、その時点の距離で打ち切る。
        /// </summary>
        public IEnumerator Wait()
        {
            float start = Time.realtimeSinceStartup;
            float anchor = Measure();
            float anchorTime = start;
            float now = anchor;
            int frames = 0;
            while (Time.realtimeSinceStartup - start < MaxSeconds)
            {
                yield return null;
                now = Measure();
                float time = Time.realtimeSinceStartup;
                if (Mathf.Abs(now - anchor) >= Tolerance)
                {
                    anchor = now;
                    anchorTime = time;
                    frames = 0;
                    continue;
                }

                frames++;
                if (frames >= StillFrames && time - anchorTime >= StillSeconds && time - start >= MinSeconds)
                {
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
