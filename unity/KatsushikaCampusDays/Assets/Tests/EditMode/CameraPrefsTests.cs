using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 設定画面で変えるカメラの感度と上下の反転 (#6)。段から倍率への換算、端での止まり方、保存の読み書き。
    /// </summary>
    public sealed class CameraPrefsTests
    {
        [TestCase(5, 1f)]
        [TestCase(1, 0.2f)]
        [TestCase(10, 2f)]
        [TestCase(0, 0.2f)]
        [TestCase(99, 2f)]
        public void Scale_IsOneAtTheMiddleLevel(int level, float expected)
        {
            Assert.AreEqual(expected, CameraPrefs.Scale(level), 1e-5f);
        }

        [TestCase(5, 1, 6)]
        [TestCase(5, -1, 4)]
        [TestCase(10, 1, 10)]
        [TestCase(1, -1, 1)]
        [TestCase(5, 3, 6)]
        [TestCase(5, 0, 5)]
        public void StepLevel_MovesOneAndStopsAtTheEnds(int level, int direction, int expected)
        {
            Assert.AreEqual(expected, CameraPrefs.StepLevel(level, direction));
        }

        [TestCase("7", 7)]
        [TestCase("0", 1)]
        [TestCase("42", 10)]
        [TestCase("", 5)]
        [TestCase("abc", 5)]
        [TestCase(null, 5)]
        public void ParseLevel_FallsBackToTheDefault(string saved, int expected)
        {
            Assert.AreEqual(expected, CameraPrefs.ParseLevel(saved));
        }

        [Test]
        public void SensitivityBar_ShowsTheLevelAsFilledBoxes()
        {
            Assert.AreEqual("■■■□□□□□□□  3", SettingsView.SensitivityBar(3));
            Assert.AreEqual("■■■■■■■■■■  10", SettingsView.SensitivityBar(42));
        }

        [Test]
        public void Saved_SurvivesAReload()
        {
            using (new PlayerPrefsKeyScope(CameraPrefs.SensitivityPrefKey))
            using (new PlayerPrefsKeyScope(CameraPrefs.InvertPrefKey))
            {
                try
                {
                    PlayerPrefs.DeleteKey(CameraPrefs.SensitivityPrefKey);
                    PlayerPrefs.DeleteKey(CameraPrefs.InvertPrefKey);
                    CameraPrefs.Reload();
                    Assert.AreEqual(CameraPrefs.DefaultLevel, CameraPrefs.SensitivityLevel, "保存が無ければ真ん中の段");
                    Assert.IsFalse(CameraPrefs.InvertY);
                    Assert.AreEqual(1f, CameraPrefs.VerticalSign);

                    CameraPrefs.SensitivityLevel = 8;
                    CameraPrefs.InvertY = true;
                    CameraPrefs.Reload();

                    Assert.AreEqual(8, CameraPrefs.SensitivityLevel);
                    Assert.AreEqual(1.6f, CameraPrefs.SensitivityScale, 1e-5f);
                    Assert.IsTrue(CameraPrefs.InvertY);
                    Assert.AreEqual(-1f, CameraPrefs.VerticalSign);
                }
                finally
                {
                    CameraPrefs.Reload();
                }
            }
        }
    }
}
