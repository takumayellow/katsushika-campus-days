using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// シーンを読む PlayMode テストの土台 (#67)。エラーのログは LogAssert に任せず LogCapture で全部集め、
    /// 各テストの最後に行ごとに出して落とす（最初の 1 行で止まると、後ろの本当の原因が見えない）。
    /// </summary>
    public abstract class SceneTestBase
    {
        /// <summary>重いテスト 1 本の上限（ミリ秒）。Campus の読み込みと 1 日の早送りが入る。既定は 180 秒。</summary>
        protected const int SceneTestTimeoutMs = 300000;

        protected LogCapture Capture { get; private set; }

        [SetUp]
        public void SetUpCapture()
        {
            Time.timeScale = 1f;
            KCDInput.ClearAllBlocks();
            Capture = new LogCapture();
        }

        [TearDown]
        public void TearDownCapture()
        {
            Capture?.Dispose();
            Capture = null;
            Time.timeScale = 1f;
            KCDInput.ClearAllBlocks();
        }

        /// <summary>
        /// 各テストの最初に呼ぶ。LogAssert の「想定外のエラーのログで落とす」はテストごとに戻るので、
        /// SetUp ではなくテスト本体で切る。切っても判定は <see cref="AssertNoErrors"/> が必ず行う。
        /// </summary>
        protected static void CollectErrorsInsteadOfLogAssert()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>シーンが読めたか。読めなければ、それまでに出たエラーを添えて落とす。</summary>
        protected void AssertLoaded(string sceneName)
        {
            Assert.IsTrue(Capture.SceneLoaded,
                sceneName + " が " + PlayModeScenes.LoadTimeoutSeconds + " 秒で読み込まれない。" +
                "EditorBuildSettings に入っているか、SceneBuilder.BuildAll で作り直したかを確かめる。" +
                LogCapture.Describe(Capture.Errors(true)));
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name, "読み込んだあとのアクティブなシーンが違う");
        }

        /// <summary>
        /// エラー・例外・Assert のログが 0 行か。NPC が NavMesh に載れない行（#63）は NpcNavMeshTests が見るので、
        /// ここでは数えない。
        /// </summary>
        protected void AssertNoErrors(string where)
        {
            var errors = Capture.Errors(false);
            Assert.IsEmpty(errors, where + " でエラーのログが " + errors.Count + " 行出た:" + LogCapture.Describe(errors));
        }
    }
}
