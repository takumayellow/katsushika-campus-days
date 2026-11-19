using System;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Assets/Audio の取り込み規則（Assets/Audio/README.md の表）。
    /// <para>既定（Windows など）: 曲・ジングル・環境音は Streaming、SE は Decompress On Load。
    /// Vorbis で、曲とジングルは 0.7、環境音は 0.5、SE は 1.0。</para>
    /// <para>WebGL: 形式は WebGL の既定の AAC（前のビルドと同じ）。曲だけを Compressed In Memory にし、ほかは Decompress On Load にする。
    /// 環境音は、展開したバッファをサンプル単位でループさせる（圧縮のままだと、Firefox と Safari は &lt;audio&gt; 要素で鳴らし、
    /// ループのたびに間が空く）。ジングルと SE は、鳴らす前に読み終えておく（WebGL では読み終わるまで長さが 0 で、
    /// ジングル・チャイム・ピアノは長さを使って BGM を絞る）。</para>
    /// <para>1ch にはしない（Force To Mono を切る）。1ch のクリップを 2D で鳴らすと、左右が同じステレオより
    /// 左右とも 3 dB 小さくなる（#28 で計測）。</para>
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/Audio/";
        public const string WebGLPlatform = "WebGL";

        /// <summary>WebGL で書き出す標本化周波数（Hz）。</summary>
        public const uint WebGLSampleRate = 44100;

        public enum Role
        {
            Song,
            Jingle,
            Ambient,
            Effect,
        }

        public override uint GetVersion()
        {
            return 2;
        }

        public static Role RoleOf(string assetPath)
        {
            if (assetPath.StartsWith(Root + "BGM/jingle_", StringComparison.Ordinal))
            {
                return Role.Jingle;
            }

            if (assetPath.StartsWith(Root + "BGM/", StringComparison.Ordinal))
            {
                return Role.Song;
            }

            return assetPath.StartsWith(Root + "Ambient/", StringComparison.Ordinal) ? Role.Ambient : Role.Effect;
        }

        public static AudioImporterSampleSettings DefaultSettings(Role role, AudioImporterSampleSettings settings)
        {
            settings.loadType = role == Role.Effect ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = role == Role.Ambient ? 0.5f : role == Role.Effect ? 1f : 0.7f;
            settings.preloadAudioData = role == Role.Effect;
            return settings;
        }

        /// <summary>
        /// WebGL の上書き。品質は既定のまま（AAC のビットレートも品質で決まる）。
        /// 標本化周波数は 44.1 kHz に上書きする。WebGL は「元のまま」を受け付けず、その設定だと上書き自体が通らない。
        /// 44.1 kHz は WebGL の既定で、前のビルドと同じ。
        /// </summary>
        public static AudioImporterSampleSettings WebGLSettings(Role role, AudioImporterSampleSettings defaults)
        {
            AudioImporterSampleSettings web = defaults;
            web.loadType = role == Role.Song ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            web.compressionFormat = AudioCompressionFormat.AAC;
            web.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            web.sampleRateOverride = WebGLSampleRate;
            web.preloadAudioData = role == Role.Jingle || role == Role.Effect;
            return web;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            Role role = RoleOf(assetPath);
            AudioImporterSampleSettings defaults = DefaultSettings(role, importer.defaultSampleSettings);
            importer.defaultSampleSettings = defaults;
            if (!importer.SetOverrideSampleSettings(WebGLPlatform, WebGLSettings(role, defaults)))
            {
                Debug.LogWarning("AudioImportRules: WebGL の上書きを設定できない: " + assetPath);
            }

            importer.forceToMono = false;
            importer.loadInBackground = role != Role.Effect;
        }
    }
}
