using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの色が Blender（blender/kcd_chara/mats.py）とゲームで同じであることを守る (#55)。
    ///
    /// FBX が Unity へ渡すのはマテリアルの「名前」だけ。以前は Unity 側が名前の部分一致で
    /// 色を推測していて（MaterialLibrary.CharacterColor）、キャラの 31 色のうち 22 色が
    /// Blender と違い、まつ毛と眉は顔の線なのにベージュ #E8E4DC になっていた。
    /// 今は Blender が &lt;id&gt;/palette.json に色表を書き、MaterialLibrary.EnsureCharacter が
    /// それで .mat を塗る。ここでは palette.json と、実際に保存されている .mat の
    /// _BaseColor を突き合わせる（.mat を塗り直し忘れると落ちる）。
    ///
    /// 顔のテクスチャを貼るもの（face, eye_white, eye_l, eye_r）と輪郭線は palette.json に無い。
    /// </summary>
    public sealed class CharacterPaletteAgreementTests
    {
        /// <summary>8 bit に戻したときのずれをどこまで許すか。丸めの往復で 1〜2 は動く。</summary>
        private const int Tolerance = 2;

        [System.Serializable]
        private sealed class Entry
        {
            public string name;
            public string hex;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public Entry[] materials;
        }

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials", "Characters");

        private static string[] PaletteFiles()
        {
            return Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories);
        }

        private static Vector3Int ParseHex(string hex)
        {
            return new Vector3Int(
                int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }

        /// <summary>.mat（YAML）の _BaseColor を 0..255 で読む。マテリアルの色は sRGB のまま保存される。</summary>
        private static bool TryReadBaseColor(string matPath, out Vector3Int color)
        {
            color = default;
            Match m = Regex.Match(
                File.ReadAllText(matPath),
                @"- _BaseColor: \{r: (?<r>[-\d.eE]+), g: (?<g>[-\d.eE]+), b: (?<b>[-\d.eE]+)");
            if (!m.Success)
            {
                return false;
            }

            int To8(string v) => Mathf.Clamp(
                Mathf.RoundToInt(float.Parse(v, CultureInfo.InvariantCulture) * 255f), 0, 255);
            color = new Vector3Int(To8(m.Groups["r"].Value), To8(m.Groups["g"].Value), To8(m.Groups["b"].Value));
            return true;
        }

        [Test]
        public void 全キャラに色表がある()
        {
            foreach (string dir in Directory.GetDirectories(CharactersFolder))
            {
                if (Directory.GetFiles(dir, "*.fbx").Length == 0)
                {
                    continue;
                }

                Assert.IsTrue(
                    File.Exists(Path.Combine(dir, "palette.json")),
                    Path.GetFileName(dir) + "/palette.json が無い。blender/build_characters.py で作り直す");
            }
        }

        [Test]
        public void 色表とマテリアルの色が一致する()
        {
            string[] files = PaletteFiles();
            Assert.IsNotEmpty(files, "palette.json が 1 つも無い");
            var mismatched = new List<string>();
            int checkedCount = 0;

            foreach (string file in files)
            {
                string id = Path.GetFileName(Path.GetDirectoryName(file));
                PaletteFile palette = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file));
                Assert.IsNotNull(palette?.materials, file + " を読めない");
                Assert.IsNotEmpty(palette.materials, file + " が空");

                foreach (Entry entry in palette.materials)
                {
                    string mat = Path.Combine(MaterialsFolder, id + "_" + entry.name + ".mat");
                    if (!File.Exists(mat))
                    {
                        mismatched.Add(id + "_" + entry.name + ".mat が無い");
                        continue;
                    }

                    Assert.IsTrue(TryReadBaseColor(mat, out Vector3Int actual), mat + " に _BaseColor が無い");
                    Vector3Int declared = ParseHex(entry.hex);
                    checkedCount++;
                    int worst = Mathf.Max(
                        Mathf.Abs(declared.x - actual.x),
                        Mathf.Max(Mathf.Abs(declared.y - actual.y), Mathf.Abs(declared.z - actual.z)));
                    if (worst > Tolerance)
                    {
                        mismatched.Add(string.Format(
                            "{0}_{1}: palette.json #{2} / .mat #{3:X2}{4:X2}{5:X2} (差 {6})",
                            id, entry.name, entry.hex, actual.x, actual.y, actual.z, worst));
                    }
                }
            }

            Assert.Greater(checkedCount, 20, "突き合わせた色が少なすぎる。読み取りが壊れている");
            Assert.IsEmpty(
                mismatched,
                "Blender のプレビューとゲームでキャラの色が違う。SceneBuilder.BuildAll で塗り直す:\n"
                + string.Join("\n", mismatched));
        }

        [Test]
        public void まつ毛は肌より暗い()
        {
            // まつ毛は目の輪郭線。肌に近い明るさだと目元がぼやけて顔が読めなくなる。
            // 以前はまつ毛と眉がベージュ #E8E4DC で、肌とほとんど同じ明るさだった (#55)。
            // 眉は髪の色に合わせるので見ない（教授は白髪なので眉も白い）。
            float Luma(Vector3Int c) => (0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z) / 255f;

            foreach (string file in PaletteFiles())
            {
                string id = Path.GetFileName(Path.GetDirectoryName(file));
                var colors = new Dictionary<string, Vector3Int>();
                foreach (Entry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file)).materials)
                {
                    colors[entry.name] = ParseHex(entry.hex);
                }

                if (colors.TryGetValue("skin", out Vector3Int skin) && colors.TryGetValue("lash", out Vector3Int lash))
                {
                    Assert.Less(Luma(lash), Luma(skin) - 0.2f, id + " のまつ毛が肌に近すぎる");
                }
            }
        }
    }
}
