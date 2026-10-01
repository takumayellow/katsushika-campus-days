using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 「E で調べる」の選び方と受け付け（InteractionPrompt / InteractionPromptView / Interactable）。
    /// 背中側や射程の外を拾わないこと、近くて正面のものを選ぶこと、封鎖中とモーダルを閉じたフレームの E を捨てること、
    /// 押しても調べられないときは案内を出さないこと。
    /// </summary>
    public sealed class InteractionPromptTests
    {
        private const float Tolerance = 0.0001f;
        private const float FacingDot = 0.15f;
        private static readonly Vector3 Forward = Vector3.forward;

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            KCDInput.ClearAllBlocks();
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
            KCDInput.ClearAllBlocks();
        }

        private static bool Score(Vector3 target, float range, out float score)
        {
            return InteractionPrompt.TryScore(Vector3.zero, Forward, target, range, FacingDot, out score);
        }

        private static Vector3 AtAngle(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        // ---- 候補の点数 ----

        [Test]
        public void TryScore_InFrontWithinRange_IsACandidate()
        {
            Assert.IsTrue(Score(new Vector3(0f, 0f, 1.5f), 2.2f, out float score));
            Assert.AreEqual(0.5f, score, Tolerance, "点数は 距離 − 正面度");
        }

        [Test]
        public void TryScore_Behind_IsNotACandidate()
        {
            Assert.IsFalse(Score(new Vector3(0f, 0f, -1f), 2.2f, out _), "背中側のものを拾っている");
        }

        [Test]
        public void TryScore_BesideAtRightAngle_IsNotACandidate()
        {
            Assert.IsFalse(Score(new Vector3(1f, 0f, 0f), 2.2f, out _), "真横のものを拾っている");
        }

        [Test]
        public void TryScore_SlightlyToTheSide_IsStillACandidate()
        {
            Assert.IsTrue(Score(AtAngle(60f, 1f), 2.2f, out float score), "斜め前のものを拾えない");
            Assert.AreEqual(1f - 0.5f, score, Tolerance);
        }

        [Test]
        public void TryScore_RangeIsInclusive()
        {
            Assert.IsTrue(Score(new Vector3(0f, 0f, 2f), 2f, out _), "ちょうど射程のものを拾えない");
            Assert.IsFalse(Score(new Vector3(0f, 0f, 2.01f), 2f, out _), "射程の外のものを拾っている");
        }

        [Test]
        public void TryScore_IgnoresHeight()
        {
            // 棚の上や階段の下にあるものも、平面で近ければ調べられる。
            Assert.IsTrue(Score(new Vector3(0f, 5f, 1f), 2.2f, out float score), "高さの差で射程の外になっている");
            Assert.AreEqual(0f, score, Tolerance);
        }

        [Test]
        public void TryScore_RightUnderfoot_CountsAsInFront()
        {
            // 平面で 5 cm 未満なら向きは見ない（ほぼ同じ位置では向きが決まらない）。
            Assert.IsTrue(InteractionPrompt.TryScore(Vector3.zero, Vector3.back, new Vector3(0.01f, 0f, 0.01f), 2.2f,
                FacingDot, out float score), "足もとのものを拾えない");
            Assert.Less(score, 0f);
        }

        [Test]
        public void TryScore_PrefersTheNearerAndMoreFrontal()
        {
            Score(new Vector3(0f, 0f, 1f), 2.2f, out float ahead);
            Score(AtAngle(60f, 0.8f), 2.2f, out float nearButAside);
            Score(new Vector3(0f, 0f, 2f), 2.2f, out float farAhead);

            Assert.Less(ahead, nearButAside, "正面の 1 m より斜め 60° の 0.8 m を選んでいる");
            Assert.Less(ahead, farAhead, "正面の 1 m より正面の 2 m を選んでいる");
        }

        // ---- E の受け付け ----

        [TestCase(true, true, false, false, true, TestName = "ShouldInteract_対象があって押した")]
        [TestCase(false, true, false, false, false, TestName = "ShouldInteract_対象が無い")]
        [TestCase(true, false, false, false, false, TestName = "ShouldInteract_押していない")]
        [TestCase(true, true, true, false, false, TestName = "ShouldInteract_会話やメニューで封鎖中")]
        [TestCase(true, true, false, true, false, TestName = "ShouldInteract_モーダルを閉じたフレーム")]
        public void ShouldInteract_OnlyWhenEverythingAllows(bool hasTarget, bool pressed, bool blocked, bool modalClosed, bool expected)
        {
            Assert.AreEqual(expected, InteractionPrompt.ShouldInteract(hasTarget, pressed, blocked, modalClosed));
        }

        [Test]
        public void ShouldInteract_FollowsTheBlock()
        {
            var dialogue = new object();
            KCDInput.Block(dialogue);
            Assert.IsFalse(InteractionPrompt.ShouldInteract(true, true, KCDInput.GameplayBlocked, false),
                "会話中の E で別の物を調べている");

            KCDInput.Unblock(dialogue);
            Assert.IsTrue(InteractionPrompt.ShouldInteract(true, true, KCDInput.GameplayBlocked, false),
                "会話を閉じたあとも調べられない");
        }

        // ---- 案内（InteractionPromptView）----

        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, false)]
        public void PromptView_IsVisible_OnlyWhenItCanBeUsed(bool hasTarget, bool blocked, bool expected)
        {
            Assert.AreEqual(expected, InteractionPromptView.IsVisible(hasTarget, blocked));
        }

        [Test]
        public void PromptView_LabelFor_PutsTheKeyInFront()
        {
            Assert.AreEqual("[E] 話す", InteractionPromptView.LabelFor("話す"));
        }

        [Test]
        public void PromptView_LabelFor_UsesTheConfiguredVerb()
        {
            var go = new GameObject("Prop");
            _created.Add(go);
            PropInteractable prop = go.AddComponent<PropInteractable>();

            Assert.AreEqual("[E] 調べる", InteractionPromptView.LabelFor(prop.PromptLabel), "既定の動詞が変わっている");

            prop.Configure("読む", "board_read", string.Empty);
            Assert.AreEqual("[E] 読む", InteractionPromptView.LabelFor(prop.PromptLabel));
            Assert.AreEqual("board_read", prop.FlagId);
        }

        // ---- 反応距離（Interactable）----

        [Test]
        public void InteractionRange_HasAFloor()
        {
            var go = new GameObject("Prop");
            _created.Add(go);
            PropInteractable prop = go.AddComponent<PropInteractable>();

            Assert.AreEqual(2.2f, prop.InteractionRange, Tolerance, "既定の反応距離が変わっている");

            prop.InteractionRange = 0f;
            Assert.AreEqual(0.2f, prop.InteractionRange, Tolerance, "反応距離 0 で調べられなくなる");

            prop.InteractionRange = -3f;
            Assert.AreEqual(0.2f, prop.InteractionRange, Tolerance);

            prop.InteractionRange = 3.5f;
            Assert.AreEqual(3.5f, prop.InteractionRange, Tolerance);
        }
    }
}
