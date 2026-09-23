using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// 操作の封鎖（KCDInput.GameplayBlocked）をオーナーごとに持つこと (#40)。
    /// 建物の出入りや落下からの復帰の暗転が終わったときに、その間に始まった会話やポーズの封鎖まで外さない。
    /// </summary>
    public sealed class GameplayBlockTests
    {
        [SetUp]
        public void SetUp()
        {
            KCDInput.ClearAllBlocks();
        }

        [TearDown]
        public void TearDown()
        {
            KCDInput.ClearAllBlocks();
        }

        [Test]
        public void Unblock_OnlyReleasesTheOwnersOwnBlock()
        {
            var interior = new object();
            var dialogue = new object();

            KCDInput.Block(interior);
            KCDInput.Block(dialogue);
            Assert.IsTrue(KCDInput.GameplayBlocked);

            // 暗転が終わって出入り係が外しても、会話の封鎖は残る。
            KCDInput.Unblock(interior);
            Assert.IsTrue(KCDInput.GameplayBlocked, "会話中なのに操作が戻った");
            Assert.IsFalse(KCDInput.IsBlockedBy(interior));
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue));

            KCDInput.Unblock(dialogue);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void AssigningFalse_DoesNotReleaseOwnerBlocks()
        {
            var dialogue = new object();
            KCDInput.Block(dialogue);

            // ポーズやクエストログは閉じるときに GameplayBlocked = false を代入する。それで会話の封鎖は外れない。
            KCDInput.GameplayBlocked = true;
            KCDInput.GameplayBlocked = false;
            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを閉じたら会話中なのに動けるようになった");

            KCDInput.Unblock(dialogue);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void OwnerUnblock_DoesNotReleaseAScreenOpenedDuringTheFade()
        {
            // 落下からの復帰の暗転中に Esc のポーズを開いた。戻し終えても、ポーズを閉じるまでは封鎖したまま
            // （外すとポーズ中に F5/F9 や写真モードが効いてしまう）。
            var bounds = new object();
            KCDInput.Block(bounds);
            KCDInput.GameplayBlocked = true;

            KCDInput.Unblock(bounds);
            Assert.IsTrue(KCDInput.GameplayBlocked, "ポーズを開いたまま封鎖が外れた");

            KCDInput.GameplayBlocked = false;
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void BlockingTwice_IsReleasedByOneUnblock()
        {
            var owner = new object();
            KCDInput.Block(owner);
            KCDInput.Block(owner);

            KCDInput.Unblock(owner);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void UnblockWithoutBlock_AndNullOwners_AreIgnored()
        {
            var other = new object();
            KCDInput.Block(other);

            KCDInput.Unblock(new object());
            KCDInput.Unblock(null);
            KCDInput.Block(null);
            Assert.IsTrue(KCDInput.GameplayBlocked);
            Assert.IsFalse(KCDInput.IsBlockedBy(null));

            KCDInput.Unblock(other);
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }

        [Test]
        public void ClearAllBlocks_ReleasesOwnersAndTheSharedFlag()
        {
            KCDInput.Block(new object());
            KCDInput.GameplayBlocked = true;

            // シーンを切り替えるときだけ使う。
            KCDInput.ClearAllBlocks();
            Assert.IsFalse(KCDInput.GameplayBlocked);
        }
    }
}
