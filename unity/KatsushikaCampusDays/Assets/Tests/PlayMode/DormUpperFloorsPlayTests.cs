using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 寮 (#113): 玄関のある 1 階から、主階段で 2 階のホールへ、塔屋の階段で屋上へ歩いて上がれる。
    /// 床から落ちず、階段室の入口（幅 0.90 m）でもはまらない。
    /// 地点は Blender の屋内座標（x, y, 床からの高さ）で書き、屋内の Empty との対応から Unity の座標へ移す。
    /// </summary>
    public sealed class DormUpperFloorsPlayTests : SceneTestBase
    {
        private const float WalkSeconds = 6f;
        private const float Arrive = 0.6f;
        private const float FloorTolerance = 0.45f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator WalkFromTheHallToTheSecondFloorAndTheRoof()
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
            yield return driver.Enter("dorm");
            Assert.AreEqual("dorm", InteriorLoader.Instance.CurrentId, "寮へ入れない");

            Transform interior = FindInterior();
            Assert.IsNotNull(interior, "Interior_dorm が無い");
            Func local = BuildMapping(interior, out string mapError);
            Assert.IsNull(mapError, mapError);

            var bad = new List<string>();
            float groundY = local(0f, 0f, 0f).y;

            // 1 階のエレベーターホールから、階段室の入口（西の壁の南寄り）を抜けて主階段を上る。
            player.Teleport(local(3.0f, 35.0f, 0.05f), 0f);
            yield return Settled();
            yield return WalkAll(player, bad, "主階段", new[]
            {
                local(4.20f, 35.55f, 0f), local(5.45f, 35.55f, 0f), local(5.45f, 37.80f, 0f), local(5.60f, 38.90f, 0f),
                local(6.95f, 38.90f, 0f), local(6.95f, 37.00f, 0f), local(6.95f, 34.90f, 0f),
            });
            AssertOnFloor(player, groundY + 3.30f, "2 階の階段の上", bad);

            // 2 階のホールから、塔屋の直階段（北の下端から南の上端へ）を上って屋上へ。
            yield return WalkAll(player, bad, "塔屋の階段", new[]
            {
                local(0.0f, 36.40f, 0f), local(-1.80f, 39.60f, 0f), local(-3.30f, 39.60f, 0f),
                local(-3.30f, 36.00f, 0f), local(-3.30f, 33.00f, 0f), local(0.0f, 14.00f, 0f),
            });
            AssertOnFloor(player, groundY + 6.60f, "屋上", bad);

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("寮の階段");
        }

        private delegate Vector3 Func(float x, float y, float z);

        private static Transform FindInterior()
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.name == "Interior_dorm")
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// Blender の屋内座標 (x, y, z) を世界へ。FBX の取り込みで x や y が反転するので、
        /// x も y も 0 でない 2 つの Empty の位置と合う符号を 4 通りから選ぶ。
        /// </summary>
        private static Func BuildMapping(Transform interior, out string error)
        {
            error = null;
            Transform hall2 = null;
            Transform shokudo = null;
            foreach (Transform t in interior.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "poi_dorm_hall2")
                {
                    hall2 = t;
                }
                else if (t.name == "poi_dorm_shokudo")
                {
                    shokudo = t;
                }
            }

            if (hall2 == null || shokudo == null)
            {
                error = "poi_dorm_hall2 / poi_dorm_shokudo が無い";
                return null;
            }

            // Blender の位置: hall2 (0.0, 36.40, 3.30)、shokudo (4.10, 19.10, 床の高さ)。
            foreach (float sx in new[] { 1f, -1f })
            {
                foreach (float sy in new[] { 1f, -1f })
                {
                    Vector3 a = interior.TransformPoint(new Vector3(0f, 3.30f, sy * 36.40f));
                    Vector3 b = interior.TransformPoint(new Vector3(sx * 4.10f, 0f, sy * 19.10f));
                    Vector2 bFlat = new Vector2(b.x - shokudo.position.x, b.z - shokudo.position.z);
                    if ((a - hall2.position).magnitude < 0.05f && bFlat.magnitude < 0.05f)
                    {
                        float fx = sx;
                        float fy = sy;
                        return (x, y, z) => interior.TransformPoint(new Vector3(fx * x, z, fy * y));
                    }
                }
            }

            error = "屋内の座標の向きが合わない: hall2 " + hall2.position.ToString("F2") + " shokudo "
                + shokudo.position.ToString("F2");
            return null;
        }

        private static IEnumerator Settled()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WalkAll(PlayerController player, List<string> bad, string label, Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 target = points[i];
                player.SetAutopilot(target, player.RunSpeed);
                float deadline = Time.realtimeSinceStartup + WalkSeconds;
                while (player.AutopilotActive && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                player.ClearAutopilot();
                Vector3 flat = player.transform.position - target;
                flat.y = 0f;
                if (flat.magnitude > Arrive)
                {
                    bad.Add(label + " の " + (i + 1) + " 番目の地点に着けない（残り " + flat.magnitude.ToString("F2")
                        + " m、いま " + player.transform.position.ToString("F2") + "）");
                    yield break;
                }
            }
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
