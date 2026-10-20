using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 遊ぶ人に見える文言に、実在の店の名前が出ないことを守る (#371)。
    ///
    /// 共創棟 1F の店とキャンパスの外の背景の店は、ゲームの文では「カフェ」「コンビニ」と書く。
    /// 見るのは、会話・クエスト・ローカライズなど Resources/KCD の JSON の文、campus.json の display と desc、
    /// クレジットの本文、画面に出る文字を持つコード（C#・タイトルのシーン・WebGL のテンプレート）。
    /// 語の一覧は建物の外の看板と同じ（CampusSignFaceTests.BrandIn）。
    /// 内部の ID（poi_kyoso_… など）と OSM の元の名前の欄（osm_name）は画面に出ないので見ない。
    /// </summary>
    public sealed class ShopNameTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>ゲームが実行時に読む JSON の置き場所（Assets/Data の写し。同じ中身かは DataBundleTests が見る）。</summary>
        private static string ResourcesRoot => Path.Combine(Application.dataPath, "Resources", "KCD");

        /// <summary>空振りで通らないように、文を 1 つ以上読めているはずのフォルダ（Resources/KCD の直下の全部）。</summary>
        private static readonly string[] ResourceFolders = { "Collectibles", "Dialogue", "Ending", "Localization", "Mobs", "Quests" };

        /// <summary>campus.json で画面に出る名前と説明の欄。osm_name は OSM の元の名前で、画面には使わない。</summary>
        private static readonly string[] CampusFields = { "display", "desc" };

        /// <summary>内部の ID（poi_kyoso_…、q_lunch など）。小文字の英数字を _ でつないだ、空白の無い名前。</summary>
        private const string IdPattern = "[a-z0-9]+(?:_[a-z0-9]+)+";

        private static readonly Regex InternalId = new Regex("^" + IdPattern + "$");
        private static readonly Regex InternalIdWord = new Regex(IdPattern);

        /// <summary>Unity のシーンは日本語を 4 桁の 16 進のエスケープで保存するので、文字に戻してから照合する。</summary>
        private static readonly Regex UnicodeEscape = new Regex(@"\\u([0-9A-Fa-f]{4})");

        [Test]
        public void ResourcesJson_NameNoRealShop()
        {
            var problems = new List<string>();
            var readPerFolder = new Dictionary<string, int>();
            foreach (string file in Directory.EnumerateFiles(ResourcesRoot, "*.json", SearchOption.AllDirectories))
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(file));
                List<(string At, string Text)> texts = TextsIn(file, keys: null);
                readPerFolder[folder] = (readPerFolder.TryGetValue(folder, out int before) ? before : 0) + texts.Count;
                AddProblems(folder + "/" + Path.GetFileName(file), texts, problems);
            }

            foreach (string folder in ResourceFolders)
            {
                readPerFolder.TryGetValue(folder, out int total);
                Assert.Greater(total, 0, "Resources/KCD/" + folder + " の文を 1 つも読めていない");
            }

            Assert.IsEmpty(problems, "Resources/KCD の JSON（会話・クエスト・ローカライズなど）に実在の店の名前がある"
                + "（「カフェ」「コンビニ」と書く。Assets/Data と Resources/KCD の両方を直す）:\n" + string.Join("\n", problems));
        }

        [Test]
        public void CampusJson_DisplayAndDescNameNoRealShop()
        {
            var copies = new[]
            {
                (Label: "data/osm/campus.json", Location: Path.Combine(RepoRoot, "data", "osm", "campus.json")),
                (Label: "Assets/Data/campus.json", Location: Path.Combine(Application.dataPath, "Data", "campus.json")),
            };

            var problems = new List<string>();
            foreach (var copy in copies)
            {
                Assert.IsTrue(File.Exists(copy.Location), copy.Label + " が無い");
                List<(string At, string Text)> texts = TextsIn(copy.Location, CampusFields);
                Assert.Greater(texts.Count, 0, copy.Label + " の display / desc を 1 つも読めていない");
                AddProblems(copy.Label, texts, problems);
            }

            Assert.IsEmpty(problems, "campus.json の display / desc に実在の店の名前がある（「カフェ」「コンビニ」と書く。"
                + "data/osm/campus.json と Assets/Data/campus.json の両方と tools/osm_extract.py を直す）:\n"
                + string.Join("\n", problems));
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void Credits_NameNoRealShop(string locale)
        {
            string credits = CreditsTextTests.TextIn(locale);
            Assert.IsNotEmpty(credits, "クレジット（" + locale + "）の本文を読めていない");

            string brand = CampusSignFaceTests.BrandIn(credits);
            Assert.IsNull(brand, "クレジット（" + locale + "）に実在の店の名前「" + brand + "」がある");
        }

        [Test]
        public void ScriptsTitleSceneAndWebTemplate_NameNoRealShop()
        {
            var problems = new List<string>();
            int scripts = AddFileProblems("Scripts", "*.cs", problems);
            int scenes = AddFileProblems("Scenes", "Title.unity", problems);
            int pages = AddFileProblems("WebGLTemplates", "*.html", problems)
                + AddFileProblems("WebGLTemplates", "*.css", problems)
                + AddFileProblems("WebGLTemplates", "*.webmanifest", problems);

            Assert.Greater(scripts, 0, "Assets/Scripts の C# を 1 つも読めていない");
            Assert.AreEqual(1, scenes, "Assets/Scenes/Title.unity を読めていない");
            Assert.Greater(pages, 0, "Assets/WebGLTemplates の html / css を 1 つも読めていない");
            Assert.IsEmpty(problems, "コード（C# のリテラルとコメント）・タイトルのシーン・WebGL のテンプレートに"
                + "実在の店の名前がある（「カフェ」「コンビニ」と書く）:\n" + string.Join("\n", problems));
        }

        /// <summary>texts のうち実在の店の名前を含むものを problems に足す。</summary>
        private static void AddProblems(string label, List<(string At, string Text)> texts, List<string> problems)
        {
            foreach ((string at, string text) in texts)
            {
                string brand = CampusSignFaceTests.BrandIn(text);
                if (brand != null)
                {
                    problems.Add(label + " " + at + " 「" + text + "」に " + brand);
                }
            }
        }

        /// <summary>
        /// Assets/folder の下で pattern に合うファイルのうち、実在の店の名前を含むものを problems に足す。
        /// 内部の ID は見ない。読んだファイルの数を返す。
        /// </summary>
        private static int AddFileProblems(string folder, string pattern, List<string> problems)
        {
            int read = 0;
            foreach (string file in Directory.EnumerateFiles(Path.Combine(Application.dataPath, folder), pattern, SearchOption.AllDirectories))
            {
                read++;
                string shown = InternalIdWord.Replace(UnicodeEscape.Replace(File.ReadAllText(file), FromEscape), " ");
                string brand = CampusSignFaceTests.BrandIn(shown);
                if (brand != null)
                {
                    problems.Add(file.Substring(Application.dataPath.Length + 1) + " に " + brand);
                }
            }

            return read;
        }

        private static string FromEscape(Match escape)
        {
            return ((char)Convert.ToInt32(escape.Groups[1].Value, 16)).ToString();
        }

        /// <summary>
        /// JSON ファイルの文字列を（場所, 文）の組で集める。keys が null なら全ての欄、渡したときはその名前の欄の文だけ。
        /// 内部の ID と空の文字列は含めない。
        /// </summary>
        private static List<(string At, string Text)> TextsIn(string file, string[] keys)
        {
            object root = MiniJson.Deserialize(File.ReadAllText(file));
            Assert.IsNotNull(root, file + " は JSON として読めない");

            var texts = new List<(string At, string Text)>();
            Collect(root, "", null, keys, texts);
            return texts;
        }

        private static void Collect(object node, string at, string key, string[] keys, List<(string At, string Text)> texts)
        {
            switch (node)
            {
                case string text:
                    if (text.Length > 0 && !InternalId.IsMatch(text) && (keys == null || Array.IndexOf(keys, key) >= 0))
                    {
                        texts.Add((at, text));
                    }

                    break;
                case Dictionary<string, object> map:
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        Collect(pair.Value, at + "/" + pair.Key, pair.Key, keys, texts);
                    }

                    break;
                case List<object> list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        Collect(list[i], at + "[" + i + "]", key, keys, texts);
                    }

                    break;
            }
        }
    }
}
