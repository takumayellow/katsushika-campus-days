using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 本物のキャンパスで、人がどれだけ固まって見えるかを数える（NPC の密集の調査）。
    /// 見る場所（門・メインストリート・水盤・各入口）に立ち、時刻ごとにしばらく歩かせながら、
    /// 立っている所から <see cref="NearRadius"/> m 以内の人（モブと物語の人）と、
    /// モブどうしが <see cref="ClumpRadius"/> m 以内に何人固まっているかを毎フレーム数える。
    /// 環境変数 KCD_MOB_DENSITY_DIR があるときだけ動き、そのフォルダに一覧（.txt）を書く。
    ///   KCD_MOB_DENSITY_NAME  ファイル名（拡張子なし。既定 mob-density）
    /// </summary>
    public sealed class MobDensityMeasureTests : SceneTestBase
    {
        private const string DirVariable = "KCD_MOB_DENSITY_DIR";
        private const string NameVariable = "KCD_MOB_DENSITY_NAME";

        private const int Seed = 482;
        private const float NearRadius = 15f;
        private const float ClumpRadius = 4f;

        /// <summary>立ってから数え始めるまでのゲーム内の秒数（出る人が目標に届くまで）。</summary>
        private const float SettleSeconds = 20f;

        /// <summary>1 か所・1 時刻で数えるゲーム内の秒数。</summary>
        private const float CountSeconds = 45f;

        private const float CountTimeScale = 4f;
        private const int TimeoutMs = SceneTestTimeoutMs + 900000;

        private static readonly float[] Hours = { 9.5f, 12.5f, 15f, 18f };

        private static readonly string[] Spots =
        {
            "place:gate_main", "place:mall_bench", "place:library_pond",
            "entrance:lecture", "entrance:cafeteria", "entrance:kyoso",
        };

        [UnityTest]
        [Timeout(TimeoutMs)]
        public IEnumerator 見る場所ごとに近くの人と固まりを数える()
        {
            string folder = System.Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore(DirVariable + " が無いので数えない");
            }

            string name = System.Environment.GetEnvironmentVariable(NameVariable);
            if (string.IsNullOrEmpty(name))
            {
                name = "mob-density";
            }

            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            Random.InitState(Seed);

            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            MobScheduler scheduler = Object.FindAnyObjectByType<MobScheduler>();
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            Assert.IsNotNull(scheduler, "MobScheduler が無い");
            Assert.IsNotNull(player, "PlayerController が無い");
            NPCTalker[] talkers = Object.FindObjectsByType<NPCTalker>();

            var report = new StringBuilder();
            report.Append("near ").Append(NearRadius).Append("m clump ").Append(ClumpRadius).Append("m count ")
                .Append(CountSeconds).Append("s\n");
            report.Append("hour spot active/target nearMob(mean/peak) nearNpc(peak) clump(mean/peak)\n");

            var positions = new List<Vector3>();
            float worstClump = 0f;
            foreach (float hour in Hours)
            {
                foreach (string spot in Spots)
                {
                    if (!scheduler.TryGetWaypoint(spot, out Vector3 at))
                    {
                        report.Append(hour.ToString("0.00")).Append(' ').Append(spot).Append(" (点が無い)\n");
                        continue;
                    }

                    player.Teleport(at + new Vector3(0f, 0.1f, -3f), 0f);
                    cycle.SetHours(hour);
                    cycle.Paused = true;
                    Time.timeScale = CountTimeScale;
                    float settle = Time.time + SettleSeconds;
                    while (Time.time < settle)
                    {
                        yield return null;
                    }

                    double nearSum = 0;
                    double clumpSum = 0;
                    int frames = 0;
                    int nearPeak = 0;
                    int npcPeak = 0;
                    int clumpPeak = 0;
                    float until = Time.time + CountSeconds;
                    while (Time.time < until)
                    {
                        Vector3 me = player.transform.position;
                        positions.Clear();
                        for (int i = 0; i < scheduler.PoolSize; i++)
                        {
                            MobWalker walker = scheduler.PoolAt(i);
                            if (walker != null && walker.IsRunning)
                            {
                                positions.Add(walker.transform.position);
                            }
                        }

                        int near = 0;
                        foreach (Vector3 p in positions)
                        {
                            if (Flat(p, me) <= NearRadius)
                            {
                                near++;
                            }
                        }

                        int npc = 0;
                        foreach (NPCTalker talker in talkers)
                        {
                            if (talker != null && talker.isActiveAndEnabled && Flat(talker.transform.position, me) <= NearRadius)
                            {
                                npc++;
                            }
                        }

                        int clump = Clump(positions);
                        nearSum += near;
                        clumpSum += clump;
                        frames++;
                        nearPeak = Mathf.Max(nearPeak, near);
                        npcPeak = Mathf.Max(npcPeak, npc);
                        clumpPeak = Mathf.Max(clumpPeak, clump);
                        yield return null;
                    }

                    Time.timeScale = 1f;
                    float clumpMean = frames > 0 ? (float)(clumpSum / frames) : 0f;
                    worstClump = Mathf.Max(worstClump, clumpMean);
                    report.Append(hour.ToString("0.00")).Append(' ').Append(spot).Append(' ')
                        .Append(scheduler.ActiveCount).Append('/').Append(scheduler.TargetCount).Append(' ')
                        .Append(frames > 0 ? (nearSum / frames).ToString("0.0") : "-").Append('/').Append(nearPeak).Append(' ')
                        .Append(npcPeak).Append(' ')
                        .Append(clumpMean.ToString("0.0")).Append('/').Append(clumpPeak).Append('\n');
                }
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
            Assert.Greater(worstClump, 0f, "どこにも人が出ていない");
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>モブ 1 人を中心に ClumpRadius 以内にいる人数（自分を含む）の、いちばん多い値。</summary>
        private static int Clump(List<Vector3> positions)
        {
            int best = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                int n = 0;
                for (int j = 0; j < positions.Count; j++)
                {
                    if (Flat(positions[i], positions[j]) <= ClumpRadius)
                    {
                        n++;
                    }
                }

                best = Mathf.Max(best, n);
            }

            return best;
        }
    }
}
