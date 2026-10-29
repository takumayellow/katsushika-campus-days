using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// Assets/Audio の取り込み設定（#28）。取り込んだ結果を、役割の表（Assets/Audio/README.md）に照らす。
    /// </summary>
    public sealed class AudioImportRulesTests
    {
        private const string Root = "Assets/Audio/";
        private const string WebGL = "WebGL";

        private sealed class Role
        {
            public string Name;
            public AudioClipLoadType DefaultLoad;
            public bool DefaultPreload;
            public AudioClipLoadType WebLoad;
            public bool WebPreload;
            public float Quality;
            public bool Background;
        }

        private static readonly Role Song = new Role
        {
            Name = "曲",
            DefaultLoad = AudioClipLoadType.Streaming,
            DefaultPreload = false,
            WebLoad = AudioClipLoadType.CompressedInMemory,
            WebPreload = false,
            Quality = 0.7f,
            Background = true,
        };

        private static readonly Role Jingle = new Role
        {
            Name = "ジングル",
            DefaultLoad = AudioClipLoadType.Streaming,
            DefaultPreload = false,
            WebLoad = AudioClipLoadType.DecompressOnLoad,
            WebPreload = true,
            Quality = 0.7f,
            Background = true,
        };

        private static readonly Role Ambient = new Role
        {
            Name = "環境音",
            DefaultLoad = AudioClipLoadType.Streaming,
            DefaultPreload = false,
            WebLoad = AudioClipLoadType.DecompressOnLoad,
            WebPreload = false,
            Quality = 0.5f,
            Background = true,
        };

        private static readonly Role Effect = new Role
        {
            Name = "SE",
            DefaultLoad = AudioClipLoadType.DecompressOnLoad,
            DefaultPreload = true,
            WebLoad = AudioClipLoadType.DecompressOnLoad,
            WebPreload = true,
            Quality = 1f,
            Background = false,
        };

        [Test]
        public void EveryClip_HasTheDefaultAndWebGLSettingsOfItsRole()
        {
            var counts = new Dictionary<Role, int> { { Song, 0 }, { Jingle, 0 }, { Ambient, 0 }, { Effect, 0 } };
            foreach (string path in Clips())
            {
                Role role = RoleOf(path);
                counts[role]++;
                AudioImporter importer = ImporterOf(path);
                string label = path + "（" + role.Name + "）";

                AudioImporterSampleSettings normal = importer.defaultSampleSettings;
                Assert.AreEqual(role.DefaultLoad, normal.loadType, label + " の既定の Load Type");
                Assert.AreEqual(AudioCompressionFormat.Vorbis, normal.compressionFormat, label + " の既定の形式");
                Assert.AreEqual(role.Quality, normal.quality, 1e-4f, label + " の既定の品質");
                Assert.AreEqual(role.DefaultPreload, normal.preloadAudioData, label + " の既定の Preload");
                Assert.AreEqual(role.Background, importer.loadInBackground, label + " の Load In Background");
                Assert.IsFalse(importer.forceToMono, label + " を 1ch で取り込んでいる（2D で鳴らすと左右とも 3 dB 小さくなる）");

                Assert.IsTrue(importer.ContainsSampleSettingsOverride(WebGL), label + " に WebGL の上書きが無い");
                AudioImporterSampleSettings web = importer.GetOverrideSampleSettings(WebGL);
                Assert.AreEqual(role.WebLoad, web.loadType, label + " の WebGL の Load Type");
                Assert.AreEqual(AudioCompressionFormat.AAC, web.compressionFormat, label + " の WebGL の形式");
                Assert.AreEqual(role.Quality, web.quality, 1e-4f, label + " の WebGL の品質");
                Assert.AreEqual(role.WebPreload, web.preloadAudioData, label + " の WebGL の Preload");
                Assert.AreEqual(AudioSampleRateSetting.OverrideSampleRate, web.sampleRateSetting, label + " の WebGL の標本化周波数の設定");
                Assert.AreEqual(44100u, web.sampleRateOverride, label + " の WebGL の標本化周波数");
            }

            foreach (KeyValuePair<Role, int> count in counts)
            {
                Assert.Greater(count.Value, 0, count.Key.Name + " のクリップが 1 本も無い（パスの分け方が変わった？）");
            }
        }

        private static Role RoleOf(string path)
        {
            if (path.StartsWith(Root + "BGM/jingle_", StringComparison.Ordinal))
            {
                return Jingle;
            }

            if (path.StartsWith(Root + "BGM/", StringComparison.Ordinal))
            {
                return Song;
            }

            return path.StartsWith(Root + "Ambient/", StringComparison.Ordinal) ? Ambient : Effect;
        }

        private static List<string> Clips()
        {
            var clips = new List<string>();
            foreach (string file in Directory.GetFiles(Root, "*.wav", SearchOption.AllDirectories))
            {
                clips.Add(file.Replace('\\', '/'));
            }

            clips.Sort(StringComparer.Ordinal);
            Assert.IsNotEmpty(clips, Root + " に wav が無い");
            return clips;
        }

        private static AudioImporter ImporterOf(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            Assert.IsNotNull(importer, path + " の AudioImporter が無い");
            return importer;
        }
    }
}
