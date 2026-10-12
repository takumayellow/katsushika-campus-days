using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KCD.Tests
{
    /// <summary>
    /// 画面の文字のフォント KCD_JP に、ゲームの文字が欠けていないことを確かめる (#70)。
    /// KCD_JP の元は tools/make_ui_font.py が絞ったフォントで、フォールバックが無いので、欠けた字は豆腐になる。
    /// 見る文字は tools/make_ui_font.py の text_codepoints と同じ（Assets のテキストとその中のエスケープ、
    /// data/osm/campus.json の名前）。片方の集め方を変えたら、もう片方も揃える。
    /// </summary>
    public sealed class ScreenFontGlyphTests
    {
        /// <summary>FontLibrary.FontAssetPath と同じ。</summary>
        private const string ScreenFontAssetPath = "Assets/Fonts/KCD_JP.asset";

        /// <summary>tools/make_ui_font.py の元のフォント。</summary>
        private const string OriginalFontPath = "Assets/Fonts/NotoSansJP-VF.ttf";

        /// <summary>Assets の外で、画面に出る名前の元になるもの（make_ui_font.py の OSM_JSON）。</summary>
        private static readonly string OsmJson =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data", "osm", "campus.json"));

        private static readonly string[] TextSuffixes = { ".json", ".cs", ".txt", ".csv", ".unity", ".prefab", ".asset" };
        private static readonly string[] YamlSuffixes = { ".unity", ".prefab", ".asset" };

        /// <summary>JSON・C#・YAML の文字列のエスケープ（サロゲートの対、4 桁、C# の 8 桁）。</summary>
        private static readonly Regex Escape = new Regex(
            @"\\u([dD][89abAB][0-9a-fA-F]{2})\\u([dD][c-fC-F][0-9a-fA-F]{2})|\\u([0-9a-fA-F]{4})|\\U([0-9a-fA-F]{8})");

        private static void AddCodepoints(string text, HashSet<int> cps)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsSurrogatePair(text, i))
                {
                    cps.Add(char.ConvertToUtf32(text, i));
                    i++;
                }
                else if (!char.IsSurrogate(text[i]))
                {
                    cps.Add(text[i]);
                }
            }

            foreach (Match m in Escape.Matches(text))
            {
                if (m.Groups[1].Success)
                {
                    int high = Convert.ToInt32(m.Groups[1].Value, 16);
                    int low = Convert.ToInt32(m.Groups[2].Value, 16);
                    cps.Add(0x10000 + ((high - 0xD800) << 10) + (low - 0xDC00));
                }
                else
                {
                    cps.Add(Convert.ToInt32(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value, 16));
                }
            }
        }

        /// <summary>Assets のテキストと OsmJson に出てくる ASCII 以外の文字（制御文字とサロゲートを除く）。</summary>
        private static HashSet<int> GameCodepoints()
        {
            var cps = new HashSet<int>();
            foreach (string path in Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories))
            {
                string suffix = Path.GetExtension(path);
                if (Array.IndexOf(TextSuffixes, suffix) < 0)
                {
                    continue;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                if (Array.IndexOf(YamlSuffixes, suffix) >= 0 && !text.StartsWith("%YAML", StringComparison.Ordinal))
                {
                    continue; // バイナリで保存されたアセット（NavMesh など）
                }

                AddCodepoints(text, cps);
            }

            Assert.IsTrue(File.Exists(OsmJson), OsmJson);
            AddCodepoints(File.ReadAllText(OsmJson, Encoding.UTF8), cps);
            cps.RemoveWhere(cp => cp < 0xA0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF));
            return cps;
        }

        /// <summary>cps のうち font に字形がある文字。FontEngine の読み込んだフォントは全体で 1 つなので、最後に外す。</summary>
        private static HashSet<int> Drawable(Font font, IEnumerable<int> cps)
        {
            Assert.AreEqual(FontEngineError.Success, FontEngine.LoadFontFace(font), font.name + " を読めない");
            try
            {
                return new HashSet<int>(cps.Where(cp => FontEngine.TryGetGlyphIndex((uint)cp, out uint glyph) && glyph != 0));
            }
            finally
            {
                FontEngine.UnloadFontFace();
            }
        }

        [Test]
        public void ScreenFontHasEveryCharacterOfTheGame()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ScreenFontAssetPath);
            Assert.IsNotNull(asset, ScreenFontAssetPath);
            Assert.IsNotNull(asset.sourceFontFile, ScreenFontAssetPath + " の元のフォントが無い");
            var original = AssetDatabase.LoadAssetAtPath<Font>(OriginalFontPath);
            Assert.IsNotNull(original, OriginalFontPath);

            HashSet<int> game = Drawable(original, GameCodepoints());
            Assert.Greater(game.Count, 1000, "ゲームの文字を集められていない");
            game.ExceptWith(Drawable(asset.sourceFontFile, game));

            string missing = string.Concat(game.OrderBy(cp => cp).Take(40).Select(char.ConvertFromUtf32));
            Assert.IsEmpty(game, "KCD_JP に無い字がある（画面では豆腐になる）。" +
                                 "python tools/make_ui_font.py で作り直してコミットする: " + missing);
        }

        [Test]
        public void MissingCharactersAreLoggedAtRuntime()
        {
            Assert.IsFalse(TMP_Settings.warningsDisabled,
                "TMP Settings の警告を切ると、実行中に欠けた字が豆腐になってもログに出ない");
        }
    }
}
