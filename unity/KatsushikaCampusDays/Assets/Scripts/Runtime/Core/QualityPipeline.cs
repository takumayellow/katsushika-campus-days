using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KCD
{
    /// <summary>
    /// URP のアセットの写しを作り, 画質の段の値を入れる (#70)。元のアセット（Assets/Settings/*_RPAsset）は変えない。
    ///
    /// 引数と戻り値は RenderPipelineAsset にしてある。EditMode テストのアセンブリは URP を参照しないので,
    /// URP の型は外に出さない。URP のアセットでなければ何もしない（false / null を返す）。
    /// </summary>
    public static class QualityPipeline
    {
        /// <summary>
        /// 写しを作る。名前は元と同じにし, 起動時のログ（[KCD] quality=... pipeline=...）の意味を変えない。
        /// 写しは保存しない（HideFlags.DontSave）。要らなくなったら呼んだ側が Destroy する。
        /// </summary>
        public static RenderPipelineAsset CreateClone(RenderPipelineAsset source)
        {
            if (!(source is UniversalRenderPipelineAsset))
            {
                return null;
            }

            RenderPipelineAsset clone = Object.Instantiate(source);
            clone.name = source.name;
            clone.hideFlags = HideFlags.DontSave;
            return clone;
        }

        /// <summary>アセットに段の値を入れる。URP のアセットでなければ false。</summary>
        public static bool Apply(RenderPipelineAsset asset, PipelineQuality quality)
        {
            if (!(asset is UniversalRenderPipelineAsset urp))
            {
                return false;
            }

            urp.renderScale = quality.RenderScale;
            urp.shadowDistance = quality.ShadowDistance;
            // 1〜4 の外を入れると URP が ArgumentException を投げる。表の値は 1 か 4。
            urp.shadowCascadeCount = Mathf.Clamp(quality.ShadowCascades, 1, 4);
            urp.mainLightShadowmapResolution = quality.ShadowResolution;
            urp.msaaSampleCount = quality.MsaaSamples;
            return true;
        }

        /// <summary>アセットの今の値。URP のアセットでなければ false。</summary>
        public static bool TryRead(RenderPipelineAsset asset, out PipelineQuality quality)
        {
            if (!(asset is UniversalRenderPipelineAsset urp))
            {
                quality = default;
                return false;
            }

            quality = new PipelineQuality(urp.renderScale, urp.shadowDistance, urp.shadowCascadeCount,
                                          urp.mainLightShadowmapResolution, urp.msaaSampleCount);
            return true;
        }

        /// <summary>アセットがすでにその値か。URP のアセットでなければ false。</summary>
        public static bool Matches(RenderPipelineAsset asset, PipelineQuality quality)
        {
            return TryRead(asset, out PipelineQuality current) && current.Approximately(quality);
        }
    }
}
