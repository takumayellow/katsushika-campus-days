using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// キャンパスの NPC がみな NavMesh に載って歩ける (#63 の検出, #67)。
    /// 載れない NPC は NPCWander が agent を切って立ち止まらせるだけなので、画面では気付きにくい。
    /// 落ちたときは NPC ごとの位置・最寄りの NavMesh・焼いた範囲・ログを並べ、どこを直すかの手がかりにする。
    /// </summary>
    public sealed class NpcNavMeshTests : SceneTestBase
    {
        /// <summary>最寄りの NavMesh を探す距離（m）。NPCWander の 3 m より広く探して、どれだけ離れているかを出す。</summary>
        private const float ProbeDistance = 50f;

        /// <summary>NavMesh に関係しそうなログ。失敗の判定には使わず、手がかりとして並べるだけ。</summary>
        private static readonly Regex NavMeshRelated = new Regex("NavMesh|agent", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_EveryNpcAgentIsOnTheNavMesh()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            // NPCWander は Start から NavMeshRetrySeconds の間、NavMesh への乗り直しを試みる。それが終わるまで待つ。
            float settleUntil = Time.time + NPCWander.NavMeshRetrySeconds + 0.5f;
            float realDeadline = Time.realtimeSinceStartup + NPCWander.NavMeshRetrySeconds * 6f;
            while (Time.time < settleUntil && Time.realtimeSinceStartup < realDeadline)
            {
                yield return null;
            }

            NPCWander[] npcs = Object.FindObjectsByType<NPCWander>(FindObjectsInactive.Exclude);
            Assert.Greater(npcs.Length, 0, "キャンパスに NPCWander が 1 体もいない");

            var stranded = new List<string>();
            var report = new StringBuilder();
            foreach (NPCWander npc in npcs)
            {
                NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
                bool onNavMesh = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
                if (!onNavMesh)
                {
                    stranded.Add(npc.name);
                }

                report.Append("\n  ").Append(onNavMesh ? "[OK] " : "[NG] ").Append(DescribeNpc(npc, agent));
            }

            List<CapturedLog> failures = Capture.Matching(LogCapture.NavMeshFailure);
            if (stranded.Count == 0 && failures.Count == 0)
            {
                AssertNoErrors("キャンパスの NPC");
                yield break;
            }

            Assert.Fail(
                npcs.Length + " 体中 " + stranded.Count + " 体が NavMesh に載っていない（" + string.Join(", ", stranded) + "）。" +
                "NavMesh の失敗のログ " + failures.Count + " 行。" +
                "\nNPC ごと（NPCWander は " + NPCWander.NavMeshSnapDistance + " m 以内に NavMesh が無いと agent を切る）:" + report +
                "\nNavMesh に関係するログ:" + LogCapture.Describe(Capture.Matching(NavMeshRelated)) +
                "\n" + DescribeNavMesh());
        }

        private static string DescribeNpc(NPCWander npc, NavMeshAgent agent)
        {
            Vector3 position = npc.transform.position;
            NPCTalker talker = npc.GetComponent<NPCTalker>();
            var builder = new StringBuilder();
            builder.Append(npc.name)
                .Append(" id=").Append(talker != null ? talker.NpcId : "-")
                .Append(" 位置 ").Append(position.ToString("F2"))
                .Append(" ホーム ").Append(npc.Home.ToString("F2"));

            if (agent == null)
            {
                return builder.Append(" NavMeshAgent が無い").ToString();
            }

            builder.Append(" agent enabled=").Append(agent.enabled)
                .Append(" isOnNavMesh=").Append(agent.isActiveAndEnabled && agent.isOnNavMesh)
                .Append(" 種類=").Append(agent.agentTypeID).Append('(').Append(NavMesh.GetSettingsNameFromID(agent.agentTypeID)).Append(')')
                .Append(" 半径=").Append(agent.radius.ToString("F2"))
                .Append(" 高さ=").Append(agent.height.ToString("F2"));

            builder.Append("\n      最寄りの NavMesh（全種類）: ")
                .Append(DescribeNearest(position, NavMesh.SamplePosition(position, out NavMeshHit any, ProbeDistance, NavMesh.AllAreas), any));

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            builder.Append("\n      最寄りの NavMesh（この agent の種類）: ")
                .Append(DescribeNearest(position, NavMesh.SamplePosition(position, out NavMeshHit own, ProbeDistance, filter), own));

            if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                builder.Append("\n      足元の当たり判定: ").Append(ground.collider.name)
                    .Append(" (y ").Append(ground.point.y.ToString("F2")).Append(')');
            }
            else
            {
                builder.Append("\n      足元の当たり判定: 20 m 下まで無い");
            }

            return builder.ToString();
        }

        private static string DescribeNearest(Vector3 from, bool found, NavMeshHit hit)
        {
            if (!found)
            {
                return ProbeDistance + " m 以内に無い";
            }

            Vector3 delta = hit.position - from;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            return hit.position.ToString("F2") + " まで " + hit.distance.ToString("F2") + " m（水平 " +
                   horizontal.ToString("F2") + " m, 高さ " + delta.y.ToString("+0.00;-0.00;0.00") + " m）" +
                   (hit.distance <= NPCWander.NavMeshSnapDistance ? " 乗り直せる距離" : " 乗り直せない距離");
        }

        /// <summary>シーンの NavMesh 全体の様子。焼けていない・範囲の外・agent の種類違いを見分ける材料。</summary>
        private static string DescribeNavMesh()
        {
            var builder = new StringBuilder();
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            builder.Append("NavMesh: 頂点 ").Append(triangulation.vertices.Length)
                .Append(" / 三角形 ").Append(triangulation.indices.Length / 3);
            if (triangulation.vertices.Length > 0)
            {
                var bounds = new Bounds(triangulation.vertices[0], Vector3.zero);
                foreach (Vector3 vertex in triangulation.vertices)
                {
                    bounds.Encapsulate(vertex);
                }

                builder.Append(" / 範囲 ").Append(bounds.min.ToString("F1")).Append(" 〜 ").Append(bounds.max.ToString("F1"));
            }

            builder.Append("\nNavMesh の agent の種類:");
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(i);
                builder.Append("\n  ").Append(settings.agentTypeID).Append(' ')
                    .Append(NavMesh.GetSettingsNameFromID(settings.agentTypeID))
                    .Append(" 半径=").Append(settings.agentRadius.ToString("F2"))
                    .Append(" 高さ=").Append(settings.agentHeight.ToString("F2"))
                    .Append(" 段差=").Append(settings.agentClimb.ToString("F2"));
            }

            builder.Append("\nNavMeshSurface:");
            int surfaces = 0;
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour == null || behaviour.GetType().Name != "NavMeshSurface")
                {
                    continue;
                }

                surfaces++;
                builder.Append("\n  ").Append(DescribeSurface(behaviour));
            }

            if (surfaces == 0)
            {
                builder.Append(" シーンに無い（SceneBuilder.BuildAll の CampusStage.BakeNavMesh が走っていない）");
            }

            return builder.ToString();
        }

        /// <summary>
        /// NavMeshSurface は com.unity.ai.navigation の型で、KCD.Runtime も参照していないので、
        /// asmdef を増やさずにリフレクションでプロパティを読む。
        /// </summary>
        private static string DescribeSurface(MonoBehaviour surface)
        {
            object data = Read(surface, "navMeshData");
            object center = Read(surface, "center");
            object size = Read(surface, "size");
            var builder = new StringBuilder();
            builder.Append(surface.gameObject.name)
                .Append(" active=").Append(surface.isActiveAndEnabled)
                .Append(" 種類=").Append(Read(surface, "agentTypeID") ?? "?")
                .Append(" データ=").Append(data is Object asset && asset != null ? asset.name : "無し");
            if (center is Vector3 c && size is Vector3 s)
            {
                Vector3 origin = surface.transform.position;
                builder.Append(" 焼いた範囲 ").Append((origin + c - s * 0.5f).ToString("F1"))
                    .Append(" 〜 ").Append((origin + c + s * 0.5f).ToString("F1"));
            }

            return builder.ToString();
        }

        private static object Read(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            return info != null ? info.GetValue(target) : null;
        }
    }
}
