using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// リザルト画面の開閉と選択 (#67)。開くと時間と操作を止め、選ぶと閉じて戻してから選んだ先へ進む。
    /// 文字の板は渡さない（Redraw は null の板を飛ばす）ので、TextMeshPro の部品もローカライズも要らない。
    /// ただし Bind の引数の型が TMP_Text なので、null を渡すだけでも asmdef に Unity.TextMeshPro の参照が要る。
    /// </summary>
    public sealed class ResultScreenTests
    {
        private GameObject _host;
        private GameObject _root;
        private ResultScreen _screen;

        [SetUp]
        public void SetUp()
        {
            KCDInput.ClearAllBlocks();
            Time.timeScale = 1f;

            _host = new GameObject("ResultScreenForTest");
            _root = new GameObject("ResultRootForTest");
            _root.SetActive(false);
            _screen = _host.AddComponent<ResultScreen>();
            _screen.Bind(_root, null, null, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_screen != null && _screen.IsOpen)
            {
                _screen.Close();
            }

            UnityEngine.Object.DestroyImmediate(_host);
            UnityEngine.Object.DestroyImmediate(_root);
            KCDInput.ClearAllBlocks();
            Time.timeScale = 1f;
        }

        [Test]
        public void Show_StopsTimeAndBlocksGameplay()
        {
            _screen.Show(new ResultData(), () => { }, () => { });

            Assert.IsTrue(_screen.IsOpen);
            Assert.IsTrue(ResultScreen.IsAnyOpen);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(KCDInput.GameplayBlocked);
        }

        [Test]
        public void ChooseContinue_ClosesBeforeCallingBack()
        {
            int continued = 0;
            int toTitle = 0;
            bool openInCallback = true;
            float scaleInCallback = -1f;
            bool blockedInCallback = true;

            _screen.Show(new ResultData(), () =>
            {
                continued++;
                openInCallback = _screen.IsOpen;
                scaleInCallback = Time.timeScale;
                blockedInCallback = KCDInput.GameplayBlocked;
            }, () => toTitle++);
            _screen.Choose(0);

            Assert.AreEqual(1, continued, "「もう一日歩く」が呼ばれない");
            Assert.AreEqual(0, toTitle, "「タイトルへ」まで呼ばれた");
            Assert.IsFalse(openInCallback, "呼び出し先から見て画面がまだ開いている");
            Assert.AreEqual(1f, scaleInCallback, "呼び出し先から見て時間が止まったまま");
            Assert.IsFalse(blockedInCallback, "呼び出し先から見て操作が封鎖されたまま");
            Assert.IsFalse(_screen.IsOpen);
            Assert.IsFalse(ResultScreen.IsAnyOpen);
        }

        [Test]
        public void ChooseToTitle_CallsOnlyTheTitleCallback()
        {
            int continued = 0;
            int toTitle = 0;

            _screen.Show(new ResultData(), () => continued++, () => toTitle++);
            _screen.Choose(1);

            Assert.AreEqual(0, continued);
            Assert.AreEqual(1, toTitle);
            Assert.IsFalse(_screen.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void Choose_WhileClosed_DoesNothing()
        {
            int called = 0;
            _screen.Show(new ResultData(), () => called++, () => called++);
            _screen.Close();

            _screen.Choose(0);
            _screen.Choose(1);

            Assert.AreEqual(0, called, "閉じたあとの選択で呼び出し先が動いた");
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void Choose_OutOfRange_KeepsTheScreenOpen(int index)
        {
            int called = 0;
            _screen.Show(new ResultData(), () => called++, () => called++);

            _screen.Choose(index);

            Assert.AreEqual(0, called);
            Assert.IsTrue(_screen.IsOpen, "範囲外の番号で閉じた");
            Assert.AreEqual(0f, Time.timeScale);
        }
    }
}
