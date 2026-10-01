using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// キャンパスの中でロードしたとき（F9 / ポーズの「ロード」）、セーブのあとに拾った隠しアイテムがワールドに戻る (#106)。
    /// 一日の記録（DayStats）だけがセーブの時点に戻り、拾った物は消えたままだと、その宝石はもう拾えず 20/20 が取れない。
    /// F9（CampusDirector）もポーズの「ロード」（PauseMenu）も SaveSystem.Load を呼ぶだけなので、ここではそれを直接呼ぶ。
    /// </summary>
    public sealed class CollectableReloadPlayTests : SceneTestBase
    {
        /// <summary>正門のキーホルダー。屋外にあり、クエストの対象でもない（拾ってもクエストの達成で自動セーブが走らない）。</summary>
        private const string ItemId = "c_gate_keyring";

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_LoadAfterPickingUp_PutsTheHiddenItemBackAndItCountsAgain()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            CollectibleCatalog catalog = CollectibleCatalog.Instance;
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsTrue(catalog.IsHidden(ItemId), ItemId + " が収集物の一覧で隠しアイテムになっていない");

            CollectableItem item = FindPickable(ItemId);
            Assert.IsNotNull(item, "はじめからなのに " + ItemId + " が拾える状態でシーンに無い");
            int before = DayStats.HiddenCollectedCount(catalog);
            Assert.IsFalse(DayStats.HasCollected(ItemId), "はじめからなのにもう拾ったことになっている");

            // 1. 拾う前にセーブする（F5）。入場のときに頼まれていた自動セーブは先に捨てる。
            AutoSave.Cancel();
            Assert.IsTrue(SaveSystem.Save(), "セーブを書けない");

            // 2. 拾う。拾っても自動セーブは走らないので、ファイルは拾う前のまま。
            item.Interact(player.gameObject);
            yield return null;

            Assert.IsTrue(DayStats.HasCollected(ItemId), "拾ったのに DayStats に入っていない");
            Assert.AreEqual(before + 1, DayStats.HiddenCollectedCount(catalog), "拾っても隠しアイテムの数が増えない");
            Assert.IsNull(FindPickable(ItemId), "拾ったのにまだ拾える");

            SaveData onDisk = SaveSystem.Read();
            Assert.IsNotNull(onDisk, "書いたセーブを読めない");
            CollectionAssert.DoesNotContain(onDisk.Collected, ItemId,
                "拾ったときにセーブが書き直された（拾う前のセーブを読む手順にならない）");

            // 3. ロードする（F9 / ポーズの「ロード」と同じ呼び出し）。
            Assert.IsTrue(SaveSystem.Load(), "セーブを読めない");
            yield return null;

            Assert.IsFalse(DayStats.HasCollected(ItemId), "ロードしても DayStats が拾う前に戻らない");
            Assert.AreEqual(before, DayStats.HiddenCollectedCount(catalog), "ロードしても隠しアイテムの数が拾う前に戻らない");

            CollectableItem back = FindPickable(ItemId);
            Assert.IsNotNull(back,
                "ロードで DayStats は拾う前に戻ったのに、" + ItemId + " がワールドに戻らない（もう拾えないので 20/20 が取れない）");
            AssertEveryHiddenItemIsPickableOrCollected(catalog, "ロードの直後");

            // 4. もう一度拾うと、隠しアイテムの数が 1 増える。
            back.Interact(player.gameObject);
            yield return null;

            Assert.IsTrue(DayStats.HasCollected(ItemId), "ロードのあとに拾い直しても DayStats に入らない");
            Assert.AreEqual(before + 1, DayStats.HiddenCollectedCount(catalog), "ロードのあとに拾い直しても数が 1 増えない");
            Assert.IsNull(FindPickable(ItemId), "拾い直したのにまだ拾える");
            AssertEveryHiddenItemIsPickableOrCollected(catalog, "拾い直したあと");

            AssertNoErrors("拾ってからのロード");
        }

        /// <summary>id の物のうち、いま E で拾えるもの。無ければ null。</summary>
        private static CollectableItem FindPickable(string itemId)
        {
            foreach (CollectableItem item in Object.FindObjectsByType<CollectableItem>(FindObjectsInactive.Exclude))
            {
                if (item != null && item.ItemId == itemId && item.CanInteract)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 隠しアイテムはどれも「ワールドで拾える」か「DayStats で拾ったことになっている」のどちらか一方だけ。
        /// だから拾える数と DayStats.HiddenCollectedCount の合計は、一覧の隠しアイテムの数（20）になる。
        /// </summary>
        private static void AssertEveryHiddenItemIsPickableOrCollected(CollectibleCatalog catalog, string when)
        {
            var pickable = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (CollectableItem item in Object.FindObjectsByType<CollectableItem>(FindObjectsInactive.Exclude))
            {
                if (item != null && item.CanInteract && catalog.IsHidden(item.ItemId))
                {
                    pickable.Add(item.ItemId);
                }
            }

            var bad = new List<string>();
            foreach (CatalogItem entry in catalog.Items)
            {
                if (!entry.IsHidden)
                {
                    continue;
                }

                bool inWorld = pickable.Contains(entry.Id);
                bool collected = DayStats.HasCollected(entry.Id);
                if (inWorld && collected)
                {
                    bad.Add(entry.Id + ": 拾ったことになっているのに、まだワールドで拾える");
                }
                else if (!inWorld && !collected)
                {
                    bad.Add(entry.Id + ": 拾っていないのに、ワールドに拾える物が無い");
                }
            }

            Assert.IsEmpty(bad, when + ":\n" + string.Join("\n", bad));
            Assert.AreEqual(catalog.HiddenCount, pickable.Count + DayStats.HiddenCollectedCount(catalog),
                when + ": 拾える隠しアイテムの数と拾った数の合計が、一覧の隠しアイテムの数にならない");
        }
    }
}
