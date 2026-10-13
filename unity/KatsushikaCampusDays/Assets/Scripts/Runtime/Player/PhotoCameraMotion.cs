using UnityEngine;

namespace KCD
{
    /// <summary>
    /// フォトモードの自由カメラの動ける範囲 (#12)。
    /// プレイヤーの見る点から一定の距離の内側に留め、壁・床・天井・木の幹（<see cref="CameraObstacleFilter"/> が止めるもの）は
    /// 球の掃引で抜けない。当たったら面の手前で止め、残りの動きは面に沿って 1 回だけ滑らせる。
    /// プレイヤーの体の中にも入らない（芯から <see cref="PlayerClearance"/> 離す）。
    /// </summary>
    public static class PhotoCameraMotion
    {
        /// <summary>カメラの当たり判定の半径（m）。肩越しカメラ（0.25 m）より小さくし、狭い所にも入れる。</summary>
        public const float CastRadius = 0.15f;

        /// <summary>当たった面からさらに離しておく距離（m）。</summary>
        public const float Skin = 0.05f;

        /// <summary>
        /// プレイヤーの芯（足元から見る点まで）からこれより近くへは寄せない（m）。
        /// 体を隠す距離（<see cref="CameraNearHider.ShowAbove"/>）と同じにして、寄った写真でも体が消えないようにする。
        /// </summary>
        public const float PlayerClearance = CameraNearHider.ShowAbove;

        /// <summary>
        /// from から desired へ動かした先。anchor から range の内側に留め、止める当たり判定の手前で止まる。
        /// 当たった分の残りは面に沿って滑らせ、その動きも同じ掃引で確かめる（角では止まる）。
        /// </summary>
        public static Vector3 Constrain(Vector3 from, Vector3 desired, Vector3 anchor, float range)
        {
            Vector3 target = CameraMath.ClampToSphere(anchor, range, desired);
            Vector3 reached = SweepTo(from, target, out Vector3 normal, out bool hit);
            if (!hit)
            {
                return reached;
            }

            Vector3 slide = Vector3.ProjectOnPlane(target - reached, normal);
            if (slide.sqrMagnitude < 1e-8f)
            {
                return reached;
            }

            Vector3 slideTarget = CameraMath.ClampToSphere(anchor, range, reached + slide);
            return SweepTo(reached, slideTarget, out _, out _);
        }

        /// <summary>from から to へ球を飛ばし、当たればその skin 手前、当たらなければ to を返す。</summary>
        private static Vector3 SweepTo(Vector3 from, Vector3 to, out Vector3 normal, out bool hit)
        {
            normal = Vector3.zero;
            hit = false;

            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-5f)
            {
                return from;
            }

            Vector3 direction = delta / length;
            float distance = CameraObstacleFilter.Sweep(from, CastRadius, direction, length + Skin, out normal);
            if (distance < 0f)
            {
                return to;
            }

            hit = true;
            return from + direction * Mathf.Clamp(distance - Skin, 0f, length);
        }
    }
}
