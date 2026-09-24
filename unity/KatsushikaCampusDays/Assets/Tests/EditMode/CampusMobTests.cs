using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// Campus シーンに焼き込んだモブの学生を守る (#22)。シーンの開き方は CampusNpcNavMeshTests と同じ。
    ///
    /// - 人は MobScheduler.MaxActive 人ぶん焼き込み、どれも非アクティブ（実行中は貸し借りだけで Instantiate しない）。
    /// - NavMeshAgent はシーンでは止めてある (#63)。当たり判定は trigger だけ（プレイヤーを押さない）。
    /// - 色は .mat で分け、MaterialPropertyBlock を使わない（SRP Batcher から外さない）。遠い段は輪郭のパスを切る。
    /// - 時間割のどの行き先も NavMesh の上にあり、道のりの隣り合う点どうしが NavMesh でつながっている。
    /// </summary>
    public sealed class CampusMobTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>焼き込んだ行き先が NavMesh からずれていてよい距離（m）。SceneBuilder は NavMesh の上に置く。</summary>
        private const float OnNavMeshTolerance = 0.5f;

        private Scene _scene;
        private readonly List<MobScheduler> _schedulers = new List<MobScheduler>();
        private MobSchedule _schedule;

        private MobScheduler Scheduler
        {
            get
            {
                Assert.AreEqual(1, _schedulers.Count, ScenePath + " の MobScheduler の数（SceneBuilder で組み直す）");
                return _schedulers[0];
            }
        }

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                _schedulers.AddRange(go.GetComponentsInChildren<MobScheduler>(true));
            }

            string path = Path.Combine(Application.dataPath, "Data", "Mobs", "schedule.json");
            _schedule = MobSchedule.Parse(File.ReadAllText(path));
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void 上限の人数ぶん焼き込んである()
        {
            MobScheduler scheduler = Scheduler;
            Assert.AreEqual(MobScheduler.MaxActive, scheduler.PoolSize);

            var seen = new HashSet<MobWalker>();
            var variants = new HashSet<string>();
            for (int i = 0; i < scheduler.PoolSize; i++)
            {
                MobWalker walker = scheduler.PoolAt(i);
                Assert.IsNotNull(walker, i + " 番目の枠が空");
                Assert.IsTrue(seen.Add(walker), walker.name + " が 2 つの枠に入っている");
                variants.Add(walker.VariantId);
            }

            Assert.GreaterOrEqual(variants.Count, 4, "見た目の組み合わせが少ない");
        }

        [Test]
        public void どの人も非アクティブで_agent_は止めてある()
        {
            var bad = new List<string>();
            foreach (MobWalker walker in Walkers())
            {
                if (walker.gameObject.activeSelf)
                {
                    bad.Add(walker.name + ": シーンで有効（出す前から描かれる）");
                }

                NavMeshAgent agent = walker.GetComponent<NavMeshAgent>();
                if (agent == null || agent.enabled)
                {
                    bad.Add(walker.name + ": NavMeshAgent が無いか有効（読み込みで NavMesh より先に作られて失敗する）");
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 当たり判定は_trigger_だけでプレイヤーを押さない()
        {
            var bad = new List<string>();
            foreach (MobWalker walker in Walkers())
            {
                Collider[] colliders = walker.GetComponentsInChildren<Collider>(true);
                if (colliders.Length == 0)
                {
                    bad.Add(walker.name + ": 話しかける判定が無い");
                }

                foreach (Collider collider in colliders)
                {
                    if (!collider.isTrigger)
                    {
                        bad.Add(walker.name + "/" + collider.name + ": trigger ではない当たり判定");
                    }
                }

                if (walker.GetComponent<MobTalker>() == null)
                {
                    bad.Add(walker.name + ": MobTalker が無い");
                }

                if (walker.gameObject.layer != LayerMask.NameToLayer("NPC"))
                {
                    bad.Add(walker.name + ": NPC のレイヤーではない（E で話しかけられない）");
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 色は_mat_で分け_遠い段は輪郭を切る()
        {
            var bad = new List<string>();
            foreach (MobWalker walker in Walkers())
            {
                if (walker.RendererCount == 0)
                {
                    bad.Add(walker.name + ": 描く Renderer が無い");
                }

                for (int r = 0; r < walker.RendererCount; r++)
                {
                    MobWalker.RendererLod lod = walker.RendererAt(r);
                    if (lod == null || lod.Renderer == null)
                    {
                        bad.Add(walker.name + ": " + r + " 番目の Renderer が無い");
                        continue;
                    }

                    if (lod.Renderer.HasPropertyBlock())
                    {
                        bad.Add(lod.Renderer.name + ": MaterialPropertyBlock がある（SRP Batcher から外れる）");
                    }

                    if (lod.Near.Length == 0 || lod.Far.Length != lod.Near.Length)
                    {
                        bad.Add(lod.Renderer.name + ": 近い / 遠いマテリアルの数が合わない");
                        continue;
                    }

                    for (int i = 0; i < lod.Near.Length; i++)
                    {
                        Material near = lod.Near[i];
                        Material far = lod.Far[i];
                        if (near == null || far == null)
                        {
                            bad.Add(lod.Renderer.name + ": " + i + " 番目のマテリアルが無い");
                            continue;
                        }

                        if (near.shader != far.shader)
                        {
                            bad.Add(far.name + ": 近いときとシェーダが違う");
                        }

                        if (far.GetShaderPassEnabled("SRPDefaultUnlit"))
                        {
                            bad.Add(far.name + ": 遠い段なのに輪郭のパスが有効");
                        }
                    }
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 時間割のどの行き先も焼き込んである()
        {
            Assert.IsNotNull(_schedule, "schedule.json を読めない");
            MobScheduler scheduler = Scheduler;
            List<MobSchedule.Waypoint> waypoints = _schedule.AllWaypoints();

            var missing = new List<string>();
            foreach (MobSchedule.Waypoint waypoint in waypoints)
            {
                if (!scheduler.TryGetWaypoint(waypoint.Key, out Vector3 _))
                {
                    missing.Add(waypoint.Key);
                }
            }

            Assert.IsEmpty(missing, "NavMesh の上に置けなかった行き先:\n" + string.Join("\n", missing));
            Assert.AreEqual(waypoints.Count, scheduler.WaypointCount);
        }

        [Test]
        public void どの行き先も_NavMesh_の上にある()
        {
            MobScheduler scheduler = Scheduler;
            var bad = new List<string>();
            for (int i = 0; i < scheduler.WaypointCount; i++)
            {
                string key = scheduler.WaypointKeyAt(i);
                Assert.IsTrue(scheduler.TryGetWaypoint(key, out Vector3 p), key);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, OnNavMeshTolerance, NavMesh.AllAreas))
                {
                    bad.Add(string.Format("{0}: {1} から {2} m 以内に NavMesh が無い", key, p, OnNavMeshTolerance));
                }
            }

            Assert.Greater(scheduler.WaypointCount, 0, "行き先が 1 つも無い");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 道のりの隣り合う点が_NavMesh_でつながっている()
        {
            Assert.IsNotNull(_schedule, "schedule.json を読めない");
            MobScheduler scheduler = Scheduler;
            var path = new NavMeshPath();
            var checkedLegs = new HashSet<string>();
            var bad = new List<string>();

            foreach (MobSchedule.Band band in _schedule.Bands)
            {
                foreach (MobSchedule.Route route in band.Routes)
                {
                    for (int i = 0; i + 1 < route.Waypoints.Count; i++)
                    {
                        string from = route.Waypoints[i].Key;
                        string to = route.Waypoints[i + 1].Key;
                        if (!checkedLegs.Add(from + " -> " + to))
                        {
                            continue;
                        }

                        if (!scheduler.TryGetWaypoint(from, out Vector3 a) || !scheduler.TryGetWaypoint(to, out Vector3 b))
                        {
                            bad.Add(band.Id + ": " + from + " -> " + to + " の点が焼き込まれていない");
                            continue;
                        }

                        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        {
                            bad.Add(string.Format("{0}: {1} -> {2} が NavMesh でつながらない（{3}）", band.Id, from, to, path.status));
                        }
                    }
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private IEnumerable<MobWalker> Walkers()
        {
            MobScheduler scheduler = Scheduler;
            Assert.Greater(scheduler.PoolSize, 0, "モブが 1 人もいない");
            for (int i = 0; i < scheduler.PoolSize; i++)
            {
                MobWalker walker = scheduler.PoolAt(i);
                if (walker != null)
                {
                    yield return walker;
                }
            }
        }
    }
}
