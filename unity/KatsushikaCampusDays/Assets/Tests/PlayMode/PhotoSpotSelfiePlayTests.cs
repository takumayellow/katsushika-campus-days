using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 写真スポットの記念写真 (#170)。6 か所の三脚で E を押すと、キャラが三脚の前へ走って振り向き、
    /// 三脚のカメラが撮った PNG が残る。その写真は一色でなく、キャラのすねから頭までが画角に入り、カメラから胸までを
    /// 壁や木がふさいでいない。構図は撮ったときのカメラの位置と当たり判定で見るので、描画のデバイスが無くても確かめる。
    /// 一色でないことは写真の画素で見るので、描画のデバイスがあるときだけ見る。そのときは撮った写真を unity/logs/selfie/ に
    /// 写すので、目で構図を確かめられる。
    /// </summary>
    public sealed class PhotoSpotSelfiePlayTests : SceneTestBase
    {
        /// <summary>6 か所を回る（屋内は暗転して入る）ので、ほかのシーンのテストより長い。</summary>
        private const int SelfieTimeoutMs = 420000;

        /// <summary>キャラが画角に入っているとみなす範囲（画面の端からこれだけ内側, 0〜1）。</summary>
        private const float ViewportMargin = 0.04f;

        /// <summary>一色でないとみなす輝度のばらつき（標準偏差, 0〜1）。</summary>
        private const float MinLuminanceSpread = 0.03f;

        [UnityTest]
        [Timeout(SelfieTimeoutMs)]
        public IEnumerator Campus_EveryPhotoSpot_SavesASelfieThatShowsThePlayer()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            FormalEnding ending = Object.FindAnyObjectByType<FormalEnding>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsNotNull(cycle, "キャンパスに DayNightCycle が無い");
            Assert.IsNotNull(PhotoSystem.Instance, "キャンパスに PhotoSystem が無い");
            Assert.IsNotNull(PhotoSystem.Instance.CutIn, "PhotoSystem にポラロイドが差し込まれていない（SceneBuilder）");
            Assert.IsNotNull(CollectibleCatalog.Instance, "CollectibleCatalog が読まれていない");

            cycle.Paused = true;
            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance, ending);
            yield return driver.Settle();

            string photoFolder = Path.Combine(SaveDirectory, PhotoSystem.FolderName);
            string reviewFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "selfie"));
            if (CanRender)
            {
                Directory.CreateDirectory(reviewFolder);
            }
            else
            {
                // 描画のデバイスが無いと、三脚のカメラは描かずに塗っただけの写真を返す（SelfieCamera）。
                // 目で見るための絵を、その写真で上書きしない。
                Debug.Log("[KCD] 描画のデバイスが無い（-nographics）ので、写真が一色でないかは見ず、unity/logs/selfie/ にも写さない");
            }

            var bad = new List<string>();
            foreach (CatalogPhotoSpot spot in CollectibleCatalog.Instance.PhotoSpots)
            {
                PhotoSpot tripod = Object.FindObjectsByType<PhotoSpot>(FindObjectsInactive.Include)
                    .FirstOrDefault(t => t.SpotId == spot.Id);
                if (tripod == null)
                {
                    bad.Add(spot.Id + ": 三脚がシーンに無い");
                    continue;
                }

                string[] before = Files(photoFolder);
                yield return driver.Photo(spot.Id);
                string[] added = Files(photoFolder).Except(before).ToArray();
                if (added.Length != 1)
                {
                    bad.Add(spot.Id + ": 撮った PNG が " + added.Length + " 枚（1 枚のはず）");
                    continue;
                }

                if (CanRender)
                {
                    File.Copy(added[0], Path.Combine(reviewFolder, spot.Id + ".png"), true);
                }

                CheckShot(spot.Id, added[0], bad);
                CheckFraming(spot, tripod.transform.position, player, bad);
                Assert.IsFalse(KCDInput.IsBlockedBy(PhotoSystem.Instance), spot.Id + " の撮影のあと操作の封鎖が残っている");
                Assert.IsFalse(player.AutopilotActive, spot.Id + " の撮影のあとプレイヤーが走らされたまま");
            }

            bad.AddRange(driver.Problems);
            Assert.IsEmpty(bad, string.Join("\n", bad) + "\n写真: " + reviewFolder);
            AssertNoErrors("写真スポットの記念写真");
        }

        private static string[] Files(string folder)
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png") : new string[0];
        }

        /// <summary>PNG が読めて、大きさが合い、一色（描けていない・真っ黒）でない。一色でないかは、描画のデバイスがあるときだけ見る。</summary>
        private static void CheckShot(string spotId, string path, List<string> bad)
        {
            var image = new Texture2D(2, 2);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path)))
                {
                    bad.Add(spotId + ": PNG が読めない");
                    return;
                }

                if (image.width != SelfieCamera.Width || image.height != SelfieCamera.Height)
                {
                    bad.Add(spotId + ": 大きさが " + image.width + "x" + image.height);
                }

                if (!CanRender)
                {
                    return;
                }

                Color32[] pixels = image.GetPixels32();
                double sum = 0;
                double squares = 0;
                int n = 0;
                for (int i = 0; i < pixels.Length; i += 53)
                {
                    Color32 p = pixels[i];
                    double y = (0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b) / 255.0;
                    sum += y;
                    squares += y * y;
                    n++;
                }

                double mean = sum / n;
                double spread = System.Math.Sqrt(System.Math.Max(0.0, squares / n - mean * mean));
                if (spread < MinLuminanceSpread)
                {
                    bad.Add(string.Format("{0}: ほぼ一色（輝度の平均 {1:F3}、ばらつき {2:F3}）", spotId, mean, spread));
                }
            }
            finally
            {
                Object.Destroy(image);
            }
        }

        /// <summary>
        /// 撮ったときの構図（PhotoSystem.LastSelfie）で、プレイヤーのすね（背丈の 2 割）と頭の天辺が画角に入ること、
        /// カメラから胸までの間をプレイヤー以外の当たり判定がふさいでいないことを見る。
        /// </summary>
        private static void CheckFraming(CatalogPhotoSpot spot, Vector3 tripod, PlayerController player, List<string> bad)
        {
            SelfiePose? last = PhotoSystem.Instance.LastSelfie;
            if (last == null)
            {
                bad.Add(spot.Id + ": 構図が残っていない");
                return;
            }

            SelfiePose pose = last.Value;
            Vector3 position = pose.Position;
            Quaternion rotation = pose.Rotation;
            Vector3 feet = player.transform.position;
            float run = Vector3.Distance(Flat(feet), Flat(spot.Selfie.StandPoint(tripod, spot.LookDir, pose.Turn)));
            Debug.Log(string.Format(
                "[KCD] 記念写真 {0}: 立ち位置を {5:F0} 度回した、立ち位置まで残り {1:F2} m、三脚から {2:F2} m、背丈 {3:F2} m、カメラを引いた量 {4:F2} m",
                spot.Id, run, Vector3.Distance(Flat(feet), Flat(tripod)), pose.SubjectHeight, pose.BackOff, pose.Turn));

            float tanY = Mathf.Tan(pose.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanX = tanY * SelfieCamera.Width / SelfieCamera.Height;
            foreach (float height in new[] { pose.SubjectHeight * 0.2f, pose.SubjectHeight })
            {
                Vector3 local = Quaternion.Inverse(rotation) * (feet + Vector3.up * height - position);
                float x = local.z > 0f ? 0.5f + 0.5f * local.x / (local.z * tanX) : -1f;
                float y = local.z > 0f ? 0.5f + 0.5f * local.y / (local.z * tanY) : -1f;
                if (x < ViewportMargin || x > 1f - ViewportMargin || y < ViewportMargin || y > 1f - ViewportMargin)
                {
                    bad.Add(string.Format("{0}: 足もと +{1:F2} m が画角の外（{2:F2}, {3:F2}）", spot.Id, height, x, y));
                }
            }

            Vector3 chest = feet + Vector3.up * (pose.SubjectHeight * 0.7f);
            foreach (RaycastHit hit in Physics.RaycastAll(position, chest - position, Vector3.Distance(position, chest),
                         ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(player.transform))
                {
                    bad.Add(string.Format("{0}: カメラと胸の間を {1} がふさいでいる（カメラから {2:F2} m）",
                        spot.Id, hit.collider.name, hit.distance));
                }
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}
