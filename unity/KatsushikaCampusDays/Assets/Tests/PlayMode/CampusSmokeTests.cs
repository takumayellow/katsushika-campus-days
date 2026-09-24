using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 「はじめから」でキャンパスへ入り、60 フレーム（かつ 2 秒）進める (#67)。
    /// スポーンが WorldBounds の中で、落ちずに地面に立ち、例外もエラーのログも出ない。
    /// </summary>
    public sealed class CampusSmokeTests : SceneTestBase
    {
        /// <summary>接地を待つ上限（実時間の秒）。スポーンは地面から少し浮かせてあるので、落ちきるまで待つ。</summary>
        private const float GroundedTimeoutSeconds = 3f;

        private const string ResultResourcePath = "KCD/Ending/result";

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_NewGame_SpawnsOnTheGroundInsideWorldBounds()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            WorldBounds bounds = Object.FindAnyObjectByType<WorldBounds>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsNotNull(bounds, "キャンパスに WorldBounds が無い");

            Vector3 spawn = player.transform.position;
            Assert.IsTrue(WorldBounds.IsWithin(spawn, bounds.Area) && spawn.y > bounds.KillY,
                "スポーン " + spawn.ToString("F2") + " が WorldBounds " + bounds.Area + "（KillY " + bounds.KillY + "）の外");

            yield return PlayModeScenes.Advance(60, 2f);

            float deadline = Time.realtimeSinceStartup + GroundedTimeoutSeconds;
            while (!player.IsGrounded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Vector3 now = player.transform.position;
            Assert.IsTrue(player.IsGrounded,
                "60 フレーム + " + GroundedTimeoutSeconds + " 秒たっても接地しない。スポーン " + spawn.ToString("F2") +
                " → いま " + now.ToString("F2") + "。真下: " + DescribeGround(now, player.transform));
            Assert.IsFalse(bounds.IsRecovering,
                "WorldBounds が戻しの最中（落ちたか範囲の外へ出た）。スポーン " + spawn.ToString("F2") + " → いま " + now.ToString("F2"));
            Assert.IsTrue(WorldBounds.IsWithin(now, bounds.Area) && now.y > bounds.KillY,
                "60 フレーム後の位置 " + now.ToString("F2") + " が WorldBounds " + bounds.Area + " の外");
            AssertNoErrors("キャンパス");
        }

        /// <summary>
        /// 入った建物の数（DayStats.NoteEnter）は EntranceTrigger だけが数える。
        /// 入口の建物 id の種類が result.json の totals.buildings と合わないと、建物の点が満点に届かないか、余る。
        /// </summary>
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_EntranceBuildingsMatchResultTotal()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            var ids = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (EntranceTrigger entrance in Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Include))
            {
                if (!string.IsNullOrEmpty(entrance.BuildingId))
                {
                    ids.Add(entrance.BuildingId);
                }
            }

            var asset = Resources.Load<TextAsset>(ResultResourcePath);
            Assert.IsNotNull(asset, "Resources/" + ResultResourcePath + ".json が無い");
            var root = MiniJson.Deserialize(asset.text) as Dictionary<string, object>;
            Assert.IsNotNull(root, ResultResourcePath + " が JSON のオブジェクトでない");
            int total = MiniJson.GetInt(MiniJson.GetObject(root, "totals"), "buildings", -1);

            Assert.AreEqual(total, ids.Count,
                "result.json の totals.buildings と、キャンパスの入口（EntranceTrigger）の建物 id の種類数が違う: " +
                string.Join(", ", ids));
            AssertNoErrors("キャンパス");
        }

        /// <summary>足元から真下へ当たり判定を 3 枚まで並べる。プレイヤー自身の当たり判定は飛ばす。</summary>
        private static string DescribeGround(Vector3 position, Transform player)
        {
            RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 2f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var builder = new StringBuilder();
            int listed = 0;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(player))
                {
                    continue;
                }

                builder.Append(listed == 0 ? string.Empty : " / ")
                    .Append(hit.collider.name).Append(" (y ").Append(hit.point.y.ToString("F2")).Append(')');
                if (++listed == 3)
                {
                    break;
                }
            }

            return listed == 0 ? "60 m 下まで当たり判定が無い（床が無いか、レイヤーが Physics に載っていない）" : builder.ToString();
        }
    }
}
