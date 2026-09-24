using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 拾い物を DayStats にそろえ直す (#106)。
    /// キャンパスの中でロードすると DayStats はセーブの時点に戻るが、以前は拾った物を Destroy していたので、
    /// セーブのあとに拾った隠しアイテムはもう拾えず 20/20 が取れなかった。いまは拾った物を隠すだけにして、
    /// ロードで DayStats に合わせて出し直す（SaveSystem.ApplyToScene が <see cref="CollectableItem.SyncAllWithDayStats()"/> を呼ぶ）。
    /// エディットモードでは OnEnable が走らず isActiveAndEnabled が false なので、ここでは拾えるかを
    /// IsTaken と activeSelf で見る。CanInteract（E で拾えるか）は CollectableReloadPlayTests が見る。
    /// </summary>
    public sealed class CollectableReloadTests
    {
        private const string HiddenId = "c_test_reload_hidden";
        private const string OtherHiddenId = "c_test_reload_other";

        /// <summary>一覧に無いクエストの拾い物（牛乳や葉と同じ扱い）。</summary>
        private const string QuestPickupId = "leaf";

        private readonly List<GameObject> _created = new List<GameObject>();

        private static CollectibleCatalog TestCatalog()
        {
            return CollectibleCatalog.Parse(
                "{\"collectibles\":[" +
                "{\"id\":\"" + HiddenId + "\",\"name_ja\":\"テスト\",\"name_en\":\"Test\",\"source\":\"hidden\"," +
                "\"position\":{\"x\":1,\"z\":2}}," +
                "{\"id\":\"" + OtherHiddenId + "\",\"name_ja\":\"テスト2\",\"name_en\":\"Test 2\",\"source\":\"hidden\"," +
                "\"position\":{\"x\":3,\"z\":4}}]}");
        }

        private CollectableItem MakeItem(string itemId)
        {
            var go = new GameObject("CollectableReloadTests " + itemId);
            _created.Add(go);
            CollectableItem item = go.AddComponent<CollectableItem>();
            item.ItemId = itemId;
            return item;
        }

        /// <summary>DayStats をロードしたときと同じ形で戻す（拾った物の id だけ入れる）。</summary>
        private static void RestoreCollected(params string[] collected)
        {
            DayStats.Restore(null, collected, null, null);
        }

        [SetUp]
        public void SetUp()
        {
            DayStats.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
            DayStats.Reset();
        }

        [Test]
        public void TakenAfterSync_HiddenItemFollowsDayStats()
        {
            // 拾ったあとで DayStats が拾う前に戻った → 出し直す。
            Assert.IsFalse(CollectableItem.TakenAfterSync(true, false, true), "拾う前に戻ったのに隠したまま");

            // DayStats に入っている → 隠す（拾っていなくても、拾ったことになっているセーブを読んだとき）。
            Assert.IsTrue(CollectableItem.TakenAfterSync(true, true, false), "拾ったことになっているのに出ている");
            Assert.IsTrue(CollectableItem.TakenAfterSync(true, true, true), "拾ったままのはずが出てくる");

            // どちらでもない → 出したまま。
            Assert.IsFalse(CollectableItem.TakenAfterSync(true, false, false), "拾っていないのに隠れる");
        }

        [Test]
        public void TakenAfterSync_QuestPickupKeepsItsOwnState()
        {
            // 同じ id の物が何個もあるので、DayStats に入っていても、拾っていない 1 個は隠さない。
            Assert.IsFalse(CollectableItem.TakenAfterSync(false, true, false), "別の 1 個を拾っただけで隠れる");

            // 拾った物は出し直さない（ロードで戻ったクエストは QuestSystem が拾った数から進め直すので、出すと二重に数える）。
            Assert.IsTrue(CollectableItem.TakenAfterSync(false, false, true), "クエストの拾い物がロードで出てくる");
        }

        [Test]
        public void Sync_HidesAHiddenItemThatDayStatsHasCollected()
        {
            CollectibleCatalog catalog = TestCatalog();
            CollectableItem item = MakeItem(HiddenId);

            DayStats.NoteCollect(HiddenId);
            item.SyncWithDayStats(catalog);

            Assert.IsTrue(item.IsTaken, "拾ったことになっているのに拾われていない");
            Assert.IsFalse(item.gameObject.activeSelf, "拾ったことになっているのにワールドに出ている");
        }

        [Test]
        public void Sync_AfterRestoringToBeforeThePickup_PutsTheItemBack()
        {
            CollectibleCatalog catalog = TestCatalog();
            CollectableItem item = MakeItem(HiddenId);

            // 拾った（DayStats に入り、ワールドからは隠れた）。
            DayStats.NoteCollect(HiddenId);
            item.SyncWithDayStats(catalog);
            Assert.IsFalse(item.gameObject.activeSelf);

            // 拾う前のセーブを読んだ。DayStats からは消えている。
            RestoreCollected(OtherHiddenId);
            Assert.IsFalse(DayStats.HasCollected(HiddenId));
            item.SyncWithDayStats(catalog);

            Assert.IsFalse(item.IsTaken, "拾う前に戻ったのに拾われたまま（もう拾えない）");
            Assert.IsTrue(item.gameObject.activeSelf, "拾う前に戻ったのにワールドに戻らない");
        }

        [Test]
        public void Sync_ItemStillRecordedInDayStatsStaysHidden()
        {
            CollectibleCatalog catalog = TestCatalog();
            CollectableItem item = MakeItem(HiddenId);

            DayStats.NoteCollect(HiddenId);
            item.SyncWithDayStats(catalog);

            // 拾ったあとのセーブを読んだ。DayStats にはまだ入っている。
            RestoreCollected(HiddenId);
            item.SyncWithDayStats(catalog);

            Assert.IsTrue(item.IsTaken, "拾ったことになっているのに拾えるようになった");
            Assert.IsFalse(item.gameObject.activeSelf, "拾ったことになっているのにワールドに戻った（二重に数えられる）");
        }

        [Test]
        public void Sync_QuestPickupIsNotHiddenBecauseAnotherCopyWasPickedUp()
        {
            CollectibleCatalog catalog = TestCatalog();
            CollectableItem leaf = MakeItem(QuestPickupId);

            // 葉は何枚もあり、DayStats には id しか残らない。1 枚拾っても残りは拾える。
            DayStats.NoteCollect(QuestPickupId);
            leaf.SyncWithDayStats(catalog);

            Assert.IsFalse(leaf.IsTaken, "別の葉を拾っただけで、この葉が拾えなくなった");
            Assert.IsTrue(leaf.gameObject.activeSelf, "別の葉を拾っただけで、この葉が消えた");
        }

        [Test]
        public void SyncAll_FindsHiddenItemsThatAreInactiveAndBringsBackOnlyTheRewoundOnes()
        {
            CollectibleCatalog catalog = TestCatalog();
            CollectableItem rewound = MakeItem(HiddenId);
            CollectableItem kept = MakeItem(OtherHiddenId);

            // 両方拾った。どちらも非アクティブになる。
            DayStats.NoteCollect(HiddenId);
            DayStats.NoteCollect(OtherHiddenId);
            CollectableItem.SyncAllWithDayStats(catalog);
            Assert.IsFalse(rewound.gameObject.activeSelf, HiddenId + " が隠れない");
            Assert.IsFalse(kept.gameObject.activeSelf, OtherHiddenId + " が隠れない");

            // OtherHiddenId だけ拾ったあとのセーブを読んだ。
            RestoreCollected(OtherHiddenId);
            CollectableItem.SyncAllWithDayStats(catalog);

            Assert.IsFalse(rewound.IsTaken, "非アクティブの拾い物を見つけられず、拾う前に戻った物が拾えないまま");
            Assert.IsTrue(rewound.gameObject.activeSelf, "非アクティブの拾い物を見つけられず、拾う前に戻った物がワールドに戻らない");
            Assert.IsTrue(kept.IsTaken, "セーブでも拾ったことになっている物が拾えるようになった");
            Assert.IsFalse(kept.gameObject.activeSelf, "セーブでも拾ったことになっている物がワールドに戻った");
        }
    }
}
