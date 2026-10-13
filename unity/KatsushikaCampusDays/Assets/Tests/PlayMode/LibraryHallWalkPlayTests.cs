using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 図書館 (#110): 入口から階段を上り、3F ホワイエ、ホールの扉、客席を通って舞台のピアノまで歩いてつながる。
    /// 屋内には NavMesh を焼いていないので、寮の階段のテストと同じく、プレイヤーを目的地へ向けて当たり判定ごと歩かせる（LibraryInterior.WalkAll）。
    /// 地点は Blender の屋内座標（x, y, 床からの高さ）で書き、屋内の Empty との対応から Unity の座標へ移す。
    /// </summary>
    public sealed class LibraryHallWalkPlayTests : SceneTestBase
    {
        private const float FloorTolerance = 0.45f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator WalkFromTheEntranceToTheHallPiano()
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
            yield return driver.Enter("library");
            Assert.AreEqual("library", InteriorLoader.Instance.CurrentId, "図書館へ入れない");

            Transform interior = LibraryInterior.Find();
            Assert.IsNotNull(interior, "Interior_library が無い");
            LibraryInterior.Point local = LibraryInterior.BuildMapping(interior, out string mapError);
            Assert.IsNull(mapError, mapError);

            var bad = new List<string>();
            float groundY = local(0f, 0f, 0f).y;
            yield return LibraryInterior.Settled();

            // 1F の入口から。ゲートは真ん中のレーン（x=0）を通る。
            // 直階段 S2（1F → M1）、S6（M1 → M2）、S5（M2 → 2F）で 2F へ。
            yield return LibraryInterior.WalkAll(player, bad, "1F から 2F", new[]
            {
                local(0f, 7.0f, 0f), local(0f, 12.0f, 0f), local(2.0f, 20.0f, 0f), local(0.8f, 30.0f, 0f), local(0.8f, 36.0f, 0f), local(0.8f, 39.0f, 1.7f),
                local(3.3f, 40.4f, 1.7f), local(3.3f, 42.8f, 3.3f),
                local(-5.2f, 44.2f, 3.3f), local(-5.2f, 47.0f, 3.3f), local(-5.2f, 49.0f, 5.0f),
            });
            AssertOnFloor(player, groundY + 5.0f, "2F", bad);

            // 2F の西の回廊を通り、大階段（2F → 3F）の 1 本目、踊り場、2 本目と上る。
            yield return LibraryInterior.WalkAll(player, bad, "2F から 3F", new[]
            {
                local(-5.2f, 51.0f, 5.0f), local(-20.0f, 51.0f, 5.0f), local(-25.0f, 50.5f, 5.0f), local(-25.0f, 38.0f, 5.0f),
                local(-25.0f, 20.0f, 5.0f), local(-32.0f, 20.0f, 5.0f), local(-32.0f, 24.5f, 5.0f), local(-32.0f, 27.8f, 7.5f),
                local(-29.8f, 29.2f, 7.5f), local(-27.6f, 27.8f, 7.5f), local(-27.6f, 24.5f, 10.0f),
                local(-27.6f, 21.0f, 10.0f),
            });
            AssertOnFloor(player, groundY + 10.0f, "3F ホワイエ", bad);

            Transform stool = LibraryInterior.FindPianoStool(interior);
            Assert.IsNotNull(stool, "舞台のピアノ椅子（seat_library_<nn> で，seat_library_<nn>_piano の Empty を持つもの）が無い");
            // 椅子は固い当たり判定なので、舞台の手前の地点から椅子へ向かって 0.9 m 手前までを終点にする。
            Vector3 stageApproach = local(-11.1f, 54.6f, 10.0f);
            Vector3 stoolAt = stool.position;
            Vector3 toward = stageApproach - stoolAt;
            toward.y = 0f;
            stoolAt += toward.normalized * 0.9f;
            stoolAt.y = groundY + 10.75f;

            // 3F ホワイエを東へ進み、浮き階段で前室の扉へ、扉を抜けて 1 階席の縦通路から舞台へ。
            yield return LibraryInterior.WalkAll(player, bad, "ホワイエからホール", new[]
            {
                local(-27.6f, 13.5f, 10.0f), local(-12.0f, 13.5f, 10.0f), local(-11.0f, 17.1f, 10.0f),
                local(-7.0f, 17.1f, 11.4f), local(-3.0f, 17.6f, 12.8f), local(-1.6f, 19.8f, 12.8f),
                local(-1.6f, 33.0f, 12.8f), local(2.25f, 35.0f, 12.8f), local(2.25f, 45.3f, 11.3f),
                local(2.25f, 53.0f, 10.0f), local(2.25f, 54.6f, 10.0f), stageApproach,
                stoolAt,
            });
            AssertOnFloor(player, groundY + 10.75f, "舞台のピアノのそば", bad);

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("図書館のホール");
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
