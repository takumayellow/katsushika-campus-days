using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>テストの間、BGM は流れているが聞こえない（MuteDuringTests）。</summary>
    public sealed class SilentPlayTests : SceneTestBase
    {
        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Title_PlaysBgm_AtZeroVolume()
        {
            yield return PlayModeScenes.LoadTitle(Capture);
            yield return PlayModeScenes.Advance(10, 0.5f);

            Assert.IsNotNull(AudioManager.Instance, "タイトルに AudioManager が無い");
            Assert.IsFalse(string.IsNullOrEmpty(AudioManager.Instance.CurrentBgm), "タイトルで BGM が流れていない");
            Assert.AreEqual(0f, AudioListener.volume, "テストの間に音量が戻された");
        }
    }
}
