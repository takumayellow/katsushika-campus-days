using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 図書館の水盤の前に、14:45 に出ている人を集めて撮る（#165 の続き）。見た目の割り当てを変える前と後で
    /// 同じ手順・同じ乱数の種で撮り、並べて比べるための画。環境変数 KCD_MOB_CAPTURE_DIR があるときだけ撮る。
    ///   KCD_MOB_CAPTURE_DIR   PNG と一覧（.txt）を書き出すフォルダ
    ///   KCD_MOB_CAPTURE_NAME  ファイル名（拡張子なし。既定 mob-crowd）
    /// </summary>
    public sealed class MobCrowdCaptureTests : SceneTestBase
    {
        private const string DirVariable = "KCD_MOB_CAPTURE_DIR";
        private const string NameVariable = "KCD_MOB_CAPTURE_NAME";

        private const float CaptureHour = 14.75f;
        private const int Seed = 165;
        private const int Width = 1600;
        private const int Height = 900;

        /// <summary>集める人の立ち位置。水盤の点から dir の向きへ Depth m、横へ Lateral m ずつ並べる。</summary>
        private const int PerRow = 5;
        private const float Lateral = 1.7f;
        private const float FirstDepth = 7f;
        private const float RowDepth = 2.6f;

        /// <summary>歩き出してから撮るまでの秒数（ゲーム内）。</summary>
        private const float WalkSeconds = 1.2f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 水盤の前に集めて撮る()
        {
            string folder = System.Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore(DirVariable + " が無いので撮らない");
            }

            string name = System.Environment.GetEnvironmentVariable(NameVariable);
            if (string.IsNullOrEmpty(name))
            {
                name = "mob-crowd";
            }

            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            Random.InitState(Seed);
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            cycle.SetHours(CaptureHour);
            cycle.Paused = true;

            MobScheduler scheduler = Object.FindAnyObjectByType<MobScheduler>();
            Assert.IsNotNull(scheduler, "MobScheduler が無い");

            // 時間帯の人数がそろうまで早送りする。
            Time.timeScale = 8f;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline && (scheduler.TargetCount == 0 || scheduler.ActiveCount < scheduler.TargetCount))
            {
                yield return null;
            }

            Time.timeScale = 1f;
            Assert.Greater(scheduler.ActiveCount, 0, "誰も出てこない");

            Assert.IsTrue(scheduler.TryGetWaypoint("place:library_pond", out Vector3 pond), "水盤の点が焼き込まれていない");
            MobSchedule schedule = MobSchedule.LoadFromResources();
            MobSchedule.Band band = schedule.BandAt(CaptureHour);
            Assert.IsTrue(FindPondRoute(band, out MobSchedule.Route route, out int pondIndex), band.Id + " に水盤へ寄る道が無い");

            var walkers = new List<MobWalker>();
            for (int i = 0; i < scheduler.PoolSize; i++)
            {
                MobWalker walker = scheduler.PoolAt(i);
                if (walker != null && walker.IsRunning)
                {
                    walkers.Add(walker);
                }
            }

            Vector3 dir = PickDirection(pond, walkers.Count);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            scheduler.enabled = false;

            var placed = new List<MobWalker>();
            for (int i = 0; i < walkers.Count; i++)
            {
                Vector3 start = StartAt(pond, dir, right, i);
                if (walkers[i].Begin(route, band.Id, 0.7f, pondIndex, start))
                {
                    walkers[i].SetTier(MobTier.Near, false, 1);
                    placed.Add(walkers[i]);
                }
            }

            HidePlayer();
            Camera main = Camera.main;
            Assert.IsNotNull(main, "メインカメラが無い");

            // 本編のカメラはプレイヤーを追うので、同じ設定の撮影用カメラを別に置く。
            var rig = new GameObject("__MobCrowdCamera");
            Camera camera = rig.AddComponent<Camera>();
            camera.CopyFrom(main);
            camera.enabled = false;
            rig.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            Vector3 eye = pond - dir * 1.5f + Vector3.up * 2.1f;
            Vector3 look = pond + dir * (FirstDepth + RowDepth) + Vector3.up * 0.9f;
            rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));

            float walkUntil = Time.time + WalkSeconds;
            while (Time.time < walkUntil)
            {
                yield return null;
            }

            try
            {
                Directory.CreateDirectory(folder);
                Shoot(camera, Path.Combine(folder, name + ".png"));
                File.WriteAllText(Path.Combine(folder, name + ".txt"), Describe(placed, pond, dir, eye), new UTF8Encoding(false));
            }
            finally
            {
                Object.Destroy(rig);
            }

            Assert.Greater(placed.Count, 0, "水盤の前に 1 人も置けなかった");
        }

        private static bool FindPondRoute(MobSchedule.Band band, out MobSchedule.Route found, out int index)
        {
            foreach (MobSchedule.Route route in band.Routes)
            {
                for (int i = 1; i < route.Waypoints.Count; i++)
                {
                    if (route.Waypoints[i].Key == "place:library_pond")
                    {
                        found = route;
                        index = i;
                        return true;
                    }
                }
            }

            found = null;
            index = -1;
            return false;
        }

        /// <summary>立ち位置がいちばん多く NavMesh に載り、水盤の点まで歩ける向きを 30 度刻みで選ぶ。</summary>
        private static Vector3 PickDirection(Vector3 pond, int count)
        {
            var path = new NavMeshPath();
            Vector3 best = Vector3.forward;
            int bestScore = -1;
            for (int deg = 0; deg < 360; deg += 30)
            {
                Vector3 dir = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                int score = 0;
                for (int i = 0; i < count; i++)
                {
                    Vector3 start = StartAt(pond, dir, right, i);
                    if (NavMesh.SamplePosition(start, out NavMeshHit hit, 0.4f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(hit.position, pond, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete)
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = dir;
                }
            }

            return best;
        }

        private static Vector3 StartAt(Vector3 pond, Vector3 dir, Vector3 right, int i)
        {
            int row = i / PerRow;
            int column = i % PerRow;
            float offset = (column - (PerRow - 1) * 0.5f) * Lateral + (row % 2) * Lateral * 0.5f;
            return pond + dir * (FirstDepth + row * RowDepth) + right * offset;
        }

        private static void HidePlayer()
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                return;
            }

            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
        }

        private static void Shoot(Camera camera, string file)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = target, mipLevel = 0 };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                buffer.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                buffer.Apply();
                File.WriteAllBytes(file, buffer.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(buffer);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static string Describe(List<MobWalker> placed, Vector3 pond, Vector3 dir, Vector3 eye)
        {
            var text = new StringBuilder();
            text.Append("pond ").Append(pond).Append(" dir ").Append(dir).Append(" eye ").Append(eye).Append('\n');
            foreach (MobWalker walker in placed)
            {
                text.Append(walker.Slot).Append(' ').Append(walker.VariantId).Append(' ')
                    .Append(walker.transform.position).Append(' ').Append(walker.Tier).Append(' ')
                    .Append(walker.gameObject.activeInHierarchy).Append('\n');
            }

            return text.ToString();
        }
    }
}
