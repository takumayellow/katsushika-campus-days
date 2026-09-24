using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// Blender のパレット（blender/kcd_lib/mats.py）と Unity のパレット
    /// （MaterialLibrary.CampusColors）が同じ色を指していることを守る (#51)。
    ///
    /// FBX が Unity へ渡すのはマテリアルの「名前」だけで、実際の色は CampusColors が決める。
    /// mats.py の色は Blender のプレビューにしか出ない。両者がずれていると、
    /// プレビューを見て色を決めてもゲームでは別の色が出る。
    /// 実際、共通の 34 色のうち 28 色がずれていて、tus_green すら Blender では
    /// 青緑 #00BE86、Unity では理科大グリーン #00843D という状態だった。
    /// 木の葉の色を何度直してもゲームで変わらなかった原因のひとつ。
    ///
    /// 名前がどちらか一方にしか無いのは正しい（屋内専用の色、Blender のプレビュー専用の色）。
    /// ここで見るのは「両方にある名前」だけ。
    /// </summary>
    public sealed class PaletteAgreementTests
    {
        /// <summary>8 bit に戻したときのずれをどこまで許すか。丸めの往復で 1〜2 は動く。</summary>
        private const int Tolerance = 2;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>リニアの 0..1 を sRGB の 0..255 へ。Blender のノードの色はリニア。</summary>
        private static int ToSrgb8(float linear)
        {
            float s = linear <= 0.0031308f
                ? linear * 12.92f
                : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;
            return Mathf.Clamp(Mathf.RoundToInt(s * 255f), 0, 255);
        }

        /// <summary>mats.py の PALETTE を読む。値はリニアなので sRGB の 0..255 に直して返す。</summary>
        private static Dictionary<string, Vector3Int> BlenderPalette()
        {
            string path = Path.Combine(RepoRoot, "blender", "kcd_lib", "mats.py");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            var palette = new Dictionary<string, Vector3Int>();
            var pattern = new Regex(
                @"""(?<n>[a-z0-9_]+)"":\s*\(\((?<r>[\d.]+),\s*(?<g>[\d.]+),\s*(?<b>[\d.]+)\)");

            foreach (Match m in pattern.Matches(File.ReadAllText(path)))
            {
                palette[m.Groups["n"].Value] = new Vector3Int(
                    ToSrgb8(float.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture)),
                    ToSrgb8(float.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture)),
                    ToSrgb8(float.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture)));
            }

            Assert.Greater(palette.Count, 20, "mats.py の PALETTE を読めていない");
            return palette;
        }

        /// <summary>
        /// MaterialLibrary.CampusColors を読む。Editor のアセンブリは EditMode から参照できないので、
        /// ソースを字面で読む。木の葉と花壇のテストも同じものを使う。
        /// </summary>
        internal static Dictionary<string, Vector3Int> UnityPalette()
        {
            string path = Path.Combine(
                Application.dataPath, "Scripts", "Editor", "MaterialLibrary.cs");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            string source = File.ReadAllText(path);
            int start = source.IndexOf("CampusColors", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "CampusColors が見つからない");
            int end = source.IndexOf("};", start, System.StringComparison.Ordinal);

            var palette = new Dictionary<string, Vector3Int>();
            foreach (Match m in Regex.Matches(
                source.Substring(start, end - start), @"\{\s*""(?<n>[a-z0-9_]+)"",\s*""(?<hex>[0-9A-Fa-f]{6})""\s*\}"))
            {
                string hex = m.Groups["hex"].Value;
                palette[m.Groups["n"].Value] = new Vector3Int(
                    int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                    int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                    int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
            }

            Assert.Greater(palette.Count, 20, "CampusColors を読めていない");
            return palette;
        }

        [Test]
        public void ブレンダーとユニティのパレットが同じ色を指している()
        {
            Dictionary<string, Vector3Int> blender = BlenderPalette();
            Dictionary<string, Vector3Int> unity = UnityPalette();
            var mismatched = new List<string>();
            int shared = 0;

            foreach (KeyValuePair<string, Vector3Int> pair in unity)
            {
                if (!blender.TryGetValue(pair.Key, out Vector3Int mine))
                {
                    continue;
                }

                shared++;
                Vector3Int theirs = pair.Value;
                int worst = Mathf.Max(
                    Mathf.Abs(mine.x - theirs.x),
                    Mathf.Max(Mathf.Abs(mine.y - theirs.y), Mathf.Abs(mine.z - theirs.z)));
                if (worst > Tolerance)
                {
                    mismatched.Add(string.Format(
                        "{0}: mats.py #{1:X2}{2:X2}{3:X2} / CampusColors #{4:X2}{5:X2}{6:X2} (差 {7})",
                        pair.Key, mine.x, mine.y, mine.z, theirs.x, theirs.y, theirs.z, worst));
                }
            }

            Assert.Greater(shared, 20, "共通の名前が少なすぎる。どちらかの読み取りが壊れている");
            Assert.IsEmpty(
                mismatched,
                "Blender のプレビューとゲームで色が違う。プレビューを見て色を決められなくなる:\n"
                + string.Join("\n", mismatched));
        }

        [Test]
        public void 葉は芝より明るく幹は葉より暗い()
        {
            Dictionary<string, Vector3Int> unity = UnityPalette();

            // 相対輝度（sRGB のまま近似で足す）。葉が芝より暗いと、遠くの木が緑ではなく
            // 「暗い穴」に見える。幹が葉より明るいと、枝が明るい槍のように目立つ (#51)。
            float Luma(string name)
            {
                Assert.IsTrue(unity.ContainsKey(name), name + " が CampusColors に無い");
                Vector3Int c = unity[name];
                return (0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z) / 255f;
            }

            Assert.Less(Luma("trunk"), Luma("leaf"), "幹が葉より明るい。枝が明るい槍になって目立つ");
            Assert.Less(Luma("leaf_dark"), Luma("leaf"), "4 段ランプの並びが崩れている");
            Assert.Less(Luma("leaf"), Luma("leaf_light"), "4 段ランプの並びが崩れている");
            Assert.Less(Luma("leaf_light"), Luma("leaf_top"), "4 段ランプの並びが崩れている");
        }
    }
}
