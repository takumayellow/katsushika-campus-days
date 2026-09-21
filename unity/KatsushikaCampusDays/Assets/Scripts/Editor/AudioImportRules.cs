using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Assets/Audio の取り込み規則（Assets/Audio/README.md の表）。
    /// BGM / Ambient は Streaming、SE は Decompress On Load。Vorbis で BGM 70 / Ambient 50 / SE 100。
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        private const string Root = "Assets/Audio/";

        public override uint GetVersion()
        {
            return 1;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            bool bgm = assetPath.StartsWith(Root + "BGM/");
            bool ambient = assetPath.StartsWith(Root + "Ambient/");
            bool streamed = bgm || ambient;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = streamed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = bgm ? 0.7f : ambient ? 0.5f : 1f;
            settings.preloadAudioData = !streamed;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = streamed;
        }
    }
}
