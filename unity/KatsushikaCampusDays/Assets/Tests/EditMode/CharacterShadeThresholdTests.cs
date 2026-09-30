using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの陰の境目と陰の色を守る (#47)。
    ///
    /// KCD/Toon の既定の境目 0.1（half-lambert）は光に対して 143° の所で、陰は光の真裏にしか出ない。キャラでは
    /// 後ろから見ると後頭部や背中に楕円の陰が浮いた。MaterialLibrary はキャラの材質の境目を 0.5（90°）にする。
    /// 顔と目は顔の見た目が決めるので変えない。輪郭（反転ハル）も変えない。
    ///
    /// 陰は「地の色 × _ShadeColor」で塗られる。_ShadeColor を地の色から作ると暗い色ほど二重に暗くなり、紺の
    /// スカートの陰が黒につぶれた。MaterialLibrary.ShadeOf は地の色の色味だけから陰の色を作る。
    /// このアセンブリは KCD.Editor を参照していないので、MaterialLibrary はリフレクションで呼ぶ。
    /// </summary>
    public sealed class CharacterShadeThresholdTests
    {
        [System.Serializable]
        private sealed class PaletteEntry
        {
            public string name;
            public string hex;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public PaletteEntry[] materials;
        }

        /// <summary>.mat の色は 8 bit 相当で比べる。</summary>
        private const float Tolerance = 2f / 255f;

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials");

        private static Type MaterialLibraryType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KCD.Editor")
                {
                    continue;
                }

                Type type = assembly.GetType("KCD.Editor.MaterialLibrary");
                Assert.IsNotNull(type, "KCD.Editor に MaterialLibrary が無い（名前を変えたらこのテストも直す）");
                return type;
            }

            Assert.Fail("KCD.Editor アセンブリが読み込まれていない");
            return null;
        }

        private static object LibraryField(string name)
        {
            FieldInfo field = MaterialLibraryType().GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "MaterialLibrary." + name + " が無い");
            return field.GetValue(null);
        }

        private static float CharacterShadeThreshold => (float)LibraryField("CharacterShadeThreshold");

        private static Color ShadeOf(Color color)
        {
            MethodInfo shadeOf = MaterialLibraryType().GetMethod("ShadeOf", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(shadeOf, "MaterialLibrary.ShadeOf が無い");
            return (Color)shadeOf.Invoke(null, new object[] { color });
        }

        private static float ReadFloat(string text, string property)
        {
            Match m = Regex.Match(text, @"- " + property + @": (?<v>[-\d.eE]+)");
            Assert.IsTrue(m.Success, property + " が無い");
            return float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>.mat に書かれていなければシェーダの既定値。</summary>
        private static float ReadFloatOrDefault(string text, string property, float shaderDefault)
        {
            return text.Contains("- " + property + ":") ? ReadFloat(text, property) : shaderDefault;
        }

        private static Color ReadColor(string text, string property)
        {
            Match m = Regex.Match(text,
                @"- " + property + @": \{r: (?<r>[-\d.eE]+), g: (?<g>[-\d.eE]+), b: (?<b>[-\d.eE]+)");
            Assert.IsTrue(m.Success, property + " が無い");
            return new Color(
                float.Parse(m.Groups["r"].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture));
        }

        private static Color ParseHex(string hex)
        {
            int value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        }

        private static void AssertSameColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, Tolerance, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, Tolerance, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, Tolerance, message + " (b)");
        }

        /// <summary>palette.json に載る材質のうち、.mat が保存されているもの。(キャラ_名前, .mat の中身, 地の色)。</summary>
        private static IEnumerable<(string label, string text, Color declared)> PaletteMaterials()
        {
            string[] palettes = Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories);
            Assert.IsNotEmpty(palettes, "palette.json が見つからない");
            foreach (string palette in palettes)
            {
                string id = Path.GetFileName(Path.GetDirectoryName(palette));
                PaletteEntry[] entries = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(palette)).materials;
                Assert.IsNotNull(entries, palette + " に materials が無い");
                int found = 0;
                foreach (PaletteEntry entry in entries)
                {
                    string path = Path.Combine(MaterialsFolder, "Characters", id + "_" + entry.name + ".mat");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    found++;
                    yield return (id + "_" + entry.name, File.ReadAllText(path), ParseHex(entry.hex));
                }

                Assert.Greater(found, 0, id + " の材質が見つからない");
            }
        }

        [Test]
        public void キャラの材質は光に対して90度の所で陰になる()
        {
            Assert.AreEqual(0.5f, CharacterShadeThreshold, 1e-6f, "half-lambert の 0.5 が光に対して 90°");
            foreach ((string label, string text, Color _) in PaletteMaterials())
            {
                Assert.AreEqual(CharacterShadeThreshold, ReadFloat(text, "_ShadeThreshold"), 1e-6f,
                    label + " の陰が光の真裏にしか出ない");
            }
        }

        [Test]
        public void 保存された顔と目と輪郭の陰の境目は変えない()
        {
            string[] faceTextured = ((string[])LibraryField("FaceTexturedNames")).Append("outline").ToArray();
            float shaderDefault = ShaderDefaultThreshold();
            int checkedCount = 0;
            foreach (string path in Directory.GetFiles(Path.Combine(MaterialsFolder, "Characters"), "*.mat"))
            {
                string stem = Path.GetFileNameWithoutExtension(path);
                foreach (string name in faceTextured)
                {
                    if (!stem.EndsWith("_" + name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    checkedCount++;
                    Assert.AreNotEqual(CharacterShadeThreshold,
                        ReadFloatOrDefault(File.ReadAllText(path), "_ShadeThreshold", shaderDefault),
                        stem + " の陰の境目までキャラの服と同じになった");
                }
            }

            Assert.Greater(checkedCount, 0, "顔と目と輪郭の材質が見つからない");
        }

        private static float ShaderDefaultThreshold()
        {
            Shader toon = Shader.Find("KCD/Toon");
            Assert.IsNotNull(toon, "KCD/Toon シェーダが見つからない");
            var material = new Material(toon);
            try
            {
                return material.GetFloat("_ShadeThreshold");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void 陰の境目は付け直しても変わらず顔と目と輪郭には付かない()
        {
            MethodInfo apply = MaterialLibraryType().GetMethod("ApplyShadeThreshold", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(apply, "MaterialLibrary.ApplyShadeThreshold が無い");
            Shader toon = Shader.Find("KCD/Toon");
            Assert.IsNotNull(toon, "KCD/Toon シェーダが見つからない");
            var material = new Material(toon);
            try
            {
                Assert.IsTrue((bool)apply.Invoke(null, new object[] { material, "cloth_blouse" }), "服に境目が付かない");
                Assert.AreEqual(CharacterShadeThreshold, material.GetFloat("_ShadeThreshold"), 1e-6f);
                Assert.IsFalse((bool)apply.Invoke(null, new object[] { material, "cloth_blouse" }),
                    "付け直すたびに材質が変わる");

                foreach (string name in ((string[])LibraryField("FaceTexturedNames")).Append("outline"))
                {
                    material.SetFloat("_ShadeThreshold", 0.1f);
                    Assert.IsFalse((bool)apply.Invoke(null, new object[] { material, name }), name + " の境目が変わる");
                    Assert.AreEqual(0.1f, material.GetFloat("_ShadeThreshold"), 1e-6f, name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void 追加ライトの境目は上限を付けて主光と分ける()
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Shaders", "KCD_Toon.shader"));
            int loop = text.IndexOf("GetAdditionalLight(lightIndex", StringComparison.Ordinal);
            Assert.GreaterOrEqual(loop, 0, "追加ライトのループが見つからない");
            int end = text.IndexOf("#endif", loop, StringComparison.Ordinal);
            string body = text.Substring(loop, end - loop);
            Assert.IsTrue(Regex.IsMatch(body, @"min\(_ShadeThreshold,\s*0\.1h?\)"),
                "追加ライトがキャラの主光の境目 (0.5) をそのまま使い、灯りが光の向きから 60° 以内の面にしか当たらない");
            Assert.IsFalse(Regex.IsMatch(body, @"smoothstep\(_ShadeThreshold\b"),
                "追加ライトの smoothstep が上限の無い _ShadeThreshold を使っている");
        }

        [Test]
        public void 陰の色は地の色の明るさによらない()
        {
            Color navy = ParseHex("26304E");
            AssertSameColor(ShadeOf(navy), ShadeOf(navy * 0.25f), "暗くした紺");
            Color navyShade = ShadeOf(navy);
            Assert.GreaterOrEqual(Mathf.Max(navyShade.r, Mathf.Max(navyShade.g, navyShade.b)), 0.7f,
                "紺の陰が黒につぶれる");
            Assert.Greater(navyShade.b, navyShade.r, "紺の陰が紺でなくなる");

            Color blackShade = ShadeOf(Color.black);
            Assert.IsFalse(float.IsNaN(blackShade.r) || float.IsNaN(blackShade.g) || float.IsNaN(blackShade.b),
                "黒の陰が NaN になる");
            AssertSameColor(ShadeOf(Color.white), blackShade, "黒の陰は白の陰と同じ（色味が無い）");
        }

        [Test]
        public void 保存された陰の色は地の色の色味から作る()
        {
            foreach ((string label, string text, Color declared) in PaletteMaterials())
            {
                AssertSameColor(ShadeOf(declared), ReadColor(text, "_ShadeColor"), label + " の陰の色が古い");
            }
        }

        [Test]
        public void キャンパスの材質の陰の境目は変えない()
        {
            int checkedCount = 0;
            foreach (string path in Directory.GetFiles(Path.Combine(MaterialsFolder, "Campus"), "*.mat"))
            {
                string text = File.ReadAllText(path);
                if (!text.Contains("- _ShadeThreshold:"))
                {
                    continue;
                }

                checkedCount++;
                Assert.AreNotEqual(CharacterShadeThreshold, ReadFloat(text, "_ShadeThreshold"),
                    Path.GetFileName(path) + " の陰の境目がキャラと同じになった");
            }

            Assert.Greater(checkedCount, 0, "キャンパスの KCD/Toon の材質が見つからない");
        }
    }
}
