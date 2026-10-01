using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 画質の段の表と, 選んだ段の保存 (#70)。
    ///
    /// 段は軽い順に並び, Web 版の既定 Medium は今の Web 版の見た目と同じ値にしてある。値を変えるときは
    /// docs/WEBGL_BUDGET.md の表も直す。
    /// </summary>
    public sealed class QualityTierTests
    {
        private static readonly int[] ShadowResolutions = { 256, 512, 1024, 2048, 4096 };
        private static readonly int[] MsaaSamples = { 1, 2, 4, 8 };

        private bool _hadPref;
        private string _savedPref;

        [SetUp]
        public void RememberPref()
        {
            // 保存のテストで開発機の設定を書き換えない。終わったら元に戻す。
            _hadPref = PlayerPrefs.HasKey(QualityTiers.PrefKey);
            _savedPref = PlayerPrefs.GetString(QualityTiers.PrefKey, string.Empty);
        }

        [TearDown]
        public void RestorePref()
        {
            if (_hadPref)
            {
                PlayerPrefs.SetString(QualityTiers.PrefKey, _savedPref);
            }
            else
            {
                PlayerPrefs.DeleteKey(QualityTiers.PrefKey);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void All_ListsEveryTierFromLightestToHeaviest()
        {
            CollectionAssert.AreEqual(new[] { QualityTier.Low, QualityTier.Medium, QualityTier.High },
                                      QualityTiers.All);
        }

        [Test]
        public void Settings_MatchTheTable()
        {
            // docs/WEBGL_BUDGET.md の表と同じ値。
            AssertTier(QualityTier.Low, 0.7f, 30f, 1, 512, 1, false, 300f);
            AssertTier(QualityTier.Medium, 0.8f, 50f, 1, 1024, 1, true, 0f);
            AssertTier(QualityTier.High, 1f, 50f, 4, 2048, 2, true, 0f);
        }

        [Test]
        public void Settings_NeverGetHeavierAsTheTierGoesDown()
        {
            for (int i = 1; i < QualityTiers.All.Count; i++)
            {
                QualityTier lighter = QualityTiers.All[i - 1];
                QualityTier heavier = QualityTiers.All[i];
                QualityTierSettings a = QualityTiers.Settings(lighter);
                QualityTierSettings b = QualityTiers.Settings(heavier);
                string pair = lighter + " → " + heavier;

                Assert.LessOrEqual(a.Pipeline.RenderScale, b.Pipeline.RenderScale, pair + " renderScale");
                Assert.LessOrEqual(a.Pipeline.ShadowDistance, b.Pipeline.ShadowDistance, pair + " 影の距離");
                Assert.LessOrEqual(a.Pipeline.ShadowCascades, b.Pipeline.ShadowCascades, pair + " カスケード");
                Assert.LessOrEqual(a.Pipeline.ShadowResolution, b.Pipeline.ShadowResolution, pair + " シャドウマップ");
                Assert.LessOrEqual(a.Pipeline.MsaaSamples, b.Pipeline.MsaaSamples, pair + " MSAA");
                Assert.IsTrue(!a.PostProcessing || b.PostProcessing, pair + " 軽い段だけポストプロセスを描いている");
                Assert.IsTrue(b.TreeDrawDistance <= 0f
                              || (a.TreeDrawDistance > 0f && a.TreeDrawDistance <= b.TreeDrawDistance),
                              pair + " 軽い段のほうが木を遠くまで描いている");
                Assert.IsFalse(a.Pipeline.Equals(b.Pipeline) && a.PostProcessing == b.PostProcessing
                               && a.TreeDrawDistance.Equals(b.TreeDrawDistance), pair + " が同じ値");
            }
        }

        [Test]
        public void Settings_UseValuesUrpAcceptsAndKeepsInTheWebBuild()
        {
            foreach (QualityTier tier in QualityTiers.All)
            {
                PipelineQuality p = QualityTiers.Settings(tier).Pipeline;
                Assert.That(p.RenderScale, Is.GreaterThan(0.1f).And.LessThanOrEqualTo(2f), tier + " renderScale");
                Assert.That(p.ShadowDistance, Is.GreaterThan(0f), tier + " 影を切ると Web 版で削られた変種が要る");
                Assert.That(p.ShadowCascades, Is.InRange(1, 4), tier + " カスケード");
                CollectionAssert.Contains(ShadowResolutions, p.ShadowResolution, tier + " シャドウマップ");
                CollectionAssert.Contains(MsaaSamples, p.MsaaSamples, tier + " MSAA");
                if (p.RenderScale < 1f)
                {
                    // 拡大の倍率がちょうど整数だと URP は最近傍（Point）で拡大し, その変種は Web 版から削られている。
                    float upscale = 1f / p.RenderScale;
                    Assert.Greater(Mathf.Abs(upscale - Mathf.Round(upscale)), 0.05f,
                                   tier + " の拡大がちょうど " + Mathf.Round(upscale) + " 倍");
                }
            }
        }

        [Test]
        public void DefaultFor_WebIsMediumAndDesktopIsHigh()
        {
            Assert.AreEqual(QualityTier.Medium, QualityTiers.DefaultFor(RuntimePlatform.WebGLPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.DefaultFor(RuntimePlatform.WindowsPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.DefaultFor(RuntimePlatform.OSXPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.DefaultFor(RuntimePlatform.LinuxPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.DefaultFor(RuntimePlatform.WindowsEditor));
        }

        [Test]
        public void SaveAndLoad_RoundTripEveryTier()
        {
            foreach (QualityTier tier in QualityTiers.All)
            {
                QualityTiers.Save(tier);

                Assert.AreEqual(QualityTiers.Key(tier), PlayerPrefs.GetString(QualityTiers.PrefKey), tier + " の保存");
                Assert.AreEqual(tier, QualityTiers.Load(RuntimePlatform.WebGLPlayer), tier + " を Web 版で読む");
                Assert.AreEqual(tier, QualityTiers.Load(RuntimePlatform.WindowsPlayer), tier + " を Windows 版で読む");
            }
        }

        [Test]
        public void Load_WithoutASavedTier_UsesThePlatformDefault()
        {
            PlayerPrefs.DeleteKey(QualityTiers.PrefKey);

            Assert.AreEqual(QualityTier.Medium, QualityTiers.Load(RuntimePlatform.WebGLPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.Load(RuntimePlatform.WindowsPlayer));
        }

        [TestCase("")]
        [TestCase("ultra")]
        [TestCase("High")]
        [TestCase("2")]
        public void Load_WithAnUnknownValue_UsesThePlatformDefault(string stored)
        {
            PlayerPrefs.SetString(QualityTiers.PrefKey, stored);

            Assert.AreEqual(QualityTier.Medium, QualityTiers.Load(RuntimePlatform.WebGLPlayer));
            Assert.AreEqual(QualityTier.High, QualityTiers.Load(RuntimePlatform.WindowsPlayer));
        }

        [Test]
        public void KeyAndTryParse_RoundTrip()
        {
            var keys = new HashSet<string>();
            foreach (QualityTier tier in QualityTiers.All)
            {
                string key = QualityTiers.Key(tier);
                Assert.IsTrue(keys.Add(key), key + " が重なっている");
                Assert.IsTrue(QualityTiers.TryParse(key, out QualityTier parsed), key);
                Assert.AreEqual(tier, parsed, key);
            }

            Assert.IsFalse(QualityTiers.TryParse(null, out _));
            Assert.IsFalse(QualityTiers.TryParse("LOW", out _));
        }

        [Test]
        public void Step_MovesOneTierAndWrapsAround()
        {
            Assert.AreEqual(QualityTier.Medium, QualityTiers.Step(QualityTier.Low, 1));
            Assert.AreEqual(QualityTier.High, QualityTiers.Step(QualityTier.Medium, 1));
            Assert.AreEqual(QualityTier.Low, QualityTiers.Step(QualityTier.High, 1), "決定キーで回すと端から戻る");
            Assert.AreEqual(QualityTier.High, QualityTiers.Step(QualityTier.Low, -1));
            Assert.AreEqual(QualityTier.Low, QualityTiers.Step(QualityTier.Medium, -1));
            Assert.AreEqual(QualityTier.High, QualityTiers.Step(QualityTier.Medium, 3), "入力の大きさによらず 1 段");
            Assert.AreEqual(QualityTier.Medium, QualityTiers.Step(QualityTier.Medium, 0));
        }

        [TestCase("Data/Localization/ja.json")]
        [TestCase("Data/Localization/en.json")]
        [TestCase("Resources/KCD/Localization/ja.json")]
        [TestCase("Resources/KCD/Localization/en.json")]
        public void Localization_HasTheQualityRowAndEveryTierName(string relativePath)
        {
            Dictionary<string, object> strings = ReadStrings(relativePath);
            var keys = new List<string> { "ui.settings.quality" };
            foreach (QualityTier tier in QualityTiers.All)
            {
                keys.Add(QualityTiers.LabelKey(tier));
            }

            foreach (string key in keys)
            {
                Assert.IsTrue(strings.ContainsKey(key), relativePath + " に " + key + " が無い");
                Assert.IsNotEmpty(strings[key] as string, relativePath + " の " + key + " が空");
            }
        }

        private static void AssertTier(QualityTier tier, float renderScale, float shadowDistance, int cascades,
                                       int shadowResolution, int msaa, bool postProcessing, float treeDistance)
        {
            QualityTierSettings s = QualityTiers.Settings(tier);
            Assert.AreEqual(renderScale, s.Pipeline.RenderScale, 1e-6f, tier + " renderScale");
            Assert.AreEqual(shadowDistance, s.Pipeline.ShadowDistance, 1e-6f, tier + " 影の距離");
            Assert.AreEqual(cascades, s.Pipeline.ShadowCascades, tier + " カスケード");
            Assert.AreEqual(shadowResolution, s.Pipeline.ShadowResolution, tier + " シャドウマップ");
            Assert.AreEqual(msaa, s.Pipeline.MsaaSamples, tier + " MSAA");
            Assert.AreEqual(postProcessing, s.PostProcessing, tier + " ポストプロセス");
            Assert.AreEqual(treeDistance, s.TreeDrawDistance, 1e-6f, tier + " 木の距離");
        }

        private static Dictionary<string, object> ReadStrings(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), path + " が無い");

            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " をオブジェクトとして読めない");

            var strings = root["strings"] as Dictionary<string, object>;
            Assert.IsNotNull(strings, path + " に strings が無い");
            return strings;
        }
    }
}
