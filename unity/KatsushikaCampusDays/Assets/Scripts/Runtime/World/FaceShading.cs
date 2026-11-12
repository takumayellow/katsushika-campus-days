using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 顔の陰を頭の向きで決める KCD/Toon の _FACE_SHADOW_ON (#14) の決まりごと。
    ///
    /// 顔の頂点には、tangent に頭の前方向（xyz）と印（w = <see cref="FrameTag"/> + (1 − 輪郭線の太さの倍率)）を、
    /// UV の 2 枚目の x に顔の左右の位置を焼く（CharacterImporter.BakeFaceFrame）。輪郭線の殻の向きは
    /// tangent から法線へ移す。シェーダの式は <see cref="Lit"/> が C# で同じものを持つ。
    /// </summary>
    public static class FaceShading
    {
        public const string Keyword = "_FACE_SHADOW_ON";

        /// <summary>顔の頂点の tangent.w の印。Outline パスは w がこれより 0.5 大きい手前から顔の頂点とみなす。</summary>
        public const float FrameTag = 4f;

        /// <summary>顔の左右の位置を焼く UV のチャンネル（髪の天使の輪と同じ 2 枚目。顔の頂点では使っていない）。</summary>
        public const int SideUvChannel = 1;

        /// <summary>
        /// 顔の縁の左右の位置。Blender の顔の面は正面から左右 74° ほどまで回り込むので、
        /// 頭の水平の断面を円とみなすと縁は sin 74° = 0.96 に来る。
        /// </summary>
        public const float EdgeSine = 0.96f;

        /// <summary>顔の陰の境目の幅（_ShadeCrisp = 0 のとき）。</summary>
        public const float Softness = 0.03f;

        /// <summary>斜めの光で境目を陰の側へずらす量。光と反対の頬に細く陰が残る。</summary>
        public const float Bias = 0.1f;

        /// <summary>
        /// 顔の頂点に焼く左右の位置。center / halfWidth は顔の面の左右の中心と半幅（モデルの根元の空間）。
        /// キャラの右（+X）が正。縁で <see cref="EdgeSine"/> になり、はみ出しても ±1 で止める。
        /// </summary>
        public static float Side(float x, float center, float halfWidth)
        {
            if (halfWidth <= 1e-6f)
            {
                return 0f;
            }

            return Mathf.Clamp((x - center) / halfWidth * EdgeSine, -1f, 1f);
        }

        /// <summary>
        /// シェーダの FaceLit と同じ式（顔の明部の割合、0〜1。影の減衰は掛けない。境目は softness の幅）。
        /// headForward は頭の前方向、side は焼いた左右の位置、lightDirection は光へ向かう向き
        /// （URP の Light.direction と同じ向き）。すべてワールド空間。
        /// </summary>
        public static float Lit(Vector3 headForward, float side, Vector3 lightDirection, float softness, float bias)
        {
            var forward = new Vector2(headForward.x, headForward.z);
            forward *= 1f / Mathf.Sqrt(Mathf.Max(Vector2.Dot(forward, forward), 1e-6f));
            var right = new Vector2(forward.y, -forward.x);
            var lightH = new Vector2(lightDirection.x, lightDirection.z);
            float lengthH = lightH.magnitude;
            Vector2 lightN = lightH / Mathf.Max(lengthH, 1e-4f);
            float front = Vector2.Dot(forward, lightN);
            float across = Vector2.Dot(right, lightN);
            float facing = side * across + Mathf.Sqrt(Mathf.Clamp01(1f - side * side)) * front;
            float threshold = Mathf.Lerp(-1f - 2f * softness, -bias * (1f - Mathf.Abs(front)), Mathf.Clamp01(lengthH * 2f));
            return SmoothStep(threshold - softness, threshold + softness, facing);
        }

        /// <summary>HLSL の smoothstep と同じ（Mathf.SmoothStep は引数の意味が違う）。</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
