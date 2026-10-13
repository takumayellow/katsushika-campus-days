using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの輪郭線の殻が、画面の面に沿って押し出されることを守る (#47)。
    ///
    /// KCD/Toon の輪郭線は法線の向きへ押し出した殻の裏面。服に沿った薄い物（リボンの輪・セーラー襟）は
    /// 法線の向きのまま押すと殻の裏が奥の服の後ろへ回り、輪郭が物から離れた弧や、襟の V の底で交差した
    /// 線になる。MaterialLibrary は、輪郭線のあるキャラの材質に _OutlineScreenFlat = 1 を付ける。
    /// ここでは実際に保存されている .mat を突き合わせる。キャンパスの材質は法線の向きのまま。
    /// </summary>
    public sealed class CharacterOutlineScreenFlatTests
    {
        [System.Serializable]
        private sealed class PaletteEntry
        {
            public string name;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public PaletteEntry[] materials;
        }

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials");

        private static float ReadFloat(string text, string property, float missing)
        {
            Match m = Regex.Match(text, @"- " + property + @": (?<v>[-\d.eE]+)");
            return m.Success ? float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : missing;
        }

        [Test]
        public void 輪郭線のあるキャラの材質は殻を画面の面に沿って押し出す()
        {
            int outlined = 0;
            foreach (string palette in Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories))
            {
                string id = Path.GetFileName(Path.GetDirectoryName(palette));
                PaletteEntry[] entries = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(palette)).materials;
                Assert.IsNotNull(entries, palette + " に materials が無い");

                // palette.json は FBX が使う材質の一覧。顔テクスチャの face は載らないので足す
                foreach (string name in entries.Select(e => e.name).Append("face"))
                {
                    string path = Path.Combine(MaterialsFolder, "Characters", id + "_" + name + ".mat");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(path);
                    if (ReadFloat(text, "_OutlineWidth", 0f) <= 0f)
                    {
                        continue;
                    }

                    outlined++;
                    Assert.AreEqual(1f, ReadFloat(text, "_OutlineScreenFlat", 0f),
                        id + "_" + name + " の輪郭線が法線の向きのまま押し出される");
                }
            }

            Assert.Greater(outlined, 50, "輪郭線のあるキャラの材質が見つからない");
        }

        [Test]
        public void キャンパスの材質の輪郭線は法線の向きのまま()
        {
            string[] campus = Directory.GetFiles(Path.Combine(MaterialsFolder, "Campus"), "*.mat");
            Assert.IsNotEmpty(campus, "キャンパスの材質が見つからない");
            foreach (string path in campus)
            {
                Assert.AreEqual(0f, ReadFloat(File.ReadAllText(path), "_OutlineScreenFlat", 0f),
                    Path.GetFileName(path) + " の輪郭線が画面の面に沿って押し出される");
            }
        }
    }
}
