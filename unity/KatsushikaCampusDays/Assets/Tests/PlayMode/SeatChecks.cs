using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内の席の PlayMode テストが共有する道具。SeatFactory が作る座る操作を探し、座ったあと立って、
    /// 体がめり込まず押し出されずにその場に立っていられるかを見る。
    /// </summary>
    internal static class SeatChecks
    {
        /// <summary>立った足もとが席の床からずれてよい高さ (m)。客席の段の差より小さい。</summary>
        private const float StandHeightTolerance = 0.3f;

        /// <summary>立ったあと落ち着くまでに動いてよい平面の距離 (m)。めり込みの押し出し（RecoverIfStuck）はこれを超える。</summary>
        internal const float MaxDrift = 0.1f;

        /// <summary>立ったあと待つフレーム数。RecoverIfStuck が最後の手段で動かすまでのフレーム数より長く待つ。</summary>
        private const int StuckFrames = PlayerController.LastResortFrames + 10;

        /// <summary>SeatFactory が seat_&lt;id&gt;_&lt;nn&gt; の Empty から作る Seat_&lt;id&gt;_&lt;nn&gt; の座る操作。</summary>
        internal static SeatInteractable FindSeat(Transform interior, string emptyName)
        {
            string seatName = "Seat_" + emptyName.Substring("seat_".Length);
            foreach (SeatInteractable seat in interior.GetComponentsInChildren<SeatInteractable>(true))
            {
                if (seat.transform.parent != null && seat.transform.parent.name == seatName)
                {
                    return seat;
                }
            }

            return null;
        }

        /// <summary>
        /// 立って、その場に落ち着くまで待つ。立った位置で体がめり込んでいると、RecoverIfStuck が
        /// 半秒ほどあとに別の場所へ押し出すので、立った直後の重なりと、そのあと動いた距離の両方を見る。
        /// </summary>
        internal static IEnumerator StandUp(PlayerController player, Transform anchor, float maxOffset, string label,
            List<string> bad)
        {
            player.StandUp();
            if (player.IsSitting)
            {
                bad.Add(label + ": 立てない");
                yield break;
            }

            Vector3 stood = player.transform.position;
            Collider inside = SolidOverlap(player.GetComponent<CharacterController>());
            if (inside != null)
            {
                bad.Add(label + ": 立った体が " + Describe(inside) + " にめり込む（立った位置 " + stood.ToString("F2") + "）");
            }

            yield return PlayModeScenes.Advance(StuckFrames, 0.5f);
            Vector3 moved = player.transform.position - stood;
            moved.y = 0f;
            if (moved.magnitude > MaxDrift)
            {
                bad.Add(label + ": 立ったあと " + moved.magnitude.ToString("F2") + " m 押し出される（立った位置 "
                    + stood.ToString("F2") + "、いま " + player.transform.position.ToString("F2") + "）");
            }

            CheckStanding(player, anchor, maxOffset, label, bad);
        }

        private static void CheckStanding(PlayerController player, Transform anchor, float maxOffset, string label,
            List<string> bad)
        {
            if (KCDInput.MovementLocked)
            {
                bad.Add(label + ": 立ったのに移動の止めが残っている");
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (!controller.enabled)
            {
                bad.Add(label + ": 立ったのに当たり判定が切れたまま");
            }

            Vector3 position = player.transform.position;
            float drop = position.y - anchor.position.y;
            if (Mathf.Abs(drop) > StandHeightTolerance)
            {
                bad.Add(label + ": 立った足もとが席の床から " + drop.ToString("F2") + " m（いま " + position.ToString("F2") + "）");
            }

            Vector3 flat = position - anchor.position;
            flat.y = 0f;
            if (flat.magnitude > maxOffset)
            {
                bad.Add(label + ": 立つと座面から " + flat.magnitude.ToString("F2") + " m 離れる");
            }

            Collider inside = SolidOverlap(controller);
            if (inside != null)
            {
                bad.Add(label + ": 落ち着いたあとも体が " + Describe(inside) + " にめり込む");
            }
        }

        private static string Describe(Collider collider)
        {
            Transform owner = collider.transform.parent;
            return collider.name + "（" + (owner != null ? owner.name : "-") + "）";
        }

        /// <summary>体の当たり判定（StuckRecovery.HasRoom と同じくスキンの厚みぶん細くする）と重なる固い当たり判定。RecoverIfStuck と同じく、プレイヤーと NPC は除く。</summary>
        internal static Collider SolidOverlap(CharacterController controller)
        {
            Transform body = controller.transform;
            StuckRecovery.CapsuleEnds(controller, body.position, controller.skinWidth,
                out Vector3 bottom, out Vector3 top, out float radius);
            Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, StuckRecovery.SolidMask(body.gameObject),
                QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (!hit.transform.IsChildOf(body))
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>座る操作が選ぶのと同じ、いちばん近い座面。</summary>
        internal static Transform NearestAnchor(SeatInteractable seat, Vector3 from)
        {
            Transform best = null;
            foreach (Transform anchor in seat.Anchors)
            {
                if (anchor != null && (best == null
                    || (anchor.position - from).sqrMagnitude < (best.position - from).sqrMagnitude))
                {
                    best = anchor;
                }
            }

            return best;
        }
    }
}
