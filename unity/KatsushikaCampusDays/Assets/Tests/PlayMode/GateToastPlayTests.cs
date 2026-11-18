using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 本物のキャンパスで北西の門を歩いて出入りすると、門の一言がトーストに出ること（#41）。
    /// 歩く代わりに 1 フレームに 1 m ずつ移し、WorldBounds が「歩いた」とみなす距離（TeleportDistance）の内側で門をまたぐ。
    /// </summary>
    public sealed class GateToastPlayTests : SceneTestBase
    {
        /// <summary>門の内側・外側の x（m）。西の壁は x = -HalfExtent。</summary>
        private const float InsideX = -WorldBounds.HalfExtent + 6f;
        private const float OutsideX = -WorldBounds.HalfExtent - 6f;

        /// <summary>1 フレームに動かす距離（m）。</summary>
        private const float Step = 1f;

        /// <summary>
        /// トーストが出るまで待つ上限（実時間の秒）。入場の 1.2 秒後に歓迎のトーストが 3 件（1 件約 3.1 秒）並ぶので、
        /// その後ろに門の一言が積まれても間に合う長さにする。
        /// </summary>
        private const float ToastWaitSeconds = 15f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_WalkingThroughTheGate_ShowsLeftAndBackToasts()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(3, 0f);
            Assert.IsFalse(KCDInput.GameplayBlocked, "入場の直後なのに操作が止まっている");

            var bounds = Object.FindAnyObjectByType<WorldBounds>();
            Assert.IsNotNull(bounds, "キャンパスに WorldBounds が無い");
            Assert.Less(bounds.Area.xMin, OutsideX, "歩ける範囲が門の外の道まで広がっていない");

            GameObject tagged = GameObject.FindWithTag("Player");
            Assert.IsNotNull(tagged, "自分の人物が居ない");
            var player = tagged.GetComponent<PlayerController>();
            Assert.IsNotNull(player, "PlayerController が無い");
            Assert.IsNotNull(HUD.Instance, "HUD が無い");
            TMP_Text label = ToastLabel(HUD.Instance.ToastView);

            float z = DormRoute.DefaultGateZ;
            player.Teleport(Ground(InsideX, z), -90f);
            yield return PlayModeScenes.Advance(5, 0f);

            var seen = new List<string>();
            yield return Walk(player, InsideX, OutsideX, z);
            string left = L.Get("ui.hud.left_campus", WorldBounds.LeftCampusFallback);
            yield return Collect(label, seen, left);
            CollectionAssert.Contains(seen, left, "門を出た一言が出ない");

            yield return Walk(player, OutsideX, InsideX, z);
            string back = L.Get("ui.hud.back_on_campus", WorldBounds.BackOnCampusFallback);
            yield return Collect(label, seen, back);
            CollectionAssert.Contains(seen, back, "門から戻った一言が出ない");

            Assert.IsFalse(bounds.IsRecovering, "門の外の道でキャンパスへ戻されている");
            AssertNoErrors("門の出入り");
        }

        private static IEnumerator Walk(PlayerController player, float fromX, float toX, float z)
        {
            float direction = Mathf.Sign(toX - fromX);
            float yaw = direction < 0f ? -90f : 90f;
            for (float x = fromX; direction * (toX - x) >= 0f; x += direction * Step)
            {
                player.Teleport(new Vector3(x, player.transform.position.y, z), yaw);
                yield return null;
            }
        }

        /// <summary>トーストに出た文を seen に集める。expected が出たら止める。</summary>
        private static IEnumerator Collect(TMP_Text label, List<string> seen, string expected)
        {
            float deadline = Time.realtimeSinceStartup + ToastWaitSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                string text = label.text;
                if (!string.IsNullOrEmpty(text) && !seen.Contains(text))
                {
                    seen.Add(text);
                }

                if (text == expected)
                {
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>真上から下へ光線を落として地面の高さを取る（門の内側で 1 回だけ使う）。当たらなければ y = 1。</summary>
        private static Vector3 Ground(float x, float z)
        {
            var origin = new Vector3(x, 60f, z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * 0.1f
                : new Vector3(x, 1f, z);
        }

        private static TMP_Text ToastLabel(ToastView view)
        {
            Assert.IsNotNull(view, "HUD にトーストが無い");
            FieldInfo field = typeof(ToastView).GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "ToastView._label が見つからない（名前が変わった？）");
            var label = field.GetValue(view) as TMP_Text;
            Assert.IsNotNull(label, "トーストの文の欄が無い");
            return label;
        }
    }
}
