using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 物にぶつかっても抜け出せること（#30）と、キャンパスの端から出られず、落ちても戻ること（#40）。
    /// プレイヤーを自動で走らせて木の幹や家具に体当たりさせ、通った道を逆にたどって出発点まで戻れるかを見る。
    /// </summary>
    public sealed class ObstacleEscapePlayTests : SceneTestBase
    {
        /// <summary>体当たりさせる木の本数。キャンパス全体から位置の順に間引いて選ぶ。</summary>
        private const int TreeSamples = 16;

        /// <summary>幹の中心から出発点・目標までの距離（m）。</summary>
        private const float TreeRunUp = 2.5f;

        /// <summary>共創棟のロビーで走らせる向きの数と距離（m）。</summary>
        private const int LobbyDirections = 12;

        // ロビーの広さより遠くを目指して、どの向きでも家具か壁に当たるまで走らせる。
        private const float LobbyReach = 25f;

        /// <summary>
        /// 体当たりで目標まで走りきるのにかかる時間に足す余裕（実時間の秒）。当たって止まったまま押し続ける時間。
        /// 端の見えない壁へは、この倍を目安に走らせる。
        /// </summary>
        private const float ChargeSeconds = 3f;

        /// <summary>来た道を戻るとき、1 区間に待つ時間の上限（実時間の秒）。</summary>
        private const float LegSeconds = 1.5f;

        /// <summary>通った道を覚える間隔（m）。</summary>
        private const float TrailStep = 0.3f;

        /// <summary>
        /// 体当たりまでに走れたとみなす距離（m、水平）。半分以上の体当たりでこれだけ進んでいなければ、
        /// 自動で走らせること自体ができていない（その場で止まったままでも「戻れた」ことになってしまう）。
        /// </summary>
        private const float MinCharge = 1f;

        /// <summary>目標のこれだけ手前で止まっていれば、何かに当たったとみなす（m、水平）。</summary>
        private const float BlockedShortfall = 0.5f;

        /// <summary>出発点に戻れたとみなす距離（m、水平）。</summary>
        private const float BackTolerance = 0.6f;

        /// <summary>端の手前のどこから走らせるか（m、範囲の縁から内側）と、縁の外のどこを目指すか。</summary>
        private const float EdgeStart = 6f;

        private const float EdgeOvershoot = 20f;

        /// <summary>出口を避けるとき、出口の範囲をこれだけ広げて見る（m）。プレイヤーの太さと、押し戻されてずれる分。</summary>
        private const float ExitMargin = 0.5f;

        /// <summary>落ちてから戻るまで待つ上限（実時間の秒）。暗転と明転を含む。</summary>
        private const float RecoverSeconds = 8f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_TreeTrunks_DoNotTrapThePlayer()
        {
            CollectErrorsInsteadOfLogAssert();
            PlayerController player = null;
            yield return LoadCampus(p => player = p);

            CapsuleCollider[] trunks = Object.FindObjectsByType<CapsuleCollider>()
                .Where(c => c.name == CameraObstacleFilter.TreeTrunkName && c.enabled && !c.isTrigger)
                .OrderBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z)
                .ToArray();
            Assert.That(trunks.Length, Is.GreaterThanOrEqualTo(TreeSamples), "木の幹の当たり判定が少なすぎる");

            var bad = new List<string>();
            var charged = new List<Vector2>();
            int tried = 0;
            int step = trunks.Length / TreeSamples;
            for (int i = 0; i < trunks.Length && tried < TreeSamples; i += step)
            {
                Vector3 trunk = trunks[i].transform.position;
                if (!TryApproach(player, trunk, out Vector3 start, out Vector3 through))
                {
                    continue;
                }

                tried++;
                yield return ChargeAndReturn(player, "木 " + trunk.ToString("F1"), start, through, bad, charged);
            }

            Assert.That(tried, Is.GreaterThanOrEqualTo(TreeSamples / 2), "体当たりできる木が少なすぎる（周りがふさがっている）");
            AssertCharged(charged);
            Assert.IsEmpty(bad, "木の幹で抜け出せなかった:\n" + string.Join("\n", bad));
            AssertNoErrors("木への体当たり");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator KyosoLobby_Furniture_DoesNotTrapThePlayer()
        {
            CollectErrorsInsteadOfLogAssert();
            PlayerController player = null;
            yield return LoadCampus(p => player = p);

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Enter("kyoso");
            Assert.IsEmpty(driver.Problems, string.Join("\n", driver.Problems));
            Assert.AreEqual("kyoso", InteriorLoader.Instance.CurrentId, "共創棟の中へ入れていない");

            // 入ってすぐの所から、ロビーの植木鉢・ベンチ・壁へ向けて四方に走らせる。
            // 入った所は出口の範囲の中にあり、出口を踏み直すと外へ出てしまう（#212）。
            // 出口から離れた所まで進んでから走らせ、出口を通る向きは飛ばす。
            Bounds[] exits = Object.FindObjectsByType<InteriorExit>()
                .Where(e => e.BuildingId == "kyoso")
                .Select(e => e.GetComponent<Collider>().bounds)
                .ToArray();
            Assert.IsNotEmpty(exits, "共創棟の出口が見つからない");
            Vector3 spawn = ClearOf(player, exits);
            var bad = new List<string>();
            var charged = new List<Vector2>();
            int tried = 0;
            for (int i = 0; i < LobbyDirections; i++)
            {
                float angle = i * 360f / LobbyDirections;
                Vector3 target = spawn + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * LobbyReach;
                if (CrossesAny(exits, spawn, target))
                {
                    continue;
                }

                tried++;
                yield return ChargeAndReturn(player, "ロビー " + angle + " 度", spawn, target, bad, charged);
            }

            Assert.That(tried, Is.GreaterThanOrEqualTo(LobbyDirections / 2),
                "出口を避けて走らせられる向きが少なすぎる（出発点 " + spawn.ToString("F2") + "、出口 " +
                string.Join(" / ", exits.Select(e => e.ToString())) + "）");
            AssertCharged(charged);
            Assert.AreEqual("kyoso", InteriorLoader.Instance.CurrentId, "走らせているうちに共創棟の外へ出た");
            Assert.IsEmpty(bad, "共創棟のロビーで抜け出せなかった:\n" + string.Join("\n", bad));
            AssertNoErrors("共創棟のロビー");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_Edges_StopThePlayer_AndAFallReturnsToCampus()
        {
            CollectErrorsInsteadOfLogAssert();
            PlayerController player = null;
            yield return LoadCampus(p => player = p);
            WorldBounds bounds = Object.FindAnyObjectByType<WorldBounds>();
            Assert.IsNotNull(bounds, "キャンパスに WorldBounds が無い");

            var bad = new List<string>();
            // 各辺の真ん中（もう一方の座標が 0）を走らせる。西の壁の門と増築区画（z 156〜262）は避けている。
            Assert.That(0f, Is.Not.InRange(DormRoute.DefaultAnnexZMin, DormRoute.DefaultAnnexZMax),
                "西の端を走らせる線が増築区画にかかっている");
            Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (Vector3 side in sides)
            {
                Assert.IsTrue(TryFloor(player, side * (WorldBounds.HalfExtent - EdgeStart), out Vector3 start),
                    side + " の端の手前に床が無い");
                player.Teleport(start, CampusDriver.Yaw(side));
                yield return PlayModeScenes.Advance(10, 0.3f);

                yield return Run(player, side * (WorldBounds.HalfExtent + EdgeOvershoot), ChargeSeconds * 2f, null);
                Vector3 stopped = player.transform.position;
                float reach = Vector3.Dot(stopped, side);
                if (reach > WorldBounds.HalfExtent || !WorldBounds.IsWithin(stopped, bounds.Area))
                {
                    bad.Add(side + " の端: 範囲の外まで出た（" + stopped.ToString("F1") + "）");
                }
                else if (reach < WorldBounds.HalfExtent - 2f)
                {
                    bad.Add(side + " の端: 縁の 2 m 以上手前で止まった（" + stopped.ToString("F1") + "）");
                }
            }

            // 最後に立っていた端から地面の下へ落とすと、暗転して安全な所へ戻る。
            // 安全な位置は封鎖が無く接地している間だけ覚え直すので、そうなってから待つ。
            float settle = Time.realtimeSinceStartup + 2f;
            while ((KCDInput.GameplayBlocked || !player.IsGrounded) && Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            Assert.IsFalse(KCDInput.GameplayBlocked, "端を走らせたあと操作が封鎖されたまま");
            yield return new WaitForSecondsRealtime(WorldBounds.SampleInterval * 2f);
            Vector3 before = player.transform.position;
            player.Teleport(new Vector3(before.x, bounds.KillY - 10f, before.z), player.transform.eulerAngles.y);
            float deadline = Time.realtimeSinceStartup + RecoverSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   (player.transform.position.y < WorldBounds.SafeMinY || bounds.IsRecovering))
            {
                yield return null;
            }

            Vector3 back = player.transform.position;
            if (bounds.IsRecovering || back.y < WorldBounds.SafeMinY || !WorldBounds.IsWithin(back, bounds.Area))
            {
                bad.Add("落ちたあと " + RecoverSeconds + " 秒たっても戻らない（いま " + back.ToString("F1") + "）");
            }
            else if (Flat(back - before).magnitude > 5f)
            {
                bad.Add("落ちる前の " + before.ToString("F1") + " から離れた所 " + back.ToString("F1") + " へ戻った");
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("キャンパスの端");
        }

        private IEnumerator LoadCampus(System.Action<PlayerController> found)
        {
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

            found(player);
        }

        /// <summary>
        /// 幹を挟んで向かい合う出発点と目標を選ぶ。出発点は幹と同じ高さの床があり、ほかの物と重ならない所。
        /// 8 方向を試し、どれも駄目なら false。
        /// </summary>
        private static bool TryApproach(PlayerController player, Vector3 trunk, out Vector3 start, out Vector3 through)
        {
            for (int k = 0; k < 8; k++)
            {
                Vector3 direction = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                through = trunk - direction * TreeRunUp;
                if (TryFloor(player, trunk + direction * TreeRunUp, out start) &&
                    Mathf.Abs(start.y - trunk.y) < 0.6f && IsFree(player, start))
                {
                    return true;
                }
            }

            start = through = Vector3.zero;
            return false;
        }

        /// <summary>
        /// start から target へ走らせて体当たりさせ、通った道を逆にたどって start まで戻れるかを見る。
        /// 体当たりまでに走った距離と、目標までの距離を charged に足す。
        /// </summary>
        private static IEnumerator ChargeAndReturn(PlayerController player, string label, Vector3 start, Vector3 target,
            List<string> bad, List<Vector2> charged)
        {
            player.Teleport(start, CampusDriver.Yaw(target - start));
            yield return PlayModeScenes.Advance(5, 0.1f);
            Vector3 origin = player.transform.position;

            var trail = new List<Vector3> { origin };
            float planned = Flat(target - origin).magnitude;
            yield return Run(player, target, planned / player.RunSpeed + ChargeSeconds, trail);
            Vector3 hit = player.transform.position;
            charged.Add(new Vector2(Flat(hit - origin).magnitude, planned));

            CharacterController controller = player.GetComponent<CharacterController>();
            int mask = StuckRecovery.SolidMask(player.gameObject);
            if (StuckRecovery.IsPenetrating(controller, mask))
            {
                bad.Add(label + ": 体当たりのあと物にめり込んだまま（" + hit.ToString("F2") + "）");
            }

            for (int i = trail.Count - 1; i >= 0; i--)
            {
                yield return Run(player, trail[i], LegSeconds, null);
            }

            Vector3 end = player.transform.position;
            float gap = Flat(end - origin).magnitude;
            if (gap > BackTolerance)
            {
                bad.Add(string.Format("{0}: {1} で止まった所から戻れない（出発点まで {2:F2} m、いま {3}）",
                    label, hit.ToString("F2"), gap, end.ToString("F2")));
            }
        }

        /// <summary>入ってきた向きへ、出口の範囲（ExitMargin だけ広げる）から出るまで進んだ床の点。</summary>
        private static Vector3 ClearOf(PlayerController player, Bounds[] exits)
        {
            Vector3 from = player.transform.position;
            Vector3 forward = Flat(player.transform.forward).normalized;
            for (float d = 0f; d < 4f; d += 0.25f)
            {
                Vector3 point = from + forward * d;
                if (!CrossesAny(exits, point, point) && TryFloor(player, point, out Vector3 floor) && IsFree(player, floor))
                {
                    return floor;
                }
            }

            Assert.Fail("出口の前に、出口から離れて立てる所が無い（入った所 " + from.ToString("F2") + "）");
            return from;
        }

        /// <summary>
        /// start から target へ走る道が、どれかの範囲（ExitMargin だけ広げる）を通るか。水平だけで見る。
        /// </summary>
        private static bool CrossesAny(Bounds[] areas, Vector3 start, Vector3 target)
        {
            const float step = 0.25f;
            Vector3 path = Flat(target - start);
            float length = path.magnitude;
            for (float d = 0f; d <= length; d += step)
            {
                Vector3 point = length > 0f ? start + path * (d / length) : start;
                foreach (Bounds area in areas)
                {
                    Vector3 gap = Flat(point - area.ClosestPoint(new Vector3(point.x, area.center.y, point.z)));
                    if (gap.magnitude <= ExitMargin)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>target へ自動で走らせ、着くか seconds たつまで待つ。trail があれば通った所を TrailStep ごとに足す。</summary>
        private static IEnumerator Run(PlayerController player, Vector3 target, float seconds, List<Vector3> trail)
        {
            player.SetAutopilot(target, player.RunSpeed);
            float deadline = Time.realtimeSinceStartup + seconds;
            while (player.AutopilotActive && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (trail != null && Flat(player.transform.position - trail[trail.Count - 1]).magnitude >= TrailStep)
                {
                    trail.Add(player.transform.position);
                }
            }

            player.ClearAutopilot();
        }

        /// <summary>
        /// 半分以上の体当たりで、MinCharge 以上走ってから目標の手前で止まっていること
        /// （走れていない・何にも当たっていない体当たりばかりでは、抜け出せるかを確かめたことにならない）。
        /// </summary>
        private static void AssertCharged(List<Vector2> charged)
        {
            string distances = "走った距離 / 目標まで m: " +
                               string.Join(", ", charged.Select(d => d.x.ToString("F2") + "/" + d.y.ToString("F1")));
            int ran = charged.Count(d => d.x >= MinCharge);
            Assert.That(ran * 2, Is.GreaterThanOrEqualTo(charged.Count), "体当たりまでにほとんど走れていない（" + distances + "）");
            int blocked = charged.Count(d => d.x <= d.y - BlockedShortfall);
            Assert.That(blocked * 2, Is.GreaterThanOrEqualTo(charged.Count), "走らせても物にほとんど当たっていない（" + distances + "）");
        }

        /// <summary>点の真下（point の 3 m 上から 6 m）の床に 0.05 m 浮かせて立つ位置。床が無ければ false。</summary>
        private static bool TryFloor(PlayerController player, Vector3 point, out Vector3 floor)
        {
            int mask = StuckRecovery.SolidMask(player.gameObject);
            Vector3 from = new Vector3(point.x, point.y + 3f, point.z);
            bool found = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore);
            floor = found ? hit.point + Vector3.up * 0.05f : point;
            return found;
        }

        /// <summary>足もと feet にプレイヤーのカプセルを置いて、ほかの当たり判定と重ならないか。</summary>
        private static bool IsFree(PlayerController player, Vector3 feet)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            float radius = controller.radius;
            Vector3 bottom = feet + Vector3.up * (radius + 0.05f);
            Vector3 top = feet + Vector3.up * Mathf.Max(radius + 0.05f, controller.height - radius);
            return !Physics.CheckCapsule(bottom, top, radius, StuckRecovery.SolidMask(player.gameObject),
                QueryTriggerInteraction.Ignore);
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}
