using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>展示モード (#466) の判定。</summary>
    public sealed class ExhibitModeTests
    {
        [TearDown]
        public void TearDown()
        {
            ExhibitMode.ResetForTests();
        }

        [Test]
        public void IsEnabled_MarkerFile_TurnsOn()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe" }, true), Is.True);
        }

        [Test]
        public void IsEnabled_Argument_TurnsOn_IgnoringCase()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe", "-KCD-Exhibit" }, false), Is.True);
        }

        [Test]
        public void IsEnabled_NeitherMarkerNorArgument_StaysOff()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe", "-kcd-scene", "Campus" }, false), Is.False);
            Assert.That(ExhibitMode.IsEnabled(null, false), Is.False);
        }

        [Test]
        public void MarkerPath_IsNextToExe()
        {
            string data = Path.Combine("C:" + Path.DirectorySeparatorChar + "kcd", "KatsushikaCampusDays_Data");
            string expected = Path.Combine("C:" + Path.DirectorySeparatorChar + "kcd", ExhibitMode.MarkerFileName);
            Assert.That(ExhibitMode.MarkerPath(data, true), Is.EqualTo(expected));
        }

        [Test]
        public void MarkerPath_EditorAndWeb_NotLooked()
        {
            Assert.That(ExhibitMode.MarkerPath("C:/kcd/KatsushikaCampusDays_Data", false), Is.Null);
            Assert.That(ExhibitMode.MarkerPath(string.Empty, true), Is.Null);
        }

        [Test]
        public void SaveDirectory_IsASubfolderOfPersistent()
        {
            string persistent = Path.Combine("C:" + Path.DirectorySeparatorChar + "data", "kcd");
            Assert.That(ExhibitMode.SaveDirectory(persistent),
                Is.EqualTo(Path.Combine(persistent, ExhibitMode.SaveFolderName)));
            Assert.That(ExhibitMode.SaveDirectory(string.Empty), Is.Null);
        }

        [Test]
        public void PhotoRoot_UsesExhibitFolder_UnlessOverridden()
        {
            string save = Path.Combine("C:" + Path.DirectorySeparatorChar + "data", ExhibitMode.SaveFolderName);
            Assert.That(ExhibitMode.PhotoRoot(null, save), Is.EqualTo(save), "展示の写真が普段の写真と混ざる");
            Assert.That(ExhibitMode.PhotoRoot("C:/tmp/smoke", save), Is.EqualTo("C:/tmp/smoke"), "スモークの差し替えを上書きした");
        }

        [Test]
        public void OffersContinue_HiddenInExhibit()
        {
            Assert.That(ExhibitMode.OffersContinue(false, true), Is.True);
            Assert.That(ExhibitMode.OffersContinue(true, true), Is.False);
            Assert.That(ExhibitMode.OffersContinue(false, false), Is.False);
        }

        [Test]
        public void Enabled_CanBeOverriddenForTests()
        {
            ExhibitMode.Enabled = true;
            Assert.That(ExhibitMode.Enabled, Is.True);
            ExhibitMode.Enabled = false;
            Assert.That(ExhibitMode.Enabled, Is.False);
        }

        [Test]
        public void Decide_BeforeWarning_DoesNothing()
        {
            float early = ExhibitMode.IdleSeconds - ExhibitMode.WarnBeforeSeconds - 1f;
            Assert.That(ExhibitMode.Decide(early, false, false), Is.EqualTo(ExhibitMode.IdleAction.None));
        }

        [Test]
        public void Decide_WarnsOnceBeforeReturning()
        {
            float warnAt = ExhibitMode.IdleSeconds - ExhibitMode.WarnBeforeSeconds;
            Assert.That(ExhibitMode.Decide(warnAt, false, false), Is.EqualTo(ExhibitMode.IdleAction.Warn));
            Assert.That(ExhibitMode.Decide(warnAt + 1f, true, false), Is.EqualTo(ExhibitMode.IdleAction.None));
        }

        [Test]
        public void Decide_ReturnsAtLimit_EvenWithoutWarning()
        {
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds, true, false),
                Is.EqualTo(ExhibitMode.IdleAction.ReturnToTitle));
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds + 30f, false, false),
                Is.EqualTo(ExhibitMode.IdleAction.ReturnToTitle));
        }

        [Test]
        public void Decide_WaitsWhileLoading()
        {
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds + 30f, false, true),
                Is.EqualTo(ExhibitMode.IdleAction.None));
        }

        // ---- 全画面 ----

        [Test]
        public void ShouldForceFullscreen_NoScreenArguments_Forces()
        {
            Assert.That(ExhibitMode.ShouldForceFullscreen(new[] { "game.exe" }), Is.True);
            Assert.That(ExhibitMode.ShouldForceFullscreen(new[] { "game.exe", "-kcd-exhibit", "-logFile", "x.log" }), Is.True);
            Assert.That(ExhibitMode.ShouldForceFullscreen(null), Is.True);
            Assert.That(ExhibitMode.ShouldForceFullscreen(new[] { "game.exe", "-screen-quality", "Low" }), Is.True,
                "画質の指定だけで全画面をやめた");
        }

        [TestCase("-screen-fullscreen")]
        [TestCase("-Screen-Width")]
        [TestCase("-screen-height")]
        [TestCase("-window-mode")]
        [TestCase("-popupwindow")]
        public void ShouldForceFullscreen_ScreenArgument_FollowsIt(string arg)
        {
            Assert.That(ExhibitMode.ShouldForceFullscreen(new[] { "game.exe", arg, "0" }), Is.False,
                "起動引数で画面を指定したのに全画面にした");
        }

        // ---- 来場者が変えた設定を次の人に残さない ----

        [Test]
        public void Exhibit_CameraSettings_AreNotSavedAndGoBackToDefault()
        {
            using (new PlayerPrefsKeyScope(CameraPrefs.SensitivityPrefKey))
            using (new PlayerPrefsKeyScope(CameraPrefs.InvertPrefKey))
            {
                try
                {
                    PlayerPrefs.SetString(CameraPrefs.SensitivityPrefKey, "9");
                    ExhibitMode.Enabled = true;
                    CameraPrefs.Reload();
                    Assert.AreEqual(CameraPrefs.DefaultLevel, CameraPrefs.SensitivityLevel, "展示モードで保存された感度を使った");

                    CameraPrefs.SensitivityLevel = 2;
                    CameraPrefs.InvertY = true;
                    Assert.AreEqual(2, CameraPrefs.SensitivityLevel, "展示モードで感度を変えられない");
                    Assert.AreEqual("9", PlayerPrefs.GetString(CameraPrefs.SensitivityPrefKey), "来場者が変えた感度を保存した");
                    Assert.IsFalse(PlayerPrefs.HasKey(CameraPrefs.InvertPrefKey), "来場者が変えた反転を保存した");

                    CameraPrefs.ResetForNextVisitor();
                    Assert.AreEqual(CameraPrefs.DefaultLevel, CameraPrefs.SensitivityLevel);
                    Assert.IsFalse(CameraPrefs.InvertY);
                }
                finally
                {
                    ExhibitMode.ResetForTests();
                    CameraPrefs.Reload();
                }
            }
        }

        [Test]
        public void Exhibit_Volume_IsNotSavedAndGoesBackToDefault()
        {
            const string BgmKey = "KCD.Volume.Bgm";
            var go = new GameObject("AudioManagerTest");
            try
            {
                using (new PlayerPrefsKeyScope(BgmKey))
                {
                    PlayerPrefs.DeleteKey(BgmKey);
                    AudioManager audio = go.AddComponent<AudioManager>();
                    ExhibitMode.Enabled = true;

                    audio.BgmVolume = 0.1f;
                    Assert.AreEqual(0.1f, audio.BgmVolume, 1e-5f, "展示モードで音量を変えられない");
                    Assert.IsFalse(PlayerPrefs.HasKey(BgmKey), "来場者が変えた音量を保存した");

                    audio.ResetVolumesForNextVisitor();
                    Assert.AreEqual(AudioManager.DefaultBgmVolume, audio.BgmVolume, 1e-5f);
                    Assert.AreEqual(AudioManager.DefaultSeVolume, audio.SeVolume, 1e-5f);
                    Assert.AreEqual(AudioManager.DefaultAmbientVolume, audio.AmbientVolume, 1e-5f);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Exhibit_QualityTier_IsNotSavedAndGoesBackToDefault()
        {
            QualityTier before = QualityManager.Current;
            QualityTier fallback = QualityTiers.DefaultFor(QualityTiers.TargetPlatform, Application.isMobilePlatform);
            QualityTier other = fallback == QualityTier.Low ? QualityTier.High : QualityTier.Low;
            using (new PlayerPrefsKeyScope(QualityTiers.PrefKey))
            {
                try
                {
                    PlayerPrefs.DeleteKey(QualityTiers.PrefKey);
                    ExhibitMode.Enabled = true;

                    QualityManager.SetTier(other);
                    Assert.AreEqual(other, QualityManager.Current, "展示モードで画質を変えられない");
                    Assert.IsFalse(PlayerPrefs.HasKey(QualityTiers.PrefKey), "来場者が変えた画質を保存した");

                    QualityManager.ResetForNextVisitor();
                    Assert.AreEqual(fallback, QualityManager.Current);
                }
                finally
                {
                    // 段を元に戻す（展示モードのままなので保存はしない）。
                    QualityManager.SetTier(before);
                    ExhibitMode.ResetForTests();
                }
            }
        }
    }
}
