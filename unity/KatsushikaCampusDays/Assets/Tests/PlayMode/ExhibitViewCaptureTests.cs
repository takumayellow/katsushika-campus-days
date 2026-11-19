using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 展示で来場者が見る場面を本物のキャンパスで撮る。環境変数 KCD_VIEW_CAPTURE_DIR があるときだけ撮る。
    ///   次の行き先（#410）: オリエンテーションを終えた直後の追跡枠とミニマップ。
    ///   迷子の本（#410）: 2 階の閲覧席の机に置いた本と、1 階カウンターの司書。
    ///   図書館の人（#165）: 時刻ごとに水盤のまわりにいる人の数と男女の内訳を数え、昼の画を撮る。
    /// </summary>
    public sealed class ExhibitViewCaptureTests : SceneTestBase
    {
        private const string DirVariable = "KCD_VIEW_CAPTURE_DIR";
        private const string PondKey = "place:library_pond";
        private const float PondRadius = 30f;
        /// <summary>数える長さ（ゲーム内の秒）。遠くに出た人が水盤まで歩いてくる時間を含める。</summary>
        private const float CountSeconds = 120f;
        private const float CountTimeScale = 4f;
        /// <summary>数えるテストの制限時間。4 つの時刻でそれぞれ人がそろうのを待ち、ゲーム内 120 秒を数えるので、場面の読み込みの分に足す。</summary>
        private const int CountTimeoutMs = SceneTestTimeoutMs + 300000;
        private const float ShotHour = 12.5f;
        private const int Width = 1600;
        private const int Height = 900;

        /// <summary>迷子の本・司書を撮るとき、どれだけ離れて立つか（m）。</summary>
        private const float BookShotDistance = 1.6f;
        /// <summary>迷子の本の拾い物の id（Editor の CollectableMeshes.StrayBookId と同じ）。</summary>
        private const string StrayBookId = "stray_book";
        private const float LibrarianShotDistance = 3.4f;

        private static readonly float[] Hours = { 9.5f, 12.5f, 14.75f, 17f };

        /// <summary>男の体。ほかの体（sora / inari / kaname / mirai）は女として数え、体の分からない人は別に数える。</summary>
        private static readonly HashSet<string> MaleBodies = new HashSet<string> { "student_m", "prof" };

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator オリエンテーションのあとの次の行き先を撮る()
        {
            string folder = Folder();
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            cycle.Paused = true;

            CampusDriver driver = NewDriver();
            QuestSystem quests = GameManager.Instance.Quests;
            yield return driver.Settle();
            yield return driver.Visit("gate_main");
            yield return driver.Visit("campus_mall");
            yield return driver.Talk("prof");
            yield return driver.Settle();
            yield return PlayModeScenes.Advance(60, 1.5f);

            Assert.IsTrue(quests.IsCompleted("q_orientation"), "オリエンテーションが終わらない: " + string.Join(" / ", driver.Problems));

            QuestData offered = quests.OfferedQuest;
            Directory.CreateDirectory(folder);
            Shoot(Path.Combine(folder, "next-destination.png"));
            {
                var text = new StringBuilder();
                text.Append("offered ").Append(offered != null ? offered.Id : "(none)").Append('\n');
                text.Append("tracked ").Append(quests.TrackedQuest != null ? quests.TrackedQuest.Id : "(none)").Append('\n');
                if (offered != null)
                {
                    text.Append("line ").Append(QuestTrackerView.OfferLine(offered)).Append('\n');
                }

                File.WriteAllText(Path.Combine(folder, "next-destination.txt"), text.ToString(), new UTF8Encoding(false));
            }

            Assert.IsNotNull(offered, "オリエンテーションのあとに次の行き先が出ない");
        }

        [UnityTest]
        [Timeout(CountTimeoutMs)]
        public IEnumerator 図書館の水盤のまわりの人を数えて撮る()
        {
            string folder = Folder();
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            MobScheduler scheduler = Object.FindAnyObjectByType<MobScheduler>();
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            Assert.IsNotNull(scheduler, "MobScheduler が無い");
            Assert.IsNotNull(player, "PlayerController が無い");
            Assert.IsTrue(scheduler.TryGetWaypoint(PondKey, out Vector3 pond), "水盤の点が焼き込まれていない");

            var bodies = new Dictionary<string, string>();
            foreach (MobSchedule.Variant variant in MobSchedule.LoadFromResources().Variants)
            {
                bodies[variant.Id] = variant.BodyId;
            }

            var report = new StringBuilder();
            report.Append("pond ").Append(pond).Append(" radius ").Append(PondRadius).Append("m\n");
            // 人はプレイヤーのまわりに出るので、来場者が水盤を見ている状態で数える。
            player.Teleport(pond + new Vector3(0f, 0f, -12f), 0f);
            yield return PlayModeScenes.Advance(20, 1f);

            int nearTotal = 0;
            foreach (float hour in Hours)
            {
                cycle.SetHours(hour);
                cycle.Paused = true;

                Time.timeScale = 8f;
                float deadline = Time.realtimeSinceStartup + 30f;
                while (Time.realtimeSinceStartup < deadline && (scheduler.TargetCount == 0 || scheduler.ActiveCount < scheduler.TargetCount))
                {
                    yield return null;
                }

                Time.timeScale = CountTimeScale;

                // 歩かせながら、水盤のまわりに来た人を数える（同じ人は 1 回）。
                // 枠は使い回されるので、枠ごとに何人目かを数えて (枠, 何人目) で人を区別する。
                var seen = new Dictionary<(int, int), string>();
                var generation = new int[scheduler.PoolSize];
                var running = new bool[scheduler.PoolSize];
                int peak = 0;
                float until = Time.time + CountSeconds;
                while (Time.time < until)
                {
                    int now = 0;
                    for (int i = 0; i < scheduler.PoolSize; i++)
                    {
                        MobWalker walker = scheduler.PoolAt(i);
                        bool isRunning = walker != null && walker.IsRunning;
                        if (isRunning && !running[i])
                        {
                            generation[i]++;
                        }

                        running[i] = isRunning;
                        if (!isRunning)
                        {
                            continue;
                        }

                        Vector3 p = walker.transform.position;
                        if (new Vector2(p.x - pond.x, p.z - pond.z).magnitude <= PondRadius)
                        {
                            now++;
                            seen[(i, generation[i])] = walker.VariantId;
                        }
                    }

                    peak = Mathf.Max(peak, now);
                    yield return null;
                }

                Time.timeScale = 1f;

                int male = 0;
                int unknown = 0;
                foreach (string variantId in seen.Values)
                {
                    if (variantId == null || !bodies.TryGetValue(variantId, out string body))
                    {
                        unknown++;
                    }
                    else if (MaleBodies.Contains(body))
                    {
                        male++;
                    }
                }

                nearTotal += seen.Count;
                report.Append(hour.ToString("0.00")).Append("h active ").Append(scheduler.ActiveCount)
                    .Append('/').Append(scheduler.TargetCount).Append(" near ").Append(seen.Count)
                    .Append(" peak ").Append(peak).Append(" male ").Append(male)
                    .Append(" female ").Append(seen.Count - male - unknown).Append(" unknown ").Append(unknown).Append('\n');

                if (Mathf.Approximately(hour, ShotHour))
                {
                    Directory.CreateDirectory(folder);
                    Shoot(Path.Combine(folder, "library-pond.png"));
                }
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "library-pond.txt"), report.ToString(), new UTF8Encoding(false));
            Assert.Greater(nearTotal, 0, "どの時刻にも水盤のまわりに誰も来ない");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator 迷子の本と司書を撮る()
        {
            string folder = Folder();
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            cycle.Paused = true;

            CampusDriver driver = NewDriver();
            yield return driver.Settle();
            yield return driver.GoTo("library");
            Assert.AreEqual("library", InteriorLoader.Instance.CurrentId, "図書館に入れない: " + string.Join(" / ", driver.Problems));

            CollectableItem book = null;
            foreach (CollectableItem item in Object.FindObjectsByType<CollectableItem>(FindObjectsInactive.Include))
            {
                if (item.ItemId == StrayBookId)
                {
                    book = item;
                }
            }

            NPCTalker librarian = null;
            foreach (NPCTalker npc in Object.FindObjectsByType<NPCTalker>(FindObjectsInactive.Include))
            {
                if (npc.NpcId == "librarian")
                {
                    librarian = npc;
                }
            }

            Assert.IsNotNull(book, "閲覧席に迷子の本が無い");
            Assert.IsNotNull(librarian, "カウンターに司書が居ない");

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Directory.CreateDirectory(folder);
            var text = new StringBuilder();

            // 本は机の上。机の手前（-z）に立って本の方を向く。
            Vector3 bookAt = book.transform.position;
            player.Teleport(new Vector3(bookAt.x, bookAt.y - 0.74f, bookAt.z - BookShotDistance), 0f);
            yield return PlayModeScenes.Advance(30, 1f);
            Shoot(Path.Combine(folder, "stray-book.png"));
            text.Append("book ").Append(bookAt.ToString("F2")).Append(" interact ").Append(book.CanInteract).Append('\n');

            // 司書はカウンターの内側。客の側（+x）から司書の方を向く。
            Vector3 deskAt = librarian.transform.position;
            player.Teleport(new Vector3(deskAt.x + LibrarianShotDistance, deskAt.y, deskAt.z), -90f);
            yield return PlayModeScenes.Advance(30, 1f);
            Shoot(Path.Combine(folder, "librarian.png"));
            text.Append("librarian ").Append(deskAt.ToString("F1")).Append(" yaw ")
                .Append(librarian.transform.eulerAngles.y.ToString("F0")).Append(" interact ").Append(librarian.CanInteract).Append('\n');

            File.WriteAllText(Path.Combine(folder, "stray-book.txt"), text.ToString(), new UTF8Encoding(false));
            Assert.IsTrue(book.CanInteract, "迷子の本を拾えない");
            Assert.IsTrue(librarian.CanInteract, "司書に話しかけられない");
            AssertNoErrors("迷子の本の場面");
        }

        /// <summary>
        /// 本編のカメラの画を HUD ごと PNG にする。batchmode には画面が無いので、カメラを RenderTexture に描き、
        /// 画面に重ねる UI（Overlay のキャンバス）は撮る間だけそのカメラに載せる。
        /// </summary>
        private static void Shoot(string file)
        {
            Camera main = Camera.main;
            Assert.IsNotNull(main, "メインカメラが無い");
            var overlays = new List<Canvas>();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.isActiveAndEnabled)
                {
                    overlays.Add(canvas);
                }
            }

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                foreach (Canvas canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = main;
                    canvas.planeDistance = main.nearClipPlane + 0.05f;
                }

                Canvas.ForceUpdateCanvases();
                var request = new RenderPipeline.StandardRequest { destination = target, mipLevel = 0 };
                RenderPipeline.SubmitRenderRequest(main, request);
                RenderPipeline.SubmitRenderRequest(main, request);
                RenderTexture.active = target;
                buffer.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                buffer.Apply();
                File.WriteAllBytes(file, buffer.EncodeToPNG());
            }
            finally
            {
                foreach (Canvas canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.worldCamera = null;
                }

                RenderTexture.active = previous;
                Object.Destroy(buffer);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static string Folder()
        {
            string folder = System.Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore(DirVariable + " が無いので撮らない");
            }

            return folder;
        }

        private static CampusDriver NewDriver()
        {
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            FormalEnding ending = Object.FindAnyObjectByType<FormalEnding>();
            Assert.IsNotNull(player, "PlayerController が無い");
            Assert.IsNotNull(ending, "FormalEnding が無い");
            Assert.IsNotNull(InteriorLoader.Instance, "InteriorLoader が無い");
            Assert.IsNotNull(DialogueSystem.Instance, "DialogueSystem が無い");
            return new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance, ending);
        }
    }
}
