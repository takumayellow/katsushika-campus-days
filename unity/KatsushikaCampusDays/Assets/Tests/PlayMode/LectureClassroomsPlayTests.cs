using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟 2F・3F の教室 14 室 (#279)。廊下からどの扉でも教室に入り、室の中の通路を次の扉まで歩いて、
    /// また廊下へ出られる。屋内には NavMesh を焼いていないので、図書館のテストと同じく、
    /// プレイヤーを目的地へ向けて当たり判定ごと歩かせる（LibraryInterior.WalkAll）。
    /// 地点は Blender の屋内座標（x, y, 床の高さ）で書き、屋内の Empty との対応から Unity の座標へ移す。
    /// 室と扉の表は blender/kcd_interior/plan_lecture_rooms_dims.py の ROOMS と同じ。
    /// </summary>
    public sealed class LectureClassroomsPlayTests : SceneTestBase
    {
        private const string BuildingId = "lecture";
        private const float FloorTolerance = 0.45f;
        private const float Floor2 = 4.6f;
        private const float Floor3 = 8.8f;

        /// <summary>室番号、床の高さ、南の室か、扉の中心 X（西から順）。</summary>
        private static readonly (string No, float Z, bool South, float[] Doors)[] Rooms =
        {
            ("203", Floor2, true, new[] { -26.5f, -12.4f }),
            ("202", Floor2, true, new[] { -9.9f, 5.9f }),
            ("201", Floor2, true, new[] { 8.1f, 27.3f }),
            ("305", Floor3, true, new[] { -25.9f, -18.9f }),
            ("304", Floor3, true, new[] { -16.8f, -4.5f }),
            ("303", Floor3, true, new[] { -2.5f, 6.3f }),
            ("302", Floor3, true, new[] { 8.3f, 20.7f }),
            ("301", Floor3, true, new[] { 27.3f }),
            ("306", Floor3, false, new[] { -25.9f, -18.9f }),
            ("307", Floor3, false, new[] { -16.8f, -4.5f }),
            ("308", Floor3, false, new[] { -2.5f, 6.3f }),
            ("309", Floor3, false, new[] { 8.3f, 13.3f }),
            ("310", Floor3, false, new[] { 15.3f, 20.3f }),
            ("311", Floor3, false, new[] { 22.5f, 32.7f }),
        };

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator EveryClassroomOn2FAnd3FCanBeEnteredThroughEachDoor()
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
            yield return driver.Enter(BuildingId);
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "講義棟へ入れない");

            GameObject root = GameObject.Find(CampusDriver.InteriorPrefix + BuildingId);
            Assert.IsNotNull(root, "Interior_lecture が無い");
            LibraryInterior.Point local = BuildMapping(root.transform, out string mapError);
            Assert.IsNull(mapError, mapError);

            var bad = new List<string>();
            foreach ((string no, float z, bool south, float[] doors) in Rooms)
            {
                yield return WalkThroughRoom(player, local, bad, no, z, south, doors);
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("講義棟 2F・3F の教室");
        }

        /// <summary>
        /// 最初の扉の前の廊下に立ち、扉を入って室の中の通路を最後の扉まで歩き、廊下へ出る。
        /// 扉が 1 枚の室は、入って同じ扉から出る。
        /// </summary>
        private static IEnumerator WalkThroughRoom(PlayerController player, LibraryInterior.Point local,
            List<string> bad, string no, float z, bool south, float[] doors)
        {
            float corridorY = south ? 21.0f : 34.0f;
            float aisleY = south ? 18.4f : 36.4f;
            float first = doors[0];
            float last = doors[doors.Length - 1];

            player.Teleport(local(first, corridorY, z), 0f);
            yield return LibraryInterior.Settled();
            float floorY = local(0f, 0f, z).y;
            AssertOnFloor(player, floorY, no + " の前の廊下", bad);

            yield return LibraryInterior.WalkAll(player, bad, no + " に入る", new[]
            {
                local(first, aisleY, z), local(last, aisleY, z),
            });
            AssertOnFloor(player, floorY, no + " の中", bad);

            yield return LibraryInterior.WalkAll(player, bad, no + " から出る", new[]
            {
                local(last, corridorY, z),
            });
            AssertOnFloor(player, floorY, no + " を出た廊下", bad);
        }

        /// <summary>
        /// Blender の屋内座標 (x, y, z) を世界へ。FBX の取り込みで x や y が反転するので、
        /// 2 つの Empty の位置と合う符号を 4 通りから選ぶ。
        /// </summary>
        private static LibraryInterior.Point BuildMapping(Transform interior, out string error)
        {
            error = null;
            Transform corridor = null;
            Transform lounge = null;
            foreach (Transform t in interior.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "poi_lecture_corridor")
                {
                    corridor = t;
                }
                else if (t.name == "poi_lecture_lounge")
                {
                    lounge = t;
                }
            }

            if (corridor == null || lounge == null)
            {
                error = "poi_lecture_corridor / poi_lecture_lounge が無い";
                return null;
            }

            // Blender の位置: corridor (-6.0, 21.6, 0.0)、lounge (42.0, 28.0, 8.8)。
            foreach (float sx in new[] { 1f, -1f })
            {
                foreach (float sy in new[] { 1f, -1f })
                {
                    Vector3 a = interior.TransformPoint(new Vector3(sx * -6.0f, 0f, sy * 21.6f));
                    Vector3 b = interior.TransformPoint(new Vector3(sx * 42.0f, 8.8f, sy * 28.0f));
                    if ((a - corridor.position).magnitude < 0.05f && (b - lounge.position).magnitude < 0.05f)
                    {
                        float fx = sx;
                        float fy = sy;
                        return (x, y, z) => interior.TransformPoint(new Vector3(fx * x, z, fy * y));
                    }
                }
            }

            error = "屋内の座標の向きが合わない: corridor " + corridor.position.ToString("F2") + " lounge "
                + lounge.position.ToString("F2");
            return null;
        }

        private static void AssertOnFloor(PlayerController player, float expectedY, string label, List<string> bad)
        {
            float y = player.transform.position.y;
            if (Mathf.Abs(y - expectedY) > FloorTolerance)
            {
                bad.Add(label + ": 足もとの高さ " + y.ToString("F2") + " m（期待 " + expectedY.ToString("F2") + " m）");
            }
        }
    }
}
