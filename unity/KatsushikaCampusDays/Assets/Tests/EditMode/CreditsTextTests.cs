using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// クレジット画面の文面が実際に入っている素材と合っていることを守る (#74)。
    ///
    /// 以前の文面は「音楽・効果音は数値合成のオリジナル」と書いていたが、実際のタイトル曲は東京理科大学校歌を
    /// 東北きりたん（NEUTRINO）が歌ったもので、昼・夕・夜・屋内の BGM も校歌のピアノ伴奏だった。
    /// 素材の権利表は docs/CREDITS.md にあり、画面の文面はそこから要点を抜いたもの。
    /// ここでは両方に同じ素材の名前が出ていることと、フォントのライセンス本文がビルドに入る場所にあることを見る。
    /// </summary>
    public sealed class CreditsTextTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>画面と docs/CREDITS.md の両方に出ているべき名前（どちらの言語でも同じ表記のもの）。</summary>
        private static readonly string[] SharedTerms =
        {
            "OpenStreetMap",
            "ODbL",
            "佐治巌",
            "大和憲史",
            "NEUTRINO",
            "Piano Sheet Converter",
            "numpy",
            "Noto Sans JP",
            "Liberation Sans",
            "SIL Open Font License 1.1",
            "StreamingAssets/Licenses",
        };

        /// <summary>
        /// docs/CREDITS.md にだけ出ているべき名前。沿道の建物の高さが PLATEAU 由来かはリポジトリに残るデータから
        /// 確かめられないので、画面には出さず CREDITS.md に「未確認」として書く。
        /// </summary>
        private static readonly string[] DocOnlyTerms = { "PLATEAU", "未確認", "docs/ref" };

        /// <summary>ビルドに入るフォントと、その OFL 本文の置き場所。フォントを足したらここと CREDITS.md に足す。</summary>
        private static readonly (string Font, string License, string Holder)[] ShippedFonts =
        {
            ("NotoSansJP-VF.ttf", "NotoSansJP-OFL.txt", "Adobe"),
            ("LiberationSans.ttf", "LiberationSans-OFL.txt", "Red Hat"),
        };

        /// <summary>言語を切り替えて本文を作り、PlayerPrefs の言語設定を元に戻す。</summary>
        internal static string TextIn(string locale)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale(locale);
                    Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
                    return CreditsView.BuildText();
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        [Test]
        public void Japanese_NamesTheRealMaterials()
        {
            string text = TextIn("ja");

            foreach (string term in SharedTerms.Concat(new[] { "東京理科大学校歌", "東北きりたん", "openstreetmap.org/copyright" }))
            {
                StringAssert.Contains(term, text, "クレジット（日本語）に「" + term + "」が無い");
            }

            StringAssert.DoesNotContain("数値合成のオリジナル", text, "BGM は校歌なので、音楽を数値合成と書いてはいけない");
            StringAssert.DoesNotContain("PLATEAU", text, "PLATEAU 由来かは確かめられていないので、画面では事実として書かない");
        }

        [Test]
        public void English_NamesTheRealMaterials()
        {
            string text = TextIn("en");

            foreach (string term in SharedTerms.Concat(new[] { "Tohoku Kiritan", "openstreetmap.org/copyright" }))
            {
                StringAssert.Contains(term, text, "Credits (English) lack \"" + term + "\"");
            }

            StringAssert.DoesNotContain("Music and sound effects are original", text,
                "the BGM is the school song, so the music must not be called synthesized");
            StringAssert.DoesNotContain("PLATEAU", text, "the PLATEAU origin is unverified, so the credits must not state it");
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void RichTextTags_AreBalanced(string locale)
        {
            string text = TextIn(locale);

            Assert.AreEqual(Regex.Matches(text, "<size=").Count, Regex.Matches(text, "</size>").Count,
                locale + ": <size> の開きと閉じの数が合わない");
            // TextMeshPro に </alpha> は無く、画面に文字のまま出る。濃さは <alpha=#FF> で戻す。
            StringAssert.DoesNotContain("</alpha>", text, locale + ": TextMeshPro に無い </alpha> を使っている");
            MatchCollection alphas = Regex.Matches(text, "<alpha=#([0-9A-Fa-f]{2})>");
            if (alphas.Count > 0)
            {
                Assert.AreEqual("FF", alphas[alphas.Count - 1].Groups[1].Value.ToUpperInvariant(),
                    locale + ": <alpha=#..> で薄くしたあと <alpha=#FF> で戻していない");
            }
        }

        [Test]
        public void CreditsDoc_ListsTheSameMaterials()
        {
            string path = Path.Combine(RepoRoot, "docs", "CREDITS.md");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            string doc = File.ReadAllText(path);

            foreach (string term in SharedTerms.Concat(DocOnlyTerms).Concat(new[] { "東京理科大学校歌", "東北きりたん" }))
            {
                StringAssert.Contains(term, doc, "docs/CREDITS.md に「" + term + "」が無い（画面の文面と合わせる）");
            }
        }

        [Test]
        public void FontLicenses_ShipInStreamingAssets()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Licenses");
            foreach ((string font, string license, string holder) in ShippedFonts)
            {
                string path = Path.Combine(dir, license);
                Assert.IsTrue(File.Exists(path), font + " の OFL 本文 " + path + " が無い");
                string body = File.ReadAllText(path);
                StringAssert.Contains("SIL OPEN FONT LICENSE Version 1.1", body, license + " に OFL の本文が無い");
                StringAssert.Contains(holder, body, license + " に著作権者（" + holder + "）の表示が無い");
            }
        }

        [Test]
        public void EveryFontFile_HasItsLicense()
        {
            string[] fonts = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFileName(p))
                .ToArray();

            Assert.IsNotEmpty(fonts, "Assets の下にフォントが 1 つも無い");
            foreach (string font in fonts)
            {
                Assert.IsTrue(ShippedFonts.Any(f => f.Font == font),
                    font + " のライセンス本文が StreamingAssets/Licenses に無い。ShippedFonts と docs/CREDITS.md に足す");
            }
        }
    }
}
