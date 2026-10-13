using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルとキャンパスの行き来を非同期で読む (#15)。読んでいる間は「読み込み中」の画面を出して操作を止め、
    /// 二度押ししても 1 回だけ読み、読み終えたら封鎖と画面を外して、主人公が動ける状態で終わる。
    /// </summary>
    public sealed class SceneLoadingTests : SceneTestBase
    {
        /// <summary>接地を待つ上限（実時間の秒）。CampusSmokeTests と同じ。</summary>
        private const float GroundedTimeoutSeconds = 3f;

        private string _countedScene;
        private int _loadCount;

        [SetUp]
        public void SetUpCounter()
        {
            _countedScene = null;
            _loadCount = 0;
            SceneManager.sceneLoaded += CountLoad;
        }

        [TearDown]
        public void TearDownCounter()
        {
            SceneManager.sceneLoaded -= CountLoad;
        }

        private void CountLoad(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == _countedScene)
            {
                _loadCount++;
            }
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator TitleToCampus_PressedTwice_LoadsOnce_AndEndsWithThePlayerFreeToMove()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);

            GameManager manager = GameManager.Instance;
            SceneLoader loader = manager.Loader;
            manager.BeginNewGame();
            Capture.Watch(GameManager.CampusSceneName);
            _countedScene = GameManager.CampusSceneName;

            // 「はじめから」を続けて押した。
            manager.EnterCampus();
            manager.EnterCampus();
            Assert.IsTrue(SceneLoader.IsAnyLoading, "EnterCampus で読み込みが始まらない");
            Assert.IsTrue(loader.OverlayVisible, "読み始めに「読み込み中」の画面が出ない");
            Assert.IsTrue(KCDInput.IsBlockedBy(loader), "読み込みの間、操作が封鎖されていない");

            float deadline = Time.realtimeSinceStartup + PlayModeScenes.LoadTimeoutSeconds;
            int loadingFrames = 0;
            while (!Capture.SceneLoaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (Capture.SceneLoaded)
                {
                    break;
                }

                loadingFrames++;
                Assert.IsTrue(loader.OverlayVisible, loadingFrames + " フレーム目: 読み込み中なのに画面が消えた");
                Assert.IsTrue(KCDInput.GameplayBlocked, loadingFrames + " フレーム目: 読み込み中なのに操作できる");

                // 読み込みの間の連打も読み直さない。
                manager.EnterCampus();
            }

            AssertLoaded(GameManager.CampusSceneName);
            Assert.AreEqual(1, _loadCount, "二度押しでキャンパスを 2 回以上読んだ");

            // 切り替えのあとの数フレーム（「つづきから」の位置合わせと自動セーブの順番待ち）も、画面と封鎖は一緒に残る。
            while (SceneLoader.IsAnyLoading && Time.realtimeSinceStartup < deadline)
            {
                Assert.IsTrue(loader.OverlayVisible, "読み終える前に「読み込み中」の画面が消えた");
                Assert.IsTrue(KCDInput.IsBlockedBy(loader), "読み終える前に操作の封鎖が外れた");
                yield return null;
            }

            Assert.IsFalse(SceneLoader.IsAnyLoading, "読み終えても読み込み中のまま");
            Assert.IsFalse(loader.OverlayVisible, "読み終えても「読み込み中」の画面が残った");
            Assert.IsFalse(KCDInput.GameplayBlocked, "読み終えても操作が封鎖されたまま");
            Assert.IsFalse(KCDInput.MovementLocked, "読み終えても移動がロックされたまま");
            Assert.IsFalse(KCDInput.PhotoMode, "読み終えたら写真モードに入っていた");
            Assert.AreEqual(1f, Time.timeScale, "読み終えても時間が止まったまま");

            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            Assert.IsTrue(player.isActiveAndEnabled, "PlayerController が動いていない");
            Assert.IsFalse(player.IsSitting, "「はじめから」なのに座っている");

            yield return PlayModeScenes.Advance(60, 2f);

            float groundedDeadline = Time.realtimeSinceStartup + GroundedTimeoutSeconds;
            while (!player.IsGrounded && Time.realtimeSinceStartup < groundedDeadline)
            {
                yield return null;
            }

            Assert.IsTrue(player.IsGrounded, "60 フレーム + " + GroundedTimeoutSeconds + " 秒たっても接地しない");
            Assert.IsFalse(KCDInput.GameplayBlocked, "入ってしばらくたつと操作が封鎖された");
            Assert.AreEqual(1, _loadCount, "読み終えたあとにキャンパスを読み直した");
            Assert.AreEqual(GameManager.CampusSceneName, SceneManager.GetActiveScene().name);
            AssertNoErrors("タイトルからキャンパスへ");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator CampusToTitle_FromThePause_LoadsOnce_AndStartsTheTitleAtFullSpeed()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);

            GameManager manager = GameManager.Instance;
            SceneLoader loader = manager.Loader;
            Capture.Watch(GameManager.TitleSceneName);
            _countedScene = GameManager.TitleSceneName;

            // ポーズメニューの「タイトルへ戻る」は、時間を止めたところから呼ばれる。
            Time.timeScale = 0f;
            manager.ReturnToTitle();
            manager.ReturnToTitle();
            Assert.IsTrue(SceneLoader.IsAnyLoading, "ReturnToTitle で読み込みが始まらない");
            Assert.IsTrue(loader.OverlayVisible, "読み始めに「読み込み中」の画面が出ない");
            Assert.IsTrue(KCDInput.IsBlockedBy(loader), "読み込みの間、操作が封鎖されていない");
            Assert.AreEqual(0f, Time.timeScale, "タイトルを読んでいる間にキャンパスの時計が進む");

            float deadline = Time.realtimeSinceStartup + PlayModeScenes.LoadTimeoutSeconds;
            while (!Capture.SceneLoaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (Capture.SceneLoaded)
                {
                    break;
                }

                Assert.IsTrue(loader.OverlayVisible, "読み込み中なのに画面が消えた");
                Assert.IsTrue(KCDInput.GameplayBlocked, "読み込み中なのに操作できる");
                manager.ReturnToTitle();
            }

            AssertLoaded(GameManager.TitleSceneName);
            Assert.AreEqual(1, _loadCount, "二度押しでタイトルを 2 回以上読んだ");

            while (SceneLoader.IsAnyLoading && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(SceneLoader.IsAnyLoading, "読み終えても読み込み中のまま");
            Assert.IsFalse(loader.OverlayVisible, "読み終えても「読み込み中」の画面が残った");
            Assert.IsFalse(KCDInput.GameplayBlocked, "タイトルで操作が封鎖されたまま");
            Assert.AreEqual(1f, Time.timeScale, "タイトルが止まったまま始まった");

            yield return PlayModeScenes.Advance(30, 1f);

            Assert.AreEqual(1, _loadCount, "読み終えたあとにタイトルを読み直した");
            Assert.AreEqual(GameManager.TitleSceneName, SceneManager.GetActiveScene().name,
                "タイトルへ戻ったあとに勝手に別のシーンへ移った");
            AssertNoErrors("キャンパスからタイトルへ");
        }
    }
}
