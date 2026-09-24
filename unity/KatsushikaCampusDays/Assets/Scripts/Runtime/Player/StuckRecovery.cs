using UnityEngine;

namespace KCD
{
    /// <summary>
    /// CharacterController が当たり判定に食い込んで動けなくなったときの脱出。
    /// 屋内は箱と厚み 0 の板をマテリアルごとに合成した非凸 MeshCollider なので、
    /// 段差越えや薄板との接触でカプセルが内部面に入り込むと PhysX が押し出せず固まる。
    /// 毎フレーム「カプセル本体（接触帯 ContactBand を除く）が固体と重なっているか」を見て、
    /// 重なっていれば ComputePenetration で押し出し、それでも駄目なら周囲の空き場所へ移す。
    /// 壁や地面の継ぎ目に押し付けて止まっているだけの接触は重なりに数えない（数えると毎フレーム押し出して震える）。
    /// </summary>
    public static class StuckRecovery
    {
        /// <summary>空き場所を探す輪の間隔（m）。</summary>
        public const float RingStep = 0.35f;

        /// <summary>空き場所を探す輪の数。RingStep * Rings が最大探索距離（2.8 m。プランターや家具の幅を越える, #30）。</summary>
        public const int Rings = 8;

        /// <summary>輪 1 周あたりの方向数。</summary>
        public const int Directions = 8;

        /// <summary>候補の足元を探すレイの長さ（m）。この範囲に床が無い候補は使わない。</summary>
        public const float GroundProbe = 2.0f;

        private static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>
        /// 現在位置を中心に、近い順・平面上に並ぶ候補オフセット。
        /// 最初に少し上（段差に噛んだとき用）、次に水平の輪を外へ広げる。
        /// </summary>
        public static Vector3[] CandidateOffsets(float step, int rings, int directions)
        {
            if (step <= 0f || rings <= 0 || directions <= 0)
            {
                return new Vector3[0];
            }

            var offsets = new Vector3[1 + rings * directions];
            offsets[0] = new Vector3(0f, step, 0f);
            int index = 1;
            for (int ring = 1; ring <= rings; ring++)
            {
                float radius = step * ring;
                // 輪ごとに半歩ずらして、同じ方向ばかり試さないようにする。
                float phase = ring % 2 == 0 ? Mathf.PI / directions : 0f;
                for (int d = 0; d < directions; d++)
                {
                    float angle = phase + Mathf.PI * 2f * d / directions;
                    offsets[index++] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                }
            }

            return offsets;
        }

        /// <summary>
        /// プレイヤー以外の固体レイヤー。トリガーは別途 QueryTriggerInteraction.Ignore で除く。
        /// NPC も除く。NPC は歩いて動くので、すれ違いざまにカプセルが重なっただけで「埋まった」と見なし、
        /// プレイヤーを押し出したりワープさせたりしないため (#30)。
        /// </summary>
        public static int SolidMask(GameObject self)
        {
            int mask = Physics.DefaultRaycastLayers;
            mask &= ~(1 << self.layer);

            int npc = LayerMask.NameToLayer("NPC");
            if (npc >= 0)
            {
                mask &= ~(1 << npc);
            }

            return mask;
        }

        /// <summary>
        /// カプセルの両端球の中心を返す。shrink だけ半径と高さを縮めると
        /// 接触帯を外した「本体」になる。
        /// </summary>
        public static void CapsuleEnds(CharacterController controller, Vector3 position, float shrink,
            out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = Mathf.Max(0.01f, controller.radius - shrink);
            float half = Mathf.Max(0f, controller.height * 0.5f - radius - shrink);
            Vector3 center = position + controller.center;
            bottom = center - Vector3.up * half;
            top = center + Vector3.up * half;
        }

        /// <summary>
        /// 接触として許す食い込みの深さ（m）。CharacterController は相手に skinWidth まで食い込んで止まる仕様で
        /// （Unity マニュアル Character Controller の Skin Width）、凹凸のある壁や厚み 0 の地面の継ぎ目では
        /// さらに少し沈む。これ以内の重なりは「押し付けて止まっている」だけなので脱出処理を起こさない。
        /// 以前は skinWidth の半分で判定していたため、壁に当たるたびに押し出し・ワープが走ってガタガタしていた。
        /// </summary>
        public static float ContactBand(float skinWidth)
        {
            return 2f * Mathf.Max(0f, skinWidth);
        }

        /// <summary>ComputePenetration の深さが接触帯を越えた本当のめり込みか。</summary>
        public static bool IsRealOverlap(float depth, float skinWidth)
        {
            return depth > ContactBand(skinWidth);
        }

        /// <summary>本体が固体と重なっているか。壁に押し付けているだけ（接触帯以内）なら false。</summary>
        public static bool IsPenetrating(CharacterController controller, int mask)
        {
            CapsuleEnds(controller, controller.transform.position, ContactBand(controller.skinWidth),
                out Vector3 bottom, out Vector3 top, out float radius);
            return Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 重なっているコライダーから押し出すベクトルを合成する。接触帯以内の重なり（床に立つ・壁に触れる）は押さない。
        /// ComputePenetration は裏向きの三角形を無視するので、厚み 0 の板に裏から入り込んだときなど
        /// 押し出せる相手が無ければ false（呼び出し側が TryFindFreeSpot に回す）。
        /// </summary>
        public static bool TryDepenetrate(CharacterController controller, int mask, out Vector3 correction)
        {
            correction = Vector3.zero;
            Transform t = controller.transform;
            CapsuleEnds(controller, t.position, 0f, out Vector3 bottom, out Vector3 top, out float radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, Overlaps, mask, QueryTriggerInteraction.Ignore);
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                Collider other = Overlaps[i];
                Overlaps[i] = null;
                if (other == null || other == controller)
                {
                    continue;
                }

                if (Physics.ComputePenetration(controller, t.position, t.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance) && IsRealOverlap(distance, controller.skinWidth))
                {
                    correction += direction * (distance + controller.skinWidth);
                    any = true;
                }
            }

            // 角で複数コライダーに挟まれると合成が過大になり薄い壁を抜けるので、1 回の押し出しは半径までに抑える。
            // 足りなければ次フレームにもう一度押し出すか、TryFindFreeSpot に落ちる。
            correction = Vector3.ClampMagnitude(correction, controller.radius);
            return any && correction.sqrMagnitude > 1e-8f;
        }

        /// <summary>
        /// 周囲の空き場所を探す。カプセル本体が固体と重ならず、足元 GroundProbe 以内に床がある候補を、
        /// 近い順に返す。見つからなければ false。
        /// </summary>
        public static bool TryFindFreeSpot(CharacterController controller, int mask, out Vector3 position)
        {
            Vector3 origin = controller.transform.position;
            Vector3[] offsets = CandidateOffsets(RingStep, Rings, Directions);
            foreach (Vector3 offset in offsets)
            {
                Vector3 candidate = origin + offset;
                if (!IsFree(controller, candidate, mask))
                {
                    continue;
                }

                // 足元に床があるか。宙に浮いた候補へ移すと今度は落ちる。
                Vector3 footProbe = candidate + Vector3.up * (controller.stepOffset + 0.05f);
                if (!Physics.Raycast(footProbe, Vector3.down, out RaycastHit hit,
                        GroundProbe + controller.stepOffset, mask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Vector3 grounded = new Vector3(candidate.x, hit.point.y + controller.skinWidth, candidate.z);
                position = IsFree(controller, grounded, mask) ? grounded : candidate;
                return true;
            }

            position = origin;
            return false;
        }

        private static bool IsFree(CharacterController controller, Vector3 position, int mask)
        {
            CapsuleEnds(controller, position, -controller.skinWidth, out Vector3 bottom, out Vector3 top, out float radius);
            return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
