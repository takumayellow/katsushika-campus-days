using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 屋内のパレット（InteriorPalette の Surface）に載る面の .mat が、作ったときの仮の灰色 B0B0AC のまま
    /// 残っていないことを守る (#279)。
    ///
    /// .mat は一度できると MaterialLibrary.EnsureCampus が作り直さないので、パレットに色を足す前に
    /// できた .mat は灰色のまま固まる。講義棟の 30 面がこれで、パレットに実測の色を入れても
    /// ゲームではどの面も同じ灰色に見えていた。
    /// Editor のアセンブリは EditMode から参照できないので、PaletteAgreementTests と同じくソースを字面で読む。
    /// </summary>
    public sealed class InteriorPaletteMaterialTests
    {
        private const string Fallback = "B0B0AC";

        private static Dictionary<string, string> Surfaces()
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "Editor", "InteriorPalette.cs");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            var surfaces = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(
                File.ReadAllText(path), @"\{\s*""(?<n>[a-z0-9_]+)"",\s*new Surface\(""(?<hex>[0-9A-Fa-f]{6})"""))
            {
                surfaces[m.Groups["n"].Value] = m.Groups["hex"].Value.ToUpperInvariant();
            }

            Assert.Greater(surfaces.Count, 100, "InteriorPalette の Surface を読めていない");
            return surfaces;
        }

        /// <summary>.mat の _BaseColor を 8 bit の 16 進へ。無ければ null。</summary>
        private static string BaseColorHex(string matPath)
        {
            Match m = Regex.Match(File.ReadAllText(matPath),
                @"_BaseColor: \{r: (?<r>[-+\d.eE]+), g: (?<g>[-+\d.eE]+), b: (?<b>[-+\d.eE]+)");
            if (!m.Success)
            {
                return null;
            }

            int To8(string v) => Mathf.Clamp(Mathf.RoundToInt(float.Parse(v, CultureInfo.InvariantCulture) * 255f), 0, 255);
            return To8(m.Groups["r"].Value).ToString("X2") + To8(m.Groups["g"].Value).ToString("X2")
                + To8(m.Groups["b"].Value).ToString("X2");
        }

        [Test]
        public void NoPaletteSurfaceMaterialIsLeftAtTheFallbackGrey()
        {
            string folder = Path.Combine(Application.dataPath, "Materials", "Campus");
            var grey = new List<string>();
            int seen = 0;
            foreach (KeyValuePair<string, string> surface in Surfaces())
            {
                string path = Path.Combine(folder, surface.Key + ".mat");
                if (!File.Exists(path) || surface.Value == Fallback)
                {
                    continue;
                }

                string hex = BaseColorHex(path);
                if (hex == null)
                {
                    continue;
                }

                seen++;
                if (hex == Fallback)
                {
                    grey.Add(surface.Key + "（パレットは " + surface.Value + "）");
                }
            }

            Assert.Greater(seen, 100, "Materials/Campus の .mat の _BaseColor を読めていない");
            Assert.IsEmpty(grey, "仮の灰色 " + Fallback + " のままの面: " + string.Join(", ", grey));
        }
    }
}
