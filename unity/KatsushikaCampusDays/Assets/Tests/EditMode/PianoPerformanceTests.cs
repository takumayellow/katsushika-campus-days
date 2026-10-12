using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>図書館のピアノを弾く操作の素材と文言、BGM の絞り方。</summary>
    public sealed class PianoPerformanceTests
    {
        private const string ClipPath = "Assets/Audio/SE/piano_library.wav";

        [Test]
        public void PianoClip_IsAShortPhraseNamedLikeTheDefaultId()
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            Assert.IsNotNull(clip, ClipPath + " が読めない");
            Assert.AreEqual(PianoPerformance.DefaultClipId, clip.name, "AudioManager は AudioClip の名前で曲を探す");
            Assert.That(clip.length, Is.InRange(10f, 30f), "短い曲のはず (" + clip.length + " 秒)");
        }

        [Test]
        public void PromptKey_IsInEveryLanguage()
        {
            foreach (string lang in new[] { "ja", "en" })
            {
                string text = System.IO.File.ReadAllText("Assets/Data/Localization/" + lang + ".json");
                StringAssert.Contains("\"ui.interact.play_piano\"", text, lang);
                text = System.IO.File.ReadAllText("Assets/Resources/KCD/Localization/" + lang + ".json");
                StringAssert.Contains("\"ui.interact.play_piano\"", text, "Resources " + lang);
            }
        }

        [Test]
        public void PianoDuck_IsDeeperThanAJingleAndRecovers()
        {
            Assert.Less(AudioManager.PianoDuckLevel, AudioManager.JingleDuckLevel, "ピアノの間は、曲が聞こえるよう BGM をジングルより絞る");

            float duck = 1f;
            for (float t = 0f; t < AudioManager.DuckAttackSeconds + 0.01f; t += 0.02f)
            {
                duck = AudioManager.StepDuck(duck, AudioManager.PianoDuckLevel, 0.02f);
            }

            Assert.AreEqual(AudioManager.PianoDuckLevel, duck, 0.01f, "絞り切るまで");
            for (float t = 0f; t < AudioManager.DuckReleaseSeconds + 0.01f; t += 0.02f)
            {
                duck = AudioManager.StepDuck(duck, 1f, 0.02f);
            }

            Assert.AreEqual(1f, duck, 0.01f, "立ったあと BGM が戻り切るまで");
        }

        [Test]
        public void StandForward_GoesBehindTheStool()
        {
            Assert.Less(PianoPerformance.StandForward, 0f);
        }
    }
}
