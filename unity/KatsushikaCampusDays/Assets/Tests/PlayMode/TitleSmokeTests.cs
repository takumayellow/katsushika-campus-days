using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>タイトルを読んで数秒放っておいても、例外もエラーのログも出ず、勝手に別のシーンへ行かない (#67)。</summary>
    public sealed class TitleSmokeTests : SceneTestBase
    {
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Title_LoadsAndIdlesWithoutErrors()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadTitle(Capture);
            AssertLoaded(GameManager.TitleSceneName);
            Assert.IsNotNull(Object.FindAnyObjectByType<TitleMenu>(), "タイトルに TitleMenu が無い");

            yield return PlayModeScenes.Advance(60, 2f);

            Assert.AreEqual(GameManager.TitleSceneName, SceneManager.GetActiveScene().name,
                "操作していないのにタイトルから別のシーンへ移った");
            AssertNoErrors("タイトル");
        }
    }
}
