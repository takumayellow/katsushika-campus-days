using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 一日の長さを決める材料として、本編を一周する道のりを本物のキャンパスの NavMesh で測る（#410）。
    /// 環境変数 KCD_VIEW_CAPTURE_DIR があるときだけ測り、区間ごとの距離と、歩き・走りでかかるゲーム内の時間を書き出す。
    /// 屋内の目的地は入口までを測る（屋内を歩く分は含まない）。
    /// </summary>
    public sealed class DayRouteMeasureTests : SceneTestBase
    {
        private const string DirVariable = "KCD_VIEW_CAPTURE_DIR";

        /// <summary>本編の順（前提を守る順）。最後は夕日のあとの帰り道。</summary>
        private static readonly string[] Route =
        {
            "q_orientation", "q_coffee", "q_lunch", "q_gym", "q_lecture", "q_library", "q_park", "q_sunset", "q_sq_night_walk",
        };

        /// <summary>比べる一日の長さ（実時間の秒）。</summary>
        private static readonly float[] DayLengths = { 720f, 960f, 1200f, 1440f };

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 本編を一周する道のりを測る()
        {
            string folder = System.Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore(DirVariable + " が無いので測らない");
            }

            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "PlayerController が無い");
            QuestSystem quests = GameManager.Instance.Quests;

            var report = new StringBuilder();
            Vector3 here = player.transform.position;
            float total = 0f;
            float mainTotal = 0f;
            int estimated = 0;
            foreach (string questId in Route)
            {
                QuestData quest = quests.Find(questId);
                Assert.IsNotNull(quest, questId + " が無い");
                foreach (QuestStep step in quest.Steps)
                {
                    if (!QuestObjectiveLocator.TryLocate(step, here, out Vector3 there))
                    {
                        report.Append(questId).Append(' ').Append(step.Kind).Append(' ').Append(step.Target).Append(" (not found)\n");
                        continue;
                    }

                    bool onMesh = PathLength(here, there, out float length);
                    if (!onMesh)
                    {
                        estimated++;
                    }

                    total += length;
                    if (questId != "q_sq_night_walk")
                    {
                        mainTotal = total;
                    }

                    report.Append(questId).Append(' ').Append(step.Kind).Append(' ').Append(step.Target)
                        .Append(' ').Append(length.ToString("F1")).Append(onMesh ? " m\n" : " m (straight x1.3)\n");
                    here = there;
                }
            }

            report.Append("main ").Append(mainTotal.ToString("F1")).Append(" m / with walk home ").Append(total.ToString("F1"))
                .Append(" m / estimated legs ").Append(estimated).Append('\n');
            foreach (float day in DayLengths)
            {
                float secondsPerHour = day / 24f;
                report.Append("day ").Append(day).Append("s: 1h = ").Append(secondsPerHour.ToString("F1")).Append("s real, main walk ")
                    .Append((mainTotal / PlayerController.DefaultWalkSpeed / secondsPerHour).ToString("F2")).Append("h, run ")
                    .Append((mainTotal / PlayerController.DefaultRunSpeed / secondsPerHour).ToString("F2")).Append("h, 08:30-20:00 = ")
                    .Append((11.5f * secondsPerHour).ToString("F0")).Append("s real\n");
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "day-route.txt"), report.ToString(), new UTF8Encoding(false));
            Assert.Greater(mainTotal, 0f, "道のりが測れない");
        }

        /// <summary>NavMesh の道のり。両端が NavMesh に載らないか道が途切れるときは、直線の 1.3 倍で見積もって false を返す。</summary>
        private static bool PathLength(Vector3 from, Vector3 to, out float length)
        {
            length = Vector3.Distance(from, to) * 1.3f;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 4f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit b, 4f, NavMesh.AllAreas))
            {
                return false;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            float sum = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                sum += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            length = sum;
            return true;
        }
    }
}
