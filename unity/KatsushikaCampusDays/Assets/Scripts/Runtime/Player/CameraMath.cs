using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 肩越しカメラとフォトモードの自由カメラで使う、距離と高さの計算 (#12)。
    /// 物理にも Cinemachine にも触らない純粋関数だけを置き、EditMode テストで確かめる。
    /// </summary>
    public static class CameraMath
    {
        /// <summary>
        /// 見る点からカメラまでに置いてよい距離。
        /// hitDistance は見る点からカメラの向きへ球を飛ばして、最初に当たった所までの距離（当たらなければ負）。
        /// 当たったらそこから skin だけ手前に置き、望む距離 desired より遠くにはしない。
        /// minDistance より近くにもしない（見る点に重なるとカメラの向きが決まらない）。desired がそれより近ければ desired。
        /// </summary>
        public static float AllowedDistance(float desired, float hitDistance, float skin, float minDistance)
        {
            desired = Mathf.Max(0f, desired);
            if (hitDistance < 0f)
            {
                return desired;
            }

            float nearest = Mathf.Min(Mathf.Max(0f, minDistance), desired);
            return Mathf.Clamp(hitDistance - Mathf.Max(0f, skin), nearest, desired);
        }

        /// <summary>
        /// 天井の下に収めたカメラの高さ。天井の高さ ceilingY から margin だけ下を上限にする。
        /// ただし見る点の高さ pivotY より下へは押し下げない（天井が見る点のすぐ上にあるときに床へ潜らせない）。
        /// 天井が無い（+∞ や NaN）ときと、もともと上限より低いときはそのまま。
        /// </summary>
        public static float CeilingClampedHeight(float cameraY, float pivotY, float ceilingY, float margin)
        {
            if (float.IsNaN(ceilingY) || float.IsInfinity(ceilingY))
            {
                return cameraY;
            }

            float limit = Mathf.Max(ceilingY - Mathf.Max(0f, margin), pivotY);
            return Mathf.Min(cameraY, limit);
        }

        /// <summary>中心 center から radius より外にある点を、その向きのまま球の表面まで戻す。内側ならそのまま。</summary>
        public static Vector3 ClampToSphere(Vector3 center, float radius, Vector3 point)
        {
            float limit = Mathf.Max(0f, radius);
            Vector3 offset = point - center;
            if (offset.sqrMagnitude <= limit * limit)
            {
                return point;
            }

            return center + offset.normalized * limit;
        }

        /// <summary>
        /// 球の軌道（CinemachineOrbitalFollow の Sphere）でカメラが見る点より上に出る最大の高さ。
        /// 半径 × 半径倍率の上限 × sin(縦角の上限)。縦角が負なら 0。
        /// </summary>
        public static float MaxOrbitHeight(float radius, float maxRadialScale, float maxPitchDegrees)
        {
            return Mathf.Max(0f, radius * maxRadialScale * Mathf.Sin(maxPitchDegrees * Mathf.Deg2Rad));
        }
    }
}
