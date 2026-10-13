using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 図書館大ホール (#110): 1 階席の 1 列目の通路の端の席（seat_library_01・02）、西の側方席（seat_library_03）、
    /// 舞台のピアノ椅子に、客席のワープ先（poi_library_hall_seats・poi_library_balcony）から通路を歩いて近づいて
    /// E の届く所へ入れ、E で座れ、立った直後に体がめり込まず、そのあと押し出されずにその場に立っていられる。
    /// 道の地点は Blender の屋内座標（x, y, 床からの高さ）で書く。
    /// </summary>
    public sealed class LibraryHallSeatsPlayTests : SceneTestBase
    {
        /// <summary>座った位置が座面からずれてよい距離 (m)。SitAt は座面へそのまま置く。</summary>
        private const float SitTolerance = 0.05f;

        /// <summary>立った足もとが席の床からずれてよい高さ (m)。客席の段の差より小さい。</summary>
        private const float StandHeightTolerance = 0.3f;

        /// <summary>立った位置が座面から離れてよい平面の距離 (m)。立つ一歩（ピアノは 0.8 m 後ろ）のぶん。</summary>
        private const float MaxStandOffset = 1.0f;

        /// <summary>立ったあと落ち着くまでに動いてよい平面の距離 (m)。めり込みの押し出し（RecoverIfStuck）はこれを超える。</summary>
        private const float MaxDrift = 0.1f;

        /// <summary>立ったあと待つフレーム数。RecoverIfStuck が最後の手段で動かすまでのフレーム数より長く待つ。</summary>
        private const int StuckFrames = PlayerController.LastResortFrames + 10;

        /// <summary>ピアノ椅子の手前で止まる距離 (m)。椅子は固い当たり判定なので、その手前を終点にする。</summary>
        private const float StoolStop = 0.9f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator HallSeats_WalkUpSitAndStandUp()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            InteractionPrompt prompt = player.GetComponentInChildren<InteractionPrompt>();
            Assert.IsNotNull(prompt, "プレイヤーに InteractionPrompt が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Settle();
            yield return driver.Enter("library");
            Assert.AreEqual("library", InteriorLoader.Instance.CurrentId, "図書館へ入れない");

            Transform interior = LibraryInterior.Find();
            Assert.IsNotNull(interior, "Interior_library が無い");
            LibraryInterior.Point local = LibraryInterior.BuildMapping(interior, out string mapError);
            Assert.IsNull(mapError, mapError);
            Transform stool = LibraryInterior.FindPianoStool(interior);
            Assert.IsNotNull(stool, "舞台のピアノ椅子（seat_library_<nn> で，seat_library_<nn>_piano の Empty を持つもの）が無い");

            Transform hallSeats = FindChild(interior, "poi_library_hall_seats");
            Assert.IsNotNull(hallSeats, "poi_library_hall_seats（客席のワープ先）が無い");
            Vector3 crossAisle = hallSeats.position; // 横通路の中央
            Transform balcony = FindChild(interior, "poi_library_balcony");
            Assert.IsNotNull(balcony, "poi_library_balcony（2 階席のワープ先）が無い");
            Vector3 westAisle = local(-5.5f, 45.3f, 11.3f);
            Vector3 stageApproach = local(-11.1f, 54.6f, 10.0f); // 下手の小階段の下
            Vector3 stoolAt = stool.position;
            Vector3 toward = stageApproach - stoolAt;
            toward.y = 0f;
            stoolAt += toward.normalized * StoolStop;
            stoolAt.y = local(0f, 0f, 10.75f).y;

            var bad = new List<string>();
            // 1 階席: ワープ先の横通路から縦通路を下り、1 列目の端の席の脇に立つ。
            yield return Visit(player, prompt, interior, "seat_library_01", false, crossAisle,
                new[] { westAisle, local(-5.5f, 53.2f, 10.0f) }, bad);
            yield return Visit(player, prompt, interior, "seat_library_02", false, crossAisle,
                new[] { local(2.25f, 45.3f, 11.3f), local(2.25f, 53.2f, 10.0f) }, bad);
            // 2 階席: ワープ先の後ろの通路から西の縦通路を B1 まで下り、B1 の西の空きを抜けて側方席の前の通路へ。
            yield return Visit(player, prompt, interior, "seat_library_03", false, balcony.position, new[]
            {
                local(-12.475f, 30.5f, 17.2f), local(-12.475f, 33.9f, 16.0f), local(-16.6f, 33.9f, 16.0f),
                local(-16.6f, 35.3f, 16.0f),
            }, bad);
            // 舞台: ワープ先から西の縦通路を舞台前まで下り、下手の小階段から椅子の手前へ。
            yield return Visit(player, prompt, interior, stool.name, true, crossAisle, new[]
            {
                westAisle, local(-5.5f, 54.6f, 10.0f), stageApproach, stoolAt,
            }, bad);

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("図書館のホールの席");
        }

        /// <summary>
        /// 1 席ぶん。start に置いて path を歩く → 席の方を向いて E の対象になる → E で座る → 立つ。
        /// </summary>
        private static IEnumerator Visit(PlayerController player, InteractionPrompt prompt, Transform interior, string name,
            bool piano, Vector3 start, Vector3[] path, List<string> bad)
        {
            SeatInteractable seat = FindSeat(interior, name);
            if (seat == null)
            {
                bad.Add(name + ": 座る操作（Seat_" + name.Substring("seat_".Length) + " の SeatInteractable）が無い");
                yield break;
            }

            if (piano && seat.Performance == null)
            {
                bad.Add(name + ": ピアノ椅子なのに演奏が繋がっていない");
            }

            player.Teleport(start, 0f);
            yield return LibraryInterior.Settled();
            int before = bad.Count;
            yield return LibraryInterior.WalkAll(player, bad, name + " への道", path);
            if (bad.Count > before)
            {
                yield break;
            }

            Vector3 toSeat = seat.transform.position - player.transform.position;
            toSeat.y = 0f;
            player.Teleport(player.transform.position, Mathf.Atan2(toSeat.x, toSeat.z) * Mathf.Rad2Deg);
            yield return PlayModeScenes.Advance(5, 0.3f);
            toSeat = seat.transform.position - player.transform.position;
            toSeat.y = 0f;
            if (toSeat.magnitude > seat.InteractionRange)
            {
                bad.Add(name + ": 通路から席まで " + toSeat.magnitude.ToString("F2") + " m（E が届くのは "
                    + seat.InteractionRange.ToString("F1") + " m まで）");
            }

            if (prompt.Current != seat)
            {
                bad.Add(name + ": 通路で席の方を向いても E の対象にならない（いま "
                    + (prompt.Current != null ? prompt.Current.name : "なし") + "、席まで "
                    + toSeat.magnitude.ToString("F2") + " m）");
                yield break;
            }

            Transform anchor = NearestAnchor(seat, player.transform.position);
            if (anchor == null)
            {
                bad.Add(name + ": 座面（Anchor）が無い");
                yield break;
            }

            prompt.Current.Interact(player.gameObject);
            if (!player.IsSitting)
            {
                bad.Add(name + ": E で座れない");
                yield break;
            }

            float offset = (player.transform.position - anchor.position).magnitude;
            if (offset > SitTolerance)
            {
                bad.Add(name + ": 座面から " + offset.ToString("F2") + " m ずれて座る");
            }

            if (!KCDInput.MovementLocked)
            {
                bad.Add(name + ": 座っても移動が止まらない");
            }

            yield return PlayModeScenes.Advance(5, 0.3f);
            if (!player.IsSitting)
            {
                bad.Add(name + ": 座ったあと勝手に立つ");
                yield break;
            }

            yield return StandUp(player, anchor, name, bad);
        }

        /// <summary>
        /// 立って、その場に落ち着くまで待つ。立った位置で体がめり込んでいると、RecoverIfStuck が
        /// 半秒ほどあとに別の場所へ押し出すので、立った直後の重なりと、そのあと動いた距離の両方を見る。
        /// </summary>
        private static IEnumerator StandUp(PlayerController player, Transform anchor, string label, List<string> bad)
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

            CheckStanding(player, anchor, label, bad);
        }

        private static void CheckStanding(PlayerController player, Transform anchor, string label, List<string> bad)
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
            if (flat.magnitude > MaxStandOffset)
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

        /// <summary>体の当たり判定（スキンの厚みぶん細くする）と重なる、プレイヤー以外の固い当たり判定。</summary>
        private static Collider SolidOverlap(CharacterController controller)
        {
            Transform body = controller.transform;
            float radius = controller.radius - controller.skinWidth;
            float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
            Vector3 centre = body.TransformPoint(controller.center);
            Collider[] hits = Physics.OverlapCapsule(centre + Vector3.down * half, centre + Vector3.up * half, radius,
                ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (hit != controller && !hit.transform.IsChildOf(body))
                {
                    return hit;
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>座る操作が選ぶのと同じ、いちばん近い座面。</summary>
        private static Transform NearestAnchor(SeatInteractable seat, Vector3 from)
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

        /// <summary>SeatFactory が seat_library_&lt;nn&gt; の Empty から作る Seat_library_&lt;nn&gt; の座る操作。</summary>
        private static SeatInteractable FindSeat(Transform interior, string emptyName)
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
    }
}
