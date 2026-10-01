using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// シーンの非同期の読み込み（SceneLoader, #15）。EditMode では LoadSceneAsync もコルーチンも使えないので、
    /// Load が順に呼ぶ段（Begin → ReportProgress → BeforeActivation → OnTargetLoaded → TrySettle）を直接呼ぶ。
    /// 二度押しを 1 回にまとめること、読み込み中の画面の出し入れ、封鎖・自動セーブ・時間を外す順番を確かめる。
    /// </summary>
    public sealed class SceneLoaderTests
    {
        private const string Campus = "Campus";
        private const string Title = "Title";
        private const float Tolerance = 0.0001f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<SceneLoader> _loaders = new List<SceneLoader>();
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            KCDInput.ClearAllBlocks();
            AutoSave.Cancel();
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode では OnDisable / OnDestroy が走らないので、読みかけのローダーは自分で片付ける。
            foreach (SceneLoader loader in _loaders)
            {
                if (loader != null)
                {
                    loader.Abort();
                }
            }

            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _loaders.Clear();
            _created.Clear();
            Time.timeScale = _timeScale;
            KCDInput.ClearAllBlocks();
            AutoSave.Cancel();
        }

        private SceneLoader MakeLoader()
        {
            var go = new GameObject("SceneLoaderForTest");
            _created.Add(go);
            SceneLoader loader = go.AddComponent<SceneLoader>();
            _loaders.Add(loader);
            return loader;
        }

        /// <summary>読み始めから切り替えまで進め、loadedFrame で切り替わったことにする。</summary>
        private static void RunUntilLoaded(SceneLoader loader, string sceneName, bool toTitle, int loadedFrame)
        {
            Assert.IsTrue(loader.Begin(sceneName, toTitle));
            loader.ReportProgress(0.3f);
            loader.BeforeActivation();
            loader.OnTargetLoaded(loadedFrame);
        }

        // ---- 二度押し ----

        [Test]
        public void Begin_WhileLoadingOrSwitching_IsRefused()
        {
            SceneLoader loader = MakeLoader();

            Assert.IsTrue(loader.Begin(Campus, false));
            Assert.IsTrue(loader.IsLoading);
            Assert.AreEqual(Campus, loader.SceneName);

            // 「はじめから」を続けて押した。読み込み中なので 2 回目は読まない。
            Assert.IsFalse(loader.Begin(Campus, false), "読み込み中にもう一度読み始めた");
            Assert.IsFalse(loader.Begin(Title, true), "読み込み中に別のシーンを読み始めた");
            Assert.AreEqual(Campus, loader.SceneName, "二度押しで読むシーンが変わった");
            Assert.AreEqual(1f, Time.timeScale, "断った二度押しがタイトルへ戻るときの時間停止を掛けた");

            // 切り替えを許したあと、切り替わるまでの間も断る。
            loader.BeforeActivation();
            Assert.IsFalse(loader.Begin(Campus, false), "切り替えの最中にもう一度読み始めた");
            Assert.AreEqual(Campus, loader.SceneName);
            Assert.IsTrue(loader.IsLoading);
            Assert.IsFalse(loader.IsSettling);
        }

        [Test]
        public void Begin_WhileSettling_FinishesThePreviousLoadAndStartsTheNext()
        {
            SceneLoader loader = MakeLoader();
            RunUntilLoaded(loader, Campus, false, 100);
            Assert.IsTrue(loader.IsSettling);

            // 切り替わった直後の新しいシーンから次のシーンへ進む操作は捨てない（捨てると押したのに何も起きない）。
            Assert.IsTrue(loader.Begin(Title, true), "切り替えのあとの数フレームに頼まれた読み込みを捨てた");
            Assert.IsFalse(loader.IsSettling);
            Assert.IsTrue(loader.IsLoading);
            Assert.AreEqual(Title, loader.SceneName);
            Assert.IsTrue(KCDInput.IsBlockedBy(loader), "新しい読み込みの封鎖が掛かっていない");
            Assert.IsTrue(loader.OverlayVisible);
            Assert.AreEqual(0f, loader.OverlayProgress, Tolerance, "新しい読み込みのバーが前の読み込みの続きから始まった");
            Assert.AreEqual(0f, Time.timeScale, "タイトルへ戻る読み込みで古いシーンの時間を止めていない");

            // 前の読み込みの切り替えのフレームで仕上げられることはもう無い。
            Assert.IsFalse(loader.TrySettle(102), "切り替える前に前の読み込みの続きで外れた");
            Assert.IsTrue(loader.IsLoading);
        }

        // ---- 読み込み中の画面 ----

        [Test]
        public void Overlay_IsShownAtBegin_FollowsTheProgress_AndIsHiddenAfterSettling()
        {
            SceneLoader loader = MakeLoader();
            Assert.IsFalse(loader.OverlayVisible, "読む前から画面が出ている");

            loader.Begin(Campus, false);
            Assert.IsTrue(loader.OverlayVisible, "読み始めに「読み込み中」の画面が出ない");
            Assert.AreEqual(0f, loader.OverlayProgress, Tolerance);
            Assert.AreEqual(LoadingOverlay.LabelFor(L.IsEnglish), loader.OverlayLabel);

            // progress は 0.9 で止まるので、0.45 はバーの半分。
            loader.ReportProgress(0.45f);
            Assert.AreEqual(0.5f, loader.OverlayProgress, Tolerance);
            loader.ReportProgress(0.9f);
            Assert.AreEqual(1f, loader.OverlayProgress, Tolerance);

            loader.BeforeActivation();
            loader.OnTargetLoaded(200);
            Assert.IsTrue(loader.OverlayVisible, "切り替わった瞬間に画面が消えた（最初のフレームが見える）");
            Assert.AreEqual(1f, loader.OverlayProgress, Tolerance);

            Assert.IsFalse(loader.TrySettle(200 + SceneLoader.SettleFrames - 1));
            Assert.IsTrue(loader.OverlayVisible, "決めたフレーム数より早く画面が消えた");

            Assert.IsTrue(loader.TrySettle(200 + SceneLoader.SettleFrames));
            Assert.IsFalse(loader.OverlayVisible, "読み終えても「読み込み中」の画面が残った");
            Assert.IsFalse(loader.IsLoading);
            Assert.IsNull(loader.SceneName);
        }

        [Test]
        public void Overlay_ShowsTheLabelOfTheCurrentLanguage()
        {
            // 言語を切り替えると L.PrefKey に書く。キーの無かった機械には残さず、有った機械には元の値を戻す (#101)。
            string before = L.Locale;
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            try
            {
                SceneLoader loader = MakeLoader();

                L.SetLocale("en");
                loader.Begin(Campus, false);
                Assert.AreEqual(LoadingOverlay.EnglishLabel, loader.OverlayLabel);
                loader.Abort();

                L.SetLocale("ja");
                loader.Begin(Campus, false);
                Assert.AreEqual(LoadingOverlay.JapaneseLabel, loader.OverlayLabel);
            }
            finally
            {
                L.SetLocale(before);
                if (hadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, savedPref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }

        [Test]
        public void Overlay_LanguageTest_LeavesNoLocaleKeyWhereThereWasNone()
        {
            // 開発機の本物の言語設定。上のテストの戻し方が壊れていても失くさないよう、ここで手で預かって手で戻す。
            bool machineHadKey = PlayerPrefs.HasKey(L.PrefKey);
            string machinePref = machineHadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            bool written = false;
            void OnChanged() => written |= PlayerPrefs.HasKey(L.PrefKey);

            // 言語を選んだことのない機械（新しい checkout・CI）。
            PlayerPrefs.DeleteKey(L.PrefKey);
            L.LocaleChanged += OnChanged;
            try
            {
                Overlay_ShowsTheLabelOfTheCurrentLanguage();

                Assert.IsTrue(written, "前提: 言語の往復で KCD.Locale が書かれていない（確かめたことにならない）");
                Assert.IsFalse(PlayerPrefs.HasKey(L.PrefKey), "言語を選んだことのない機械に KCD.Locale を作って残した");
            }
            finally
            {
                L.LocaleChanged -= OnChanged;
                if (machineHadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, machinePref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }

        // ---- 封鎖を外す順番 ----

        [Test]
        public void Blocks_OldSceneOnesAreDropped_NewSceneOnesSurvive_TheLoaderHoldsUntilSettled()
        {
            SceneLoader loader = MakeLoader();

            // 古いシーンで掛かったまま残った封鎖（オーナーと共有のフラグ）。
            var stale = new object();
            KCDInput.Block(stale);
            KCDInput.GameplayBlocked = true;

            loader.Begin(Campus, false);
            Assert.IsFalse(KCDInput.IsBlockedBy(stale), "読み始めに古い封鎖が外れない (#40)");
            Assert.IsTrue(KCDInput.IsBlockedBy(loader), "読み込みの間、ローダーが操作を封鎖していない");

            // 読み込みの間に古いシーン（まだ動いている）が掛けた封鎖。持ち主はこのあと消える。
            var oldScene = new object();
            KCDInput.Block(oldScene);
            loader.BeforeActivation();
            Assert.IsFalse(KCDInput.IsBlockedBy(oldScene), "切り替えの直前に古いシーンの封鎖が外れない");
            Assert.IsTrue(KCDInput.IsBlockedBy(loader), "切り替えの直前にローダーの封鎖まで外れた");

            // 新しいシーンの Awake / Start が掛けた封鎖は残す。
            loader.OnTargetLoaded(300);
            var newScene = new object();
            KCDInput.Block(newScene);

            Assert.IsFalse(loader.TrySettle(300 + SceneLoader.SettleFrames - 1));
            Assert.IsTrue(KCDInput.IsBlockedBy(loader),
                "切り替えから " + (SceneLoader.SettleFrames - 1) + " フレームで封鎖が外れた（つづきからの位置合わせより前に動ける）");

            Assert.IsTrue(loader.TrySettle(300 + SceneLoader.SettleFrames));
            Assert.IsFalse(KCDInput.IsBlockedBy(loader), "読み終えてもローダーの封鎖が残った");
            Assert.IsTrue(KCDInput.IsBlockedBy(newScene), "読み終えたときに新しいシーンの封鎖まで外した");

            // 古いシーンの共有のフラグも残っていない。
            KCDInput.Unblock(newScene);
            Assert.IsFalse(KCDInput.GameplayBlocked, "読み終えても古いシーンの封鎖が残った");
        }

        [Test]
        public void Settle_WaitsForTheSwitch()
        {
            SceneLoader loader = MakeLoader();
            loader.Begin(Campus, false);

            // 切り替わる前は、何フレーム経っても外さない（重いシーンは何秒もかかる）。
            Assert.IsFalse(loader.TrySettle(100000), "切り替わる前に封鎖と画面を外した");
            loader.BeforeActivation();
            Assert.IsFalse(loader.TrySettle(100000), "切り替えを許しただけで封鎖と画面を外した");
            Assert.IsTrue(KCDInput.IsBlockedBy(loader));
            Assert.IsTrue(loader.OverlayVisible);
        }

        [Test]
        public void Stages_OutOfOrder_DoNothing()
        {
            SceneLoader loader = MakeLoader();

            loader.ReportProgress(0.5f);
            loader.BeforeActivation();
            Assert.IsFalse(KCDInput.GameplayBlocked, "読んでいないのに切り替えの直前の段が封鎖を掛けた");

            loader.OnTargetLoaded(10);
            Assert.IsFalse(loader.IsLoading, "読んでいないのに切り替わったことになった");
            Assert.IsFalse(loader.TrySettle(100));
            Assert.IsFalse(loader.OverlayVisible);
        }

        // ---- 時間 ----

        [Test]
        public void ToTitle_StopsTheOldSceneWhileLoading_AndStartsTheTitleAtFullSpeed()
        {
            SceneLoader loader = MakeLoader();

            loader.Begin(Title, true);
            Assert.AreEqual(0f, Time.timeScale, "タイトルを読んでいる間にキャンパスの時計が進む");

            loader.BeforeActivation();
            Assert.AreEqual(1f, Time.timeScale, "タイトルが止まったまま始まる");

            loader.OnTargetLoaded(10);
            Assert.IsTrue(loader.TrySettle(10 + SceneLoader.SettleFrames));
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void ToTitle_FromThePause_StartsTheTitleAtFullSpeed()
        {
            SceneLoader loader = MakeLoader();

            // ポーズメニューの「タイトルへ戻る」。ポーズが止めた時間のまま読み始める。
            Time.timeScale = 0f;
            loader.Begin(Title, true);
            Assert.AreEqual(0f, Time.timeScale);

            loader.BeforeActivation();
            Assert.AreEqual(1f, Time.timeScale, "ポーズから戻ったタイトルが止まったまま始まる");
        }

        [Test]
        public void ToCampus_LeavesTheTimeScaleAlone()
        {
            SceneLoader loader = MakeLoader();

            RunUntilLoaded(loader, Campus, false, 10);
            Assert.AreEqual(1f, Time.timeScale, "キャンパスへ入る読み込みが時間を変えた");
            Assert.IsTrue(loader.TrySettle(10 + SceneLoader.SettleFrames));
            Assert.AreEqual(1f, Time.timeScale);
        }

        // ---- 自動セーブ ----

        [Test]
        public void AutoSave_OldSceneRequestsAreDropped_NewSceneRequestsAreKept()
        {
            SceneLoader loader = MakeLoader();

            AutoSave.Request();
            loader.Begin(Campus, false);
            Assert.IsFalse(AutoSave.Pending, "読み始めに前のキャンパスの自動セーブの依頼が残った (#61)");

            // 読み込みの間に古いシーンが頼んだ分も、次のシーンの最初のフレームで書かせない。
            AutoSave.Request();
            loader.BeforeActivation();
            Assert.IsFalse(AutoSave.Pending, "切り替えの直前に古いシーンの自動セーブの依頼が残った");

            // 新しいシーンで頼まれた分は捨てない。
            loader.OnTargetLoaded(10);
            AutoSave.Request();
            Assert.IsTrue(loader.TrySettle(10 + SceneLoader.SettleFrames));
            Assert.IsTrue(AutoSave.Pending, "新しいシーンの自動セーブの依頼を捨てた");
        }

        // ---- やめる ----

        [Test]
        public void Abort_WhileLoadingTheTitle_GivesBackTheBlockTheTimeAndTheScreen()
        {
            SceneLoader loader = MakeLoader();
            var other = new object();

            loader.Begin(Title, true);
            KCDInput.Block(other);
            loader.Abort();

            Assert.IsFalse(loader.IsLoading);
            Assert.IsFalse(SceneLoader.IsAnyLoading);
            Assert.IsFalse(loader.OverlayVisible, "やめたのに「読み込み中」の画面が残った");
            Assert.IsFalse(KCDInput.IsBlockedBy(loader), "やめたのにローダーの封鎖が残った");
            Assert.IsTrue(KCDInput.IsBlockedBy(other), "やめたときにほかの封鎖まで外した");
            Assert.AreEqual(1f, Time.timeScale, "タイトルへ戻るのをやめたのに時間が止まったまま");

            // やめたあとはもう一度読める。
            Assert.IsTrue(loader.Begin(Title, true));
        }

        [Test]
        public void Abort_AfterTheSwitch_LeavesTheNewScenesTimeScaleAlone()
        {
            SceneLoader loader = MakeLoader();
            RunUntilLoaded(loader, Title, true, 10);

            // 新しいシーンが自分で時間を変えた（演出など）。やめても触らない。
            Time.timeScale = 0.5f;
            loader.Abort();
            Assert.AreEqual(0.5f, Time.timeScale, Tolerance, "切り替えのあとにやめたら新しいシーンの時間を変えた");
            Assert.IsFalse(KCDInput.IsBlockedBy(loader));
            Assert.IsFalse(loader.OverlayVisible);
        }

        [Test]
        public void Abort_WhenIdle_TouchesNothing()
        {
            SceneLoader loader = MakeLoader();
            var pause = new object();
            KCDInput.Block(pause);
            Time.timeScale = 0f;

            loader.Abort();
            Assert.AreEqual(0f, Time.timeScale, "読んでいないローダーがほかの画面の止めた時間を動かした");
            Assert.IsTrue(KCDInput.IsBlockedBy(pause));
        }

        // ---- どこかで読んでいるか ----

        [Test]
        public void IsAnyLoading_FollowsTheLoader_AndForgetsADestroyedOne()
        {
            Assert.IsFalse(SceneLoader.IsAnyLoading);

            SceneLoader loader = MakeLoader();
            loader.Begin(Campus, false);
            Assert.IsTrue(SceneLoader.IsAnyLoading, "読み込み中なのにポーズメニューの門が開いている");

            loader.OnTargetLoaded(10);
            Assert.IsTrue(SceneLoader.IsAnyLoading, "切り替えのあとの数フレームも読み込み中として扱う");
            Assert.IsTrue(loader.TrySettle(10 + SceneLoader.SettleFrames));
            Assert.IsFalse(SceneLoader.IsAnyLoading);

            // 読みかけのまま持ち主ごと消えても、門が閉じたまま残らない。
            loader.Begin(Title, false);
            Assert.IsTrue(SceneLoader.IsAnyLoading);
            Object.DestroyImmediate(loader.gameObject);
            Assert.IsFalse(SceneLoader.IsAnyLoading, "消えたローダーが読み込み中のまま残った");
        }

        // ---- 純関数 ----

        [Test]
        public void DisplayProgress_StretchesZeroToPointNineOverTheWholeBar()
        {
            Assert.AreEqual(0f, SceneLoader.DisplayProgress(0f), Tolerance);
            Assert.AreEqual(0.5f, SceneLoader.DisplayProgress(0.45f), Tolerance);
            Assert.AreEqual(1f, SceneLoader.DisplayProgress(SceneLoader.ActivationProgress), Tolerance);
            Assert.AreEqual(1f, SceneLoader.DisplayProgress(1f), Tolerance, "0.9 を越えてもバーは 1 まで");
            Assert.AreEqual(0f, SceneLoader.DisplayProgress(-1f), Tolerance);
        }

        [Test]
        public void ReadyToActivate_OnlyOncePointNineIsReached()
        {
            Assert.IsFalse(SceneLoader.ReadyToActivate(0f));
            Assert.IsFalse(SceneLoader.ReadyToActivate(0.89f));
            Assert.IsTrue(SceneLoader.ReadyToActivate(0.9f));
            Assert.IsTrue(SceneLoader.ReadyToActivate(1f));
        }

        [Test]
        public void ShouldSettle_CountsFramesFromTheSwitch()
        {
            Assert.IsFalse(SceneLoader.ShouldSettle(SceneLoader.NoFrame, 100000), "切り替わっていないのに外す");
            Assert.IsFalse(SceneLoader.ShouldSettle(10, 10));
            Assert.IsFalse(SceneLoader.ShouldSettle(10, 10 + SceneLoader.SettleFrames - 1));
            Assert.IsTrue(SceneLoader.ShouldSettle(10, 10 + SceneLoader.SettleFrames));
            Assert.IsTrue(SceneLoader.ShouldSettle(10, 50));
        }

        [Test]
        public void LabelFor_PicksTheLanguage()
        {
            Assert.AreEqual("読み込み中", LoadingOverlay.LabelFor(false));
            Assert.AreEqual("Loading", LoadingOverlay.LabelFor(true));
        }
    }
}
