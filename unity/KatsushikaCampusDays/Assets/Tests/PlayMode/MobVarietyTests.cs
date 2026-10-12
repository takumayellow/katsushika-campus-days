using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 同じ見た目・同じ言動のモブが近くに並ばないことを、本物の MobScheduler で一日を通して確かめる。
    /// 時間帯ごとに何時か選んで早送りし、<see cref="Samples"/> 回のぞいて次を数える。
    ///   見た目: 出ている人のうち <see cref="NearDistance"/> m 以内にいる 2 人で見た目（VariantId）が同じ組。
    ///   台詞:   見えている人に片端から話しかけ、見えている人どうしで同じ台詞を言った組。
    ///   動き:   見えていて <see cref="NearDistance"/> m 以内にいる 2 人で、のぞいた毎回 Locomotion の周期が
    ///           <see cref="InStepPhase"/> 以内に揃っていた組（同じ動きを同じ拍子で繰り返している）。
    /// </summary>
    public sealed class MobVarietyTests : SceneTestBase
    {
        /// <summary>同じ場所にいると見なす距離（m）。</summary>
        private const float NearDistance = 30f;

        /// <summary>
        /// 周期が揃っているとみなす差（1 周に対する割合）。枠ごとのずらし幅はどの 2 枠も 0.02 以上離れている
        /// （BlinkPhaseTests）ので、それより狭い差がのぞいた毎回続くなら、同じ動きを同じ拍子で繰り返している。
        /// </summary>
        private const float InStepPhase = 0.015f;

        private const float FastForward = 8f;

        /// <summary>プレイヤーを人だかりへ移すとき、建物の入口からこれだけ離す（m）。入口に触れると屋内へ移ってしまう。</summary>
        private const float EntranceClearance = 8f;

        // 道のりを歩き終えた人の分は同じ時間帯のうちは足さないことがあり、目標の人数に届かない時刻がある。
        // 届かなければこの秒数（実時間）で数え始める。
        private const float FillTimeoutSeconds = 8f;
        private const int Samples = 5;
        private const float SampleGapSeconds = 4f;

        /// <summary>のぞくのを待つ上限（実時間の秒）。時計が止まったら（リザルトなど）ここで打ち切る。</summary>
        private const float SampleTimeoutSeconds = 10f;

        /// <summary>
        /// 朝・昼休み・午後・夕方・夜の、始め・中ほど・終わり近く。一日は 20:00 で終わってリザルトが出るので、夜はその手前まで。
        /// </summary>
        private static readonly float[] Hours = { 8.5f, 10f, 11.25f, 12f, 13f, 14f, 15.5f, 16.75f, 17.5f, 18.5f, 19.25f, 19.75f };

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 一日を通して近くに同じ見た目の人がいない()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            Random.InitState(165);
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            MobScheduler scheduler = Object.FindAnyObjectByType<MobScheduler>();
            Assert.IsNotNull(scheduler, "MobScheduler が無い");

            GameObject player = GameObject.FindWithTag("Player");
            Assert.IsNotNull(player, "プレイヤーが無い");
            PlayerController controller = player.GetComponent<PlayerController>();
            Assert.IsNotNull(controller, "PlayerController が無い");
            List<Vector3> entrances = Entrances();
            var report = new StringBuilder();
            int worst = 0;
            int worstLines = 0;
            int worstInStep = 0;
            try
            {
                foreach (float hour in Hours)
                {
                    cycle.SetHours(hour);
                    cycle.Paused = true;
                    Time.timeScale = FastForward;

                    float deadline = Time.realtimeSinceStartup + FillTimeoutSeconds;
                    while (Time.realtimeSinceStartup < deadline && scheduler.ActiveCount < scheduler.TargetCount)
                    {
                        yield return null;
                    }

                    // 見えている人が多いほど台詞と動きの重なりを確かめられるので、いちばん人の多い所に立つ。
                    StandInCrowd(Running(scheduler), controller, entrances);

                    int maxPairs = 0;
                    int maxPeople = 0;
                    int maxActive = 0;
                    int maxShared = 0;
                    int maxSameLine = 0;
                    int talked = 0;
                    var phases = new List<Dictionary<int, float>>();
                    var positions = new List<Dictionary<int, Vector3>>();
                    for (int s = 0; s < Samples; s++)
                    {
                        float until = Time.time + SampleGapSeconds;
                        float stuck = Time.realtimeSinceStartup + SampleTimeoutSeconds;
                        while (Time.time < until)
                        {
                            Assert.Less(Time.realtimeSinceStartup, stuck, hour + " 時で時計が進まない（timeScale " + Time.timeScale + "）");
                            yield return null;
                        }

                        List<MobWalker> running = Running(scheduler);
                        CountSameLookNearby(running, out int pairs, out int people);
                        maxPairs = Mathf.Max(maxPairs, pairs);
                        maxPeople = Mathf.Max(maxPeople, people);
                        maxActive = Mathf.Max(maxActive, running.Count);
                        maxShared = Mathf.Max(maxShared, SharingLook(running));

                        talked += TalkToVisible(running, player);
                        maxSameLine = Mathf.Max(maxSameLine, CountSameLineVisible(running));
                        RecordPhases(running, phases, positions);
                    }

                    int inStep = CountInStep(phases, positions);
                    worst = Mathf.Max(worst, maxPairs);
                    worstLines = Mathf.Max(worstLines, maxSameLine);
                    worstInStep = Mathf.Max(worstInStep, inStep);
                    string line = string.Format(
                        "{0:0.00} 時: 出ている人 {1}（目標 {2}）、キャンパスのどこかに同じ見た目がいる人 {3}、"
                        + "{4} m 以内で同じ見た目の組 {5}、そのうちの人 {6}、"
                        + "話しかけた人 {7}、見えている人どうしで同じ台詞の組 {8}、周期が揃い続けた組 {9}\n",
                        hour, maxActive, scheduler.TargetCount, maxShared, NearDistance, maxPairs, maxPeople,
                        talked, maxSameLine, inStep);
                    report.Append(line);
                    Debug.Log("[MobVariety] " + line);
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }

            Debug.Log("[MobVariety]\n" + report);
            Assert.AreEqual(0, worst, "近くに同じ見た目の人がいる:\n" + report);
            Assert.AreEqual(0, worstLines, "見えている人どうしで同じ台詞を言った:\n" + report);
            Assert.AreEqual(0, worstInStep, "近くの人と同じ拍子で動き続けている:\n" + report);
            AssertNoErrors("一日を通したモブの見た目と言動");
        }

        private static List<MobWalker> Running(MobScheduler scheduler)
        {
            var running = new List<MobWalker>();
            for (int i = 0; i < scheduler.PoolSize; i++)
            {
                MobWalker walker = scheduler.PoolAt(i);
                if (walker != null && walker.IsRunning)
                {
                    running.Add(walker);
                }
            }

            return running;
        }

        /// <summary>建物の入口（触れると屋内へ移るもの）の位置。</summary>
        private static List<Vector3> Entrances()
        {
            var points = new List<Vector3>();
            foreach (EntranceTrigger entrance in Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Exclude))
            {
                points.Add(entrance.transform.position);
            }

            foreach (DormEntrance entrance in Object.FindObjectsByType<DormEntrance>(FindObjectsInactive.Exclude))
            {
                points.Add(entrance.transform.position);
            }

            foreach (InteriorExit exit in Object.FindObjectsByType<InteriorExit>(FindObjectsInactive.Exclude))
            {
                points.Add(exit.transform.position);
            }

            return points;
        }

        /// <summary>
        /// <see cref="NearDistance"/> m 以内に出ている人がいちばん多い人の横へプレイヤーを移す。
        /// 入口に近い人は選ばない。誰もいなければ動かさない。
        /// </summary>
        private static void StandInCrowd(List<MobWalker> walkers, PlayerController player, List<Vector3> entrances)
        {
            MobWalker best = null;
            int bestCount = -1;
            foreach (MobWalker walker in walkers)
            {
                Vector3 p = walker.transform.position;
                if (NearAny(p, entrances, EntranceClearance))
                {
                    continue;
                }

                int count = 0;
                foreach (MobWalker other in walkers)
                {
                    count += Vector3.Distance(p, other.transform.position) <= NearDistance ? 1 : 0;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    best = walker;
                }
            }

            if (best == null)
            {
                return;
            }

            Vector3 stand = best.transform.position - best.transform.forward * 3f;
            if (NavMesh.SamplePosition(stand, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                stand = hit.position;
            }
            else
            {
                stand = best.transform.position;
            }

            Vector3 toward = best.transform.position - stand;
            player.Teleport(stand, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg);
        }

        private static bool NearAny(Vector3 point, List<Vector3> points, float radius)
        {
            foreach (Vector3 p in points)
            {
                if (Vector3.Distance(point, p) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>見えている人に話しかける（プレイヤーが話しかけるのと同じ道）。話しかけた人の数を返す。</summary>
        private static int TalkToVisible(List<MobWalker> walkers, GameObject player)
        {
            int talked = 0;
            foreach (MobWalker walker in walkers)
            {
                MobTalker talker = walker.GetComponent<MobTalker>();
                if (walker.Tier == MobTier.Hidden || talker == null || !talker.CanInteract || walker.LineIndex >= 0)
                {
                    continue;
                }

                talker.Interact(player);
                talked++;
            }

            return talked;
        }

        /// <summary>見えている人どうしで、同じ時間帯の同じ台詞を言った組の数。</summary>
        private static int CountSameLineVisible(List<MobWalker> walkers)
        {
            int pairs = 0;
            for (int a = 0; a < walkers.Count; a++)
            {
                MobWalker x = walkers[a];
                if (x.Tier == MobTier.Hidden || x.LineIndex < 0)
                {
                    continue;
                }

                for (int b = a + 1; b < walkers.Count; b++)
                {
                    MobWalker y = walkers[b];
                    if (y.Tier != MobTier.Hidden && y.LineIndex == x.LineIndex && y.BandId == x.BandId)
                    {
                        pairs++;
                    }
                }
            }

            return pairs;
        }

        /// <summary>見えている人の Locomotion の周期（Cycle Offset を足した 0〜1）と位置を枠ごとに記録する。</summary>
        private static void RecordPhases(
            List<MobWalker> walkers, List<Dictionary<int, float>> phases, List<Dictionary<int, Vector3>> positions)
        {
            var phase = new Dictionary<int, float>();
            var position = new Dictionary<int, Vector3>();
            foreach (MobWalker walker in walkers)
            {
                Animator animator = walker.GetComponentInChildren<Animator>();
                if (walker.Tier == MobTier.Hidden || animator == null || !animator.isActiveAndEnabled)
                {
                    continue;
                }

                BlinkPhase blink = animator.GetComponent<BlinkPhase>();
                float offset = blink != null ? blink.Offset : 0f;
                phase[walker.Slot] = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime + offset, 1f);
                position[walker.Slot] = walker.transform.position;
            }

            phases.Add(phase);
            positions.Add(position);
        }

        /// <summary>のぞいた毎回、見えていて近くにいて周期が揃っていた 2 人の組の数。</summary>
        private static int CountInStep(List<Dictionary<int, float>> phases, List<Dictionary<int, Vector3>> positions)
        {
            if (phases.Count == 0)
            {
                return 0;
            }

            int pairs = 0;
            var slots = new List<int>(phases[0].Keys);
            for (int a = 0; a < slots.Count; a++)
            {
                for (int b = a + 1; b < slots.Count; b++)
                {
                    bool always = true;
                    for (int s = 0; s < phases.Count && always; s++)
                    {
                        always = phases[s].TryGetValue(slots[a], out float pa)
                            && phases[s].TryGetValue(slots[b], out float pb)
                            && BlinkPhase.CircularDistance(pa, pb) <= InStepPhase
                            && Vector3.Distance(positions[s][slots[a]], positions[s][slots[b]]) <= NearDistance;
                    }

                    pairs += always ? 1 : 0;
                }
            }

            return pairs;
        }

        /// <summary>出ている人のうち、ほかにも同じ見た目の人が出ている人の数。</summary>
        private static int SharingLook(List<MobWalker> walkers)
        {
            var counts = new Dictionary<string, int>();
            foreach (MobWalker walker in walkers)
            {
                counts.TryGetValue(walker.VariantId, out int n);
                counts[walker.VariantId] = n + 1;
            }

            int shared = 0;
            foreach (int n in counts.Values)
            {
                shared += n > 1 ? n : 0;
            }

            return shared;
        }

        /// <summary>NearDistance 以内にいる 2 人で見た目が同じ組の数と、その組に入っている人の数。</summary>
        private static void CountSameLookNearby(List<MobWalker> walkers, out int pairs, out int people)
        {
            pairs = 0;
            var involved = new HashSet<int>();
            for (int a = 0; a < walkers.Count; a++)
            {
                for (int b = a + 1; b < walkers.Count; b++)
                {
                    if (walkers[a].VariantId != walkers[b].VariantId)
                    {
                        continue;
                    }

                    if (Vector3.Distance(walkers[a].transform.position, walkers[b].transform.position) > NearDistance)
                    {
                        continue;
                    }

                    pairs++;
                    involved.Add(a);
                    involved.Add(b);
                }
            }

            people = involved.Count;
        }
    }
}
