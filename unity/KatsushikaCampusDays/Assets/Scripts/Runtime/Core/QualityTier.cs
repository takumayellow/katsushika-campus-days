using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>設定画面で選ぶ画質の段 (#70)。</summary>
    public enum QualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// URP のアセットに入れる値。どれも URP 17 のソースで, 実行中に変えてもその次のフレームから効き,
    /// WebGL のビルドで Shader の変種が削られていないことを確かめた（docs/WEBGL_BUDGET.md）。
    /// </summary>
    public readonly struct PipelineQuality : IEquatable<PipelineQuality>
    {
        public PipelineQuality(float renderScale, float shadowDistance, int shadowCascades,
                               int shadowResolution, int msaaSamples)
        {
            RenderScale = renderScale;
            ShadowDistance = shadowDistance;
            ShadowCascades = shadowCascades;
            ShadowResolution = shadowResolution;
            MsaaSamples = msaaSamples;
        }

        /// <summary>画面の解像度に掛ける倍率。1 未満は小さく描いて引き伸ばす。</summary>
        public float RenderScale { get; }

        /// <summary>主光源の影を描く距離 [m]。</summary>
        public float ShadowDistance { get; }

        /// <summary>影のカスケードの数（1〜4）。</summary>
        public int ShadowCascades { get; }

        /// <summary>主光源のシャドウマップの 1 辺 [px]。</summary>
        public int ShadowResolution { get; }

        /// <summary>MSAA のサンプル数（1 = なし, 2, 4, 8）。</summary>
        public int MsaaSamples { get; }

        /// <summary>小数の丸めの差を無視して同じか。アセットから読んだ値と比べるときに使う。</summary>
        public bool Approximately(PipelineQuality other)
        {
            return Mathf.Approximately(RenderScale, other.RenderScale)
                   && Mathf.Approximately(ShadowDistance, other.ShadowDistance)
                   && ShadowCascades == other.ShadowCascades
                   && ShadowResolution == other.ShadowResolution
                   && MsaaSamples == other.MsaaSamples;
        }

        public bool Equals(PipelineQuality other)
        {
            return RenderScale.Equals(other.RenderScale)
                   && ShadowDistance.Equals(other.ShadowDistance)
                   && ShadowCascades == other.ShadowCascades
                   && ShadowResolution == other.ShadowResolution
                   && MsaaSamples == other.MsaaSamples;
        }

        public override bool Equals(object obj) => obj is PipelineQuality other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(RenderScale, ShadowDistance, ShadowCascades, ShadowResolution, MsaaSamples);
        }

        public override string ToString()
        {
            return "renderScale=" + RenderScale + " shadow=" + ShadowDistance + "m/" + ShadowCascades
                   + "cascade/" + ShadowResolution + "px msaa=" + MsaaSamples;
        }
    }

    /// <summary>画質の段ごとの値。URP のアセットの値と, カメラのポストプロセスと, 木を描く距離。</summary>
    public readonly struct QualityTierSettings
    {
        public QualityTierSettings(PipelineQuality pipeline, bool postProcessing, float treeDrawDistance)
        {
            Pipeline = pipeline;
            PostProcessing = postProcessing;
            TreeDrawDistance = treeDrawDistance;
        }

        public PipelineQuality Pipeline { get; }

        /// <summary>カメラのポストプロセス（色調・ブルーム・周辺減光・FXAA）を描くか。</summary>
        public bool PostProcessing { get; }

        /// <summary>木のまとまりを描く距離 [m]。0 は制限なし（カメラの遠クリップまで描く）。</summary>
        public float TreeDrawDistance { get; }
    }

    /// <summary>
    /// 画質の段の表と, 選んだ段の保存 (#70)。値を決めた理由と実測は docs/WEBGL_BUDGET.md。
    ///
    /// Medium は Web 版の今の見た目（Mobile_RPAsset）と同じ値, High は Windows 版の今の見た目（PC_RPAsset）に
    /// MSAA 2x を足した値。表に無い項目（ソフトシャドウ・深度テクスチャ・HDR など）は, 起動した品質レベルの
    /// アセットの値のまま変えない。Web 版では Mobile_RPAsset に合わせて Shader の変種が削られているので,
    /// 削られた変種を要る項目（ソフトシャドウ）は段で動かさない。
    /// </summary>
    public static class QualityTiers
    {
        /// <summary>PlayerPrefs のキー。値は <see cref="Key"/> の文字列。</summary>
        public const string PrefKey = "KCD.QualityTier";

        private static readonly QualityTier[] s_All = { QualityTier.Low, QualityTier.Medium, QualityTier.High };

        /// <summary>軽い順の全段。</summary>
        public static IReadOnlyList<QualityTier> All => s_All;

        /// <summary>段の値。</summary>
        public static QualityTierSettings Settings(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low:
                    // 描く画素を Medium の 0.8² = 64% から 0.7² = 49% に減らす。0.5 にしないのは, URP の拡大が
                    // ちょうど半分のときに最近傍（Point）を選び, その変種がビルドから削られているため。
                    // 木は霧で 8 割見えている 300 m で切る（キャンパス内の平均で 37 まとまり中 2.8 を省く）。
                    return new QualityTierSettings(
                        new PipelineQuality(renderScale: 0.7f, shadowDistance: 30f, shadowCascades: 1,
                                            shadowResolution: 512, msaaSamples: 1),
                        postProcessing: false, treeDrawDistance: 300f);
                case QualityTier.High:
                    return new QualityTierSettings(
                        new PipelineQuality(renderScale: 1f, shadowDistance: 50f, shadowCascades: 4,
                                            shadowResolution: 2048, msaaSamples: 2),
                        postProcessing: true, treeDrawDistance: 0f);
                default:
                    return new QualityTierSettings(
                        new PipelineQuality(renderScale: 0.8f, shadowDistance: 50f, shadowCascades: 1,
                                            shadowResolution: 1024, msaaSamples: 1),
                        postProcessing: true, treeDrawDistance: 0f);
            }
        }

        /// <summary>設定を保存していないときの段。Web 版は Medium（今の見た目のまま）, それ以外は High。</summary>
        public static QualityTier DefaultFor(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WebGLPlayer ? QualityTier.Medium : QualityTier.High;
        }

        /// <summary>
        /// 既定の段を決めるときのプラットフォーム。エディタでビルド先を WebGL にしているときは Web 版と同じ扱いにし,
        /// Web 版の見た目をエディタで確かめられるようにする。
        /// </summary>
        public static RuntimePlatform TargetPlatform
        {
            get
            {
#if UNITY_WEBGL
                return RuntimePlatform.WebGLPlayer;
#else
                return Application.platform;
#endif
            }
        }

        /// <summary>保存とローカライズのキーに使う名前。</summary>
        public static string Key(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low:
                    return "low";
                case QualityTier.High:
                    return "high";
                default:
                    return "medium";
            }
        }

        /// <summary><see cref="Key"/> の文字列から段を読む。知らない文字列なら false。</summary>
        public static bool TryParse(string key, out QualityTier tier)
        {
            foreach (QualityTier candidate in s_All)
            {
                if (string.Equals(key, Key(candidate), StringComparison.Ordinal))
                {
                    tier = candidate;
                    return true;
                }
            }

            tier = QualityTier.Medium;
            return false;
        }

        /// <summary>保存した段。保存が無いか読めなければ, そのプラットフォームの既定。</summary>
        public static QualityTier Load(RuntimePlatform platform)
        {
            return TryParse(PlayerPrefs.GetString(PrefKey, string.Empty), out QualityTier tier)
                ? tier
                : DefaultFor(platform);
        }

        public static void Save(QualityTier tier)
        {
            PlayerPrefs.SetString(PrefKey, Key(tier));
            PlayerPrefs.Save();
        }

        /// <summary>左右の入力で次の段へ。端からは反対の端へ回る（決定キーで順に回せるように）。</summary>
        public static QualityTier Step(QualityTier tier, int direction)
        {
            int count = s_All.Length;
            int index = (((int)tier + Math.Sign(direction)) % count + count) % count;
            return s_All[index];
        }

        /// <summary>設定画面に出す名前のローカライズのキー。</summary>
        public static string LabelKey(QualityTier tier)
        {
            return "ui.settings.quality." + Key(tier);
        }
    }
}
