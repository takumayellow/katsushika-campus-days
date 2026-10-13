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

        /// <summary>座る前の位置へ戻ったとみなす、立った位置との平面の差 (m)。Teleport はその位置へそのまま置く。</summary>
        private const float ReturnTolerance = 0.01f;

        /// <summary>
        /// 立って、その場に落ち着くまで待つ。立った位置で体がめり込んでいると、RecoverIfStuck が
        /// 半秒ほどあとに別の場所へ押し出すので、立った直後の重なりと、そのあと動いた距離の両方を見る。
        /// before（座る前に立っていた所）を渡すと、そこへ戻ったかを見分け、戻ったならそこから、
        /// 前に出たなら座面から maxOffset 以内に立っているかを見る。
        /// </summary>
        internal static IEnumerator StandUp(PlayerController player, Transform anchor, float maxOffset, string label,
            List<string> bad, Vector3? before = null)
        {
            player.StandUp();
            if (player.IsSitting)
            {
                bad.Add(label + ": 立てない");
                yield break;
            }

            Vector3 stood = player.transform.position;
            bool returned = before.HasValue && Flat(stood - before.Value) <= ReturnTolerance;
            Debug.Log("[SeatChecks] " + label + ": " + (returned ? "座る前に立っていた所へ戻った" : "座面の前に出た"));

            // StandPosition は HasRoom と同じカプセルとマスクで立つ位置を選ぶので、ここで落ちるのは
            // 前にも座る前の所にも収まらない席だけ。本当の判定は、重力で落ち着いたあとの押し出しの検査。
            Collider inside = SolidOverlap(player.GetComponent<CharacterController>());
            if (inside != null)
            {
                bad.Add(label + ": 立った体が " + Describe(inside) + " にめり込む（立った位置 " + stood.ToString("F2") + "）");
            }

            yield return PlayModeScenes.Advance(StuckFrames, 0.5f);
            float moved = Flat(player.transform.position - stood);
            if (moved > MaxDrift)
            {
                bad.Add(label + ": 立ったあと " + moved.ToString("F2") + " m 押し出される（立った位置 "
                    + stood.ToString("F2") + "、いま " + player.transform.position.ToString("F2") + "）");
            }

            CheckStanding(player, anchor, returned ? before.Value : (Vector3?)null, maxOffset, label, bad);
        }

        /// <summary>落ち着いたあとの立ち姿。returnedTo があればそこから MaxDrift 以内、無ければ座面から maxOffset 以内。</summary>
        private static void CheckStanding(PlayerController player, Transform anchor, Vector3? returnedTo, float maxOffset,
            string label, List<string> bad)
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

            if (returnedTo.HasValue)
            {
                float away = Flat(position - returnedTo.Value);
                if (away > MaxDrift)
                {
                    bad.Add(label + ": 座る前に立っていた所へ戻ったあと " + away.ToString("F2") + " m ずれる");
                }
            }
            else
            {
                float away = Flat(position - anchor.position);
                if (away > maxOffset)
                {
                    bad.Add(label + ": 前に出て立つと座面から " + away.ToString("F2") + " m 離れる（上限 "
                        + maxOffset.ToString("F2") + " m）");
                }
            }

            // 重力で落ち着いたあとの重なり。立った直後の検査と違い、立つ位置の選び方に依らず実際のめり込みを見る。
            Collider inside = SolidOverlap(controller);
            if (inside != null)
            {
                bad.Add(label + ": 落ち着いたあとも体が " + Describe(inside) + " にめり込む");
            }
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
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
