using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内の席から立つ (#272)。座面の前に席の当たり判定や机がかぶる席（教育棟・研究実験棟 1 号館・
    /// 食堂・図書館）で、席の脇の E の届く所から座って立つと、立った直後に体がめり込まず、
    /// そのあと押し出されずにその場に立っていられる。前に出た席は座面の一歩前に、
    /// 座る前に立っていた所へ戻った席はその所に立つ。
    /// 席の脇は座面のまわりから探す。床が座面と同じ高さにあり、体が収まり、座面との間に壁が無く、
    /// そこに置いて落ち着いても動かない所を、近い順に使う。
    /// </summary>
    public sealed class InteriorSeatsStandUpPlayTests : SceneTestBase
    {
        /// <summary>建物ごとの代表の席（seat_&lt;id&gt;_&lt;nn&gt; の Empty の名前）。</summary>
        private static readonly (string Building, string[] Seats)[] Cases =
        {
            ("kyoso", new[] { "seat_kyoso_03", "seat_kyoso_17", "seat_kyoso_27", "seat_kyoso_70", "seat_kyoso_83" }),
            ("lab1", new[] { "seat_lab1_01", "seat_lab1_06", "seat_lab1_08" }),
            ("cafeteria", new[] { "seat_cafeteria_01", "seat_cafeteria_17", "seat_cafeteria_20" }),
            ("library", new[] { "seat_library_13" }),
        };

        /// <summary>席の脇を探す、座面からの平面の距離 (m)。</summary>
        private static readonly float[] ApproachDistances = { 0.8f, 1.0f, 1.2f, 1.5f, 1.8f };

        /// <summary>席の脇を探す、座面の前を 0 とした向き（度）。</summary>
        private static readonly float[] ApproachAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };

        /// <summary>席の脇の床が座面の床からずれてよい高さ (m)。</summary>
        private const float FloorTolerance = 0.3f;

        /// <summary>前に出て立ったとき、座面から離れてよい平面の距離 (m)。立つ一歩に、落ち着くまでの動きを足す。</summary>
        private const float MaxStandAhead = PlayerController.DefaultStandForward + SeatChecks.MaxDrift;

        /// <summary>席の脇と座面の間に壁が無いかを見る高さ (m)。机や背もたれより上。</summary>
        private const float SightHeight = 1.2f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator CrowdedSeats_StandUpWithoutSinkingOrBeingPushedOut()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.Paused = true;
            }

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Settle();

            var bad = new List<string>();
            foreach ((string building, string[] seats) in Cases)
            {
                yield return driver.GoTo(building);
                if (InteriorLoader.Instance.CurrentId != building)
                {
                    bad.Add(building + ": 中へ入れない（" + string.Join(" / ", driver.Problems) + "）");
                    continue;
                }

                Transform interior = FindInterior(building);
                if (interior == null)
                {
                    bad.Add(building + ": " + CampusDriver.InteriorPrefix + building + " が無い");
                    continue;
                }

                foreach (string name in seats)
                {
                    yield return Visit(player, interior, name, bad);
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("屋内の席から立つ");
        }

        /// <summary>1 席ぶん。席の脇に置いて落ち着かせる → E と同じ Interact で座る → 立つ。</summary>
        private static IEnumerator Visit(PlayerController player, Transform interior, string name, List<string> bad)
        {
            SeatInteractable seat = SeatChecks.FindSeat(interior, name);
            if (seat == null)
            {
                bad.Add(name + ": 座る操作（Seat_" + name.Substring("seat_".Length) + " の SeatInteractable）が無い");
                yield break;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            int mask = StuckRecovery.SolidMask(player.gameObject);
            bool placed = false;
            foreach (Vector3 spot in ApproachSpots(seat, controller, mask))
            {
                player.Teleport(spot, CampusDriver.Yaw(seat.transform.position - spot));
                yield return LibraryInterior.Settled();
                Vector3 drift = player.transform.position - spot;
                drift.y = 0f;
                if (drift.magnitude <= SeatChecks.MaxDrift && !StuckRecovery.IsPenetrating(controller, mask))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                bad.Add(name + ": 席の脇に E の届く空いた床が無い（座面 " + seat.transform.position.ToString("F2") + "）");
                yield break;
            }

            Vector3 before = player.transform.position;
            Transform anchor = SeatChecks.NearestAnchor(seat, before);
            seat.Interact(player.gameObject);
            if (!player.IsSitting)
            {
                bad.Add(name + ": 座れない（席の脇 " + player.transform.position.ToString("F2") + "）");
                yield break;
            }

            yield return PlayModeScenes.Advance(5, 0.3f);
            yield return SeatChecks.StandUp(player, anchor, MaxStandAhead, name, bad, before);
        }

        /// <summary>座面のまわりで、体が収まり E の届く床。近い順。</summary>
        private static IEnumerable<Vector3> ApproachSpots(SeatInteractable seat, CharacterController controller, int mask)
        {
            foreach (Transform anchor in seat.Anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                Vector3 forward = anchor.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
                foreach (float distance in ApproachDistances)
                {
                    foreach (float angle in ApproachAngles)
                    {
                        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * forward * distance;
                        if (TryFloor(anchor.position + offset, anchor.position.y, mask, out Vector3 spot)
                            && Reaches(seat, spot)
                            && StuckRecovery.HasRoom(controller, spot, mask)
                            && !Physics.Linecast(anchor.position + Vector3.up * SightHeight, spot + Vector3.up * SightHeight,
                                mask, QueryTriggerInteraction.Ignore))
                        {
                            yield return spot;
                        }
                    }
                }
            }
        }

        /// <summary>at の真下の床。座面の床と同じ高さに無ければ false（机の上や段の下は使わない）。</summary>
        private static bool TryFloor(Vector3 at, float floorY, int mask, out Vector3 spot)
        {
            spot = at;
            Vector3 origin = new Vector3(at.x, floorY + 1f, at.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1f + FloorTolerance + 0.3f, mask,
                    QueryTriggerInteraction.Ignore)
                || Mathf.Abs(hit.point.y - floorY) > FloorTolerance)
            {
                return false;
            }

            spot = new Vector3(at.x, hit.point.y, at.z);
            return true;
        }

        private static bool Reaches(SeatInteractable seat, Vector3 spot)
        {
            Vector3 flat = seat.transform.position - spot;
            flat.y = 0f;
            return flat.magnitude <= seat.InteractionRange;
        }

        private static Transform FindInterior(string building)
        {
            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + building);
            return root != null ? root.transform : null;
        }
    }
}
