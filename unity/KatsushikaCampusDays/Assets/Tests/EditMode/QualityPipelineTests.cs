using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KCD.Tests
{
    /// <summary>
    /// 画質の段を URP のアセットに入れる部分 (#70)。
    ///
    /// 段を変えても Assets/Settings/*_RPAsset は書き換えず, 写しにだけ値を入れる。Medium と High は
    /// 今の Web 版・Windows 版のアセットと同じ値から作ってあり, アセットの値が変わったら表も見直す。
    /// QualitySettings.renderPipeline（ProjectSettings に残る）はテストでは触らない。
    /// </summary>
    public sealed class QualityPipelineTests
    {
        private const string MobileLevel = "Mobile";
        private const string PcLevel = "PC";

        [Test]
        public void QualityLevels_UseTheAssetsTheTiersWereBuiltFrom()
        {
            Assert.AreEqual("Mobile_RPAsset", LevelAsset(MobileLevel).name, "Web 版の品質レベルのアセット");
            Assert.AreEqual("PC_RPAsset", LevelAsset(PcLevel).name, "Windows 版の品質レベルのアセット");
        }

        [Test]
        public void Medium_IsTheWebAssetAsItIs()
        {
            // Web 版の既定は Medium。差し替えずに今の見た目のまま動くよう, Mobile_RPAsset と同じ値にしてある。
            PipelineQuality medium = QualityTiers.Settings(QualityTier.Medium).Pipeline;
            RenderPipelineAsset mobile = LevelAsset(MobileLevel);

            Assert.IsTrue(QualityPipeline.TryRead(mobile, out PipelineQuality current), "URP のアセットとして読めない");
            Assert.IsTrue(current.Approximately(medium), "Mobile_RPAsset " + current + " / Medium " + medium);
            Assert.IsTrue(QualityPipeline.Matches(mobile, medium));
        }

        [Test]
        public void High_IsTheDesktopAssetWithMsaa()
        {
            PipelineQuality high = QualityTiers.Settings(QualityTier.High).Pipeline;
            RenderPipelineAsset pc = LevelAsset(PcLevel);

            Assert.IsTrue(QualityPipeline.TryRead(pc, out PipelineQuality current), "URP のアセットとして読めない");
            var withHighMsaa = new PipelineQuality(current.RenderScale, current.ShadowDistance, current.ShadowCascades,
                                                   current.ShadowResolution, high.MsaaSamples);
            Assert.IsTrue(withHighMsaa.Approximately(high), "PC_RPAsset " + current + " / High " + high);
            Assert.Greater(high.MsaaSamples, current.MsaaSamples, "High は PC_RPAsset に MSAA を足した値");
        }

        [Test]
        public void CreateCloneAndApply_ChangeOnlyTheCopy()
        {
            foreach (string level in new[] { MobileLevel, PcLevel })
            {
                RenderPipelineAsset source = LevelAsset(level);
                string before = EditorJsonUtility.ToJson(source);
                bool dirtyBefore = EditorUtility.IsDirty(source);
                QualityPipeline.TryRead(source, out PipelineQuality original);

                RenderPipelineAsset clone = QualityPipeline.CreateClone(source);
                try
                {
                    Assert.IsNotNull(clone, level + " の写しが作れない");
                    Assert.AreNotSame(source, clone, level);
                    Assert.IsFalse(AssetDatabase.Contains(clone), level + " の写しがアセットになっている");
                    Assert.AreEqual(source.name, clone.name, level + " 起動時のログの名前を変えない");
                    Assert.IsTrue((clone.hideFlags & HideFlags.DontSave) == HideFlags.DontSave,
                                  level + " の写しが保存される");
                    Assert.IsTrue(QualityPipeline.Matches(clone, original), level + " の写しが元と違う値で始まる");

                    foreach (QualityTier tier in QualityTiers.All)
                    {
                        PipelineQuality wanted = QualityTiers.Settings(tier).Pipeline;
                        Assert.IsTrue(QualityPipeline.Apply(clone, wanted), level + " " + tier);
                        Assert.IsTrue(QualityPipeline.TryRead(clone, out PipelineQuality applied), level + " " + tier);
                        Assert.IsTrue(applied.Approximately(wanted), level + " " + tier + ": " + applied + " / " + wanted);
                    }

                    Assert.AreEqual(before, EditorJsonUtility.ToJson(source), level + " の元のアセットの値が変わった");
                    Assert.AreEqual(dirtyBefore, EditorUtility.IsDirty(source), level + " の元のアセットが変更扱いになった");
                    Assert.IsTrue(QualityPipeline.Matches(source, original), level);
                }
                finally
                {
                    if (clone != null)
                    {
                        Object.DestroyImmediate(clone);
                    }
                }
            }
        }

        [Test]
        public void NonUrpAssets_AreLeftAlone()
        {
            PipelineQuality low = QualityTiers.Settings(QualityTier.Low).Pipeline;

            Assert.IsNull(QualityPipeline.CreateClone(null));
            Assert.IsFalse(QualityPipeline.Apply(null, low));
            Assert.IsFalse(QualityPipeline.TryRead(null, out _));
            Assert.IsFalse(QualityPipeline.Matches(null, low));
        }

        [Test]
        public void Approximately_IgnoresRoundingButNotRealChanges()
        {
            var a = new PipelineQuality(0.8f, 50f, 1, 1024, 1);

            Assert.IsTrue(a.Approximately(new PipelineQuality(0.8f + 1e-7f, 50f, 1, 1024, 1)));
            Assert.IsFalse(a.Approximately(new PipelineQuality(0.7f, 50f, 1, 1024, 1)));
            Assert.IsFalse(a.Approximately(new PipelineQuality(0.8f, 30f, 1, 1024, 1)));
            Assert.IsFalse(a.Approximately(new PipelineQuality(0.8f, 50f, 4, 1024, 1)));
            Assert.IsFalse(a.Approximately(new PipelineQuality(0.8f, 50f, 1, 2048, 1)));
            Assert.IsFalse(a.Approximately(new PipelineQuality(0.8f, 50f, 1, 1024, 2)));
            Assert.AreEqual(a, new PipelineQuality(0.8f, 50f, 1, 1024, 1));
            Assert.AreEqual(a.GetHashCode(), new PipelineQuality(0.8f, 50f, 1, 1024, 1).GetHashCode());
        }

        /// <summary>品質レベルの名前から, そのレベルのアセット。</summary>
        private static RenderPipelineAsset LevelAsset(string levelName)
        {
            int index = Array.IndexOf(QualitySettings.names, levelName);
            Assert.GreaterOrEqual(index, 0, "品質レベル " + levelName + " が無い");
            RenderPipelineAsset asset = QualitySettings.GetRenderPipelineAssetAt(index);
            Assert.IsNotNull(asset, "品質レベル " + levelName + " にアセットが無い");
            return asset;
        }
    }
}
