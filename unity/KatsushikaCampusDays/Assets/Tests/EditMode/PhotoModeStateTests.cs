using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// フォトモードに入ると時間が止まり、操作が封鎖され、ゲーム中の UI が隠れる。抜けると入る前の状態に戻る (#12)。
    /// </summary>
    public sealed class PhotoModeStateTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        private float _originalTimeScale;
        private bool _originalPhotoMode;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _originalPhotoMode = KCDInput.PhotoMode;
            KCDInput.ClearAllBlocks();
            KCDInput.PhotoMode = false;
            Time.timeScale = 1f;
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
            KCDInput.PhotoMode = _originalPhotoMode;
            Time.timeScale = _originalTimeScale;
        }

        [Test]
        public void Enter_StopsTimeBlocksInputAndHidesTheUi()
        {
            GameObject ui = Ui(true);
            var state = new PhotoModeState();

            state.Enter(ui);

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "時間が止まっていない");
            Assert.IsTrue(KCDInput.GameplayBlocked, "移動や会話の操作が通る");
            Assert.IsTrue(KCDInput.IsBlockedBy(state));
            Assert.IsTrue(KCDInput.PhotoMode);
            Assert.IsFalse(ui.activeSelf, "ゲーム中の UI が写る");
        }

        [Test]
        public void Exit_RestoresTimeInputAndUi()
        {
            GameObject ui = Ui(true);
            var state = new PhotoModeState();

            state.Enter(ui);
            state.Exit();

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "時間が止まったまま");
            Assert.IsFalse(KCDInput.GameplayBlocked, "操作が封鎖されたまま");
            Assert.IsFalse(KCDInput.PhotoMode);
            Assert.IsTrue(ui.activeSelf, "ゲーム中の UI が戻らない");
        }

        [Test]
        public void Exit_RestoresTheTimeScaleFromBeforeEntering()
        {
            Time.timeScale = 0.5f;
            var state = new PhotoModeState();

            state.Enter(Ui(true));
            Assert.AreEqual(0f, Time.timeScale, 1e-6f);

            state.Exit();
            Assert.AreEqual(0.5f, Time.timeScale, 1e-6f, "入る前の速さ（1 ではなく 0.5）に戻す");
        }

        [Test]
        public void Exit_KeepsAUiThatWasHiddenBeforeHidden()
        {
            GameObject ui = Ui(false);
            var state = new PhotoModeState();

            state.Enter(ui);
            Assert.IsFalse(ui.activeSelf);

            state.Exit();
            Assert.IsFalse(ui.activeSelf, "入る前から隠れていた UI を出してしまった");
        }

        [Test]
        public void Exit_KeepsOtherOwnersBlocks()
        {
            var dialogue = new object();
            var state = new PhotoModeState();

            state.Enter(Ui(true));
            KCDInput.Block(dialogue);
            state.Exit();

            Assert.IsTrue(KCDInput.GameplayBlocked, "ほかの画面の封鎖まで外した");
            Assert.IsTrue(KCDInput.IsBlockedBy(dialogue));
            Assert.IsFalse(KCDInput.IsBlockedBy(state));
        }

        [Test]
        public void Enter_TwiceCountsAsOnce()
        {
            GameObject ui = Ui(true);
            var state = new PhotoModeState();

            state.Enter(ui);
            state.Enter(ui);
            state.Exit();

            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "2 回目で止まった速さ（0）を覚え直した");
            Assert.IsFalse(KCDInput.GameplayBlocked);
            Assert.IsTrue(ui.activeSelf, "2 回目で隠れた状態を覚え直した");
        }

        [Test]
        public void Exit_WithoutEnterChangesNothing()
        {
            GameObject ui = Ui(true);
            Time.timeScale = 0.7f;
            KCDInput.GameplayBlocked = true;
            var state = new PhotoModeState();

            state.Exit();

            Assert.AreEqual(0.7f, Time.timeScale, 1e-6f);
            Assert.IsTrue(KCDInput.GameplayBlocked, "入っていないのに封鎖を外した");
            Assert.IsTrue(ui.activeSelf);
        }

        [Test]
        public void Release_RestoresTimeAndBlockButLeavesUiAndPhotoMode()
        {
            // シーンを閉じるとき（OnDisable）の片付け。UI は破棄の途中かもしれないので触らず、PhotoMode は OnDestroy に任せる。
            GameObject ui = Ui(true);
            var state = new PhotoModeState();

            state.Enter(ui);
            state.Release();

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "シーンを閉じても時間が止まったまま");
            Assert.IsFalse(KCDInput.IsBlockedBy(state));
            Assert.IsFalse(KCDInput.GameplayBlocked);
            Assert.IsTrue(KCDInput.PhotoMode);
            Assert.IsFalse(ui.activeSelf);

            state.Exit();
            Assert.IsFalse(ui.activeSelf, "片付けたあとの Exit は何もしない");
        }

        [Test]
        public void Enter_WithoutUiStillStopsAndRestoresTime()
        {
            var state = new PhotoModeState();

            state.Enter(null);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f);
            Assert.IsTrue(KCDInput.GameplayBlocked);

            state.Exit();
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            Assert.IsFalse(KCDInput.GameplayBlocked);
            Assert.IsFalse(KCDInput.PhotoMode);
        }

        private GameObject Ui(bool active)
        {
            var go = new GameObject("Gameplay");
            _created.Add(go);
            go.SetActive(active);
            return go;
        }
    }
}
