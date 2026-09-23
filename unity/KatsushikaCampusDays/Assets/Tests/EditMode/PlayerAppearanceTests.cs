using System;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルで選んだキャラクターが本編の体に反映されるか (#6)。
    /// 体は 3 体ともシーンに焼き込んであり、PlayerAppearance が 1 体だけを出す。
    /// </summary>
    public sealed class PlayerAppearanceTests
    {
        private static readonly string[] Ids = { "mirai", "botchan", "madonna" };

        private GameObject _player;
        private GameObject[] _bodies;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("Player");
            _bodies = new GameObject[Ids.Length];
            for (int i = 0; i < Ids.Length; i++)
            {
                _bodies[i] = new GameObject("Body_" + Ids[i]);
                _bodies[i].transform.SetParent(_player.transform, false);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                UnityEngine.Object.DestroyImmediate(_player);
            }
        }

        private PlayerAppearance Bound()
        {
            PlayerAppearance appearance = _player.AddComponent<PlayerAppearance>();
            appearance.Bind(Ids, _bodies);
            return appearance;
        }

        [Test]
        public void Apply_ShowsOnlyTheChosenBody()
        {
            PlayerAppearance appearance = Bound();

            appearance.Apply("botchan");

            Assert.IsFalse(_bodies[0].activeSelf, "選ばれていない mirai は消す");
            Assert.IsTrue(_bodies[1].activeSelf, "選んだ botchan だけを出す");
            Assert.IsFalse(_bodies[2].activeSelf, "選ばれていない madonna は消す");
            Assert.AreEqual("botchan", appearance.ActiveCharacterId);
        }

        [Test]
        public void Apply_SwitchingAgainLeavesExactlyOneBody()
        {
            PlayerAppearance appearance = Bound();

            appearance.Apply("botchan");
            appearance.Apply("madonna");

            Assert.IsFalse(_bodies[0].activeSelf);
            Assert.IsFalse(_bodies[1].activeSelf, "前に出していた体は消す");
            Assert.IsTrue(_bodies[2].activeSelf);
            Assert.AreEqual("madonna", appearance.ActiveCharacterId);
        }

        [Test]
        public void Apply_UnknownIdKeepsTheFirstBody()
        {
            PlayerAppearance appearance = Bound();

            appearance.Apply("nobody");

            Assert.IsTrue(_bodies[0].activeSelf, "知らない id では最初の体を残す（真っ暗な主人公を出さない）");
            Assert.IsFalse(_bodies[1].activeSelf);
            Assert.IsFalse(_bodies[2].activeSelf);
            Assert.AreEqual("mirai", appearance.ActiveCharacterId);
        }

        [Test]
        public void Apply_EmptyIdKeepsTheFirstBody()
        {
            PlayerAppearance appearance = Bound();

            appearance.Apply(string.Empty);

            Assert.IsTrue(_bodies[0].activeSelf);
            Assert.IsFalse(_bodies[1].activeSelf);
            Assert.IsFalse(_bodies[2].activeSelf);
        }

        [Test]
        public void Apply_WithoutBodiesDoesNotThrow()
        {
            PlayerAppearance appearance = _player.AddComponent<PlayerAppearance>();
            appearance.Bind(Ids, new GameObject[0]);

            Assert.DoesNotThrow(() => appearance.Apply("botchan"));
            Assert.AreEqual(string.Empty, appearance.ActiveCharacterId);
        }

        [Test]
        public void Apply_WithoutBindDoesNotThrow()
        {
            PlayerAppearance appearance = _player.AddComponent<PlayerAppearance>();

            Assert.DoesNotThrow(() => appearance.Apply("botchan"));
            Assert.DoesNotThrow(() => appearance.Bind(null, null));
            Assert.DoesNotThrow(() => appearance.Apply(null));
        }

        [Test]
        public void Apply_WithAnimatorDriverPresentRebindsWithoutThrowing()
        {
            // Apply は実行順に関係なく毎回 PlayerAnimatorDriver.RebindAnimator() を呼ぶ。
            // ドライバがまだ Awake していない（_player が null の）状態でも落ちてはいけない。
            // ここで落ちると、起動時に主人公が固まったまま動かなくなる (#6)。
            _player.AddComponent<PlayerAnimatorDriver>();
            PlayerAppearance appearance = Bound();

            Assert.DoesNotThrow(() => appearance.Apply("madonna"));
            Assert.IsTrue(_bodies[2].activeSelf);
            Assert.AreEqual("madonna", appearance.ActiveCharacterId);
        }

        [Test]
        public void ResolveIndex_MatchesIdAndFallsBackToTheFirstUsableBody()
        {
            Assert.AreEqual(1, PlayerAppearance.ResolveIndex(Ids, _bodies, "botchan"));
            Assert.AreEqual(0, PlayerAppearance.ResolveIndex(Ids, _bodies, "nobody"), "知らない id は先頭");
            Assert.AreEqual(0, PlayerAppearance.ResolveIndex(Ids, _bodies, null));
            Assert.AreEqual(0, PlayerAppearance.ResolveIndex(null, _bodies, "botchan"), "対応表が無くても先頭は出す");
        }

        [Test]
        public void ResolveIndex_NoBodiesGivesMinusOne()
        {
            Assert.AreEqual(-1, PlayerAppearance.ResolveIndex(Ids, null, "mirai"));
            Assert.AreEqual(-1, PlayerAppearance.ResolveIndex(Ids, new GameObject[0], "mirai"));
            Assert.AreEqual(-1, PlayerAppearance.ResolveIndex(Ids, new GameObject[3], "mirai"), "中身が全部 null");
        }

        [Test]
        public void ResolveIndex_SkipsMissingBodies()
        {
            var holed = new[] { null, _bodies[1], _bodies[2] };

            Assert.AreEqual(1, PlayerAppearance.ResolveIndex(Ids, holed, "mirai"),
                "mirai の体が欠けていたら次に使える体へ逃がす");
            Assert.AreEqual(2, PlayerAppearance.ResolveIndex(Ids, holed, "madonna"));
        }

        [Test]
        public void ResolveIndex_ShorterIdTableStillMatchesWhatItCovers()
        {
            var shortIds = new[] { "mirai" };

            Assert.AreEqual(0, PlayerAppearance.ResolveIndex(shortIds, _bodies, "mirai"));
            Assert.AreEqual(0, PlayerAppearance.ResolveIndex(shortIds, _bodies, "madonna"),
                "対応表に無い id は先頭へ逃がす");
        }

        [Test]
        public void PlayableCharacterIds_AreTheThreeTheSceneBakes()
        {
            // ActorFactory.CreateSelectableBodies が焼く体はこの並び。順番が変わると
            // 「知らない id は先頭」のフォールバックで出る顔も変わる。
            CollectionAssert.AreEqual(Ids, GameManager.PlayableCharacterIds);
        }

        [Test]
        public void ExecutionOrder_RunsBeforePlayerAnimatorDriver()
        {
            // 引数なしの GetComponentInChildren は非アクティブな子を拾わないので、
            // PlayerAnimatorDriver.Awake より先に余分な体を消しておきたい。
            // 実行順が狂っても Apply の RebindAnimator が拾い直すが、一手で済む方を既定にしておく。
            var order = Attribute.GetCustomAttribute(
                typeof(PlayerAppearance), typeof(DefaultExecutionOrder)) as DefaultExecutionOrder;
            Assert.IsNotNull(order, "PlayerAppearance に DefaultExecutionOrder が付いていない");

            var driverOrder = Attribute.GetCustomAttribute(
                typeof(PlayerAnimatorDriver), typeof(DefaultExecutionOrder)) as DefaultExecutionOrder;
            int driver = driverOrder != null ? driverOrder.order : 0;
            Assert.Less(order.order, driver, "PlayerAnimatorDriver より先に走らないと体を掴み違える");
        }
    }
}
