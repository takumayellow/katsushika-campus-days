using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// コードが L.Get / L.Format に渡す辞書キーが ja / en の両方に、しかも Data と Resources/KCD の両方にあること (#17)。
    /// キーが無いと L は fallback（多くは日本語）かキー名そのものを出すので、英語表示に日本語やキー名が漏れる。
    /// 第1引数がリテラルでない呼び出しは DynamicKeyCalls に理由つきで載せ、キーの出どころはデータ側の検査で見る。
    /// </summary>
    public sealed class LocalizationKeysTests
    {
        private static readonly string[] DictionaryFolders = { "Data/Localization", "Resources/KCD/Localization" };

        private static readonly string[] Locales = { "ja", "en" };

        /// <summary>
        /// 第1引数がリテラルでない呼び出し。ファイル名、式（空白は 1 つに詰める）、そのキーがどこで担保されているか。
        /// ここに無い呼び出しが増えるとテストが落ちる。キーの出どころを確かめる検査と一緒に足す。
        /// </summary>
        private static readonly (string File, string Expression, string Reason)[] DynamicKeyCalls =
        {
            ("AudioManager.Scene.cs", "label",
                "ChimeLabelKey が返すリテラル ui.hud.chime_noon / ui.hud.chime_evening。KeyShapedLiterals_ExistInEveryDictionary が見る"),
            ("CollectibleEntries.cs", "\"item.\" + Id + \".name\"",
                "collectibles.json の収集物。CollectibleLocalizationTests が全件を見る"),
            ("CollectibleEntries.cs", "\"photo.\" + Id + \".name\"",
                "collectibles.json の写真スポット。CollectibleLocalizationTests が全件を見る"),
            ("CollectibleEntries.cs", "\"ach.\" + Id + \".name\"",
                "collectibles.json の称号。CollectibleLocalizationTests が全件を見る"),
            ("DialogueData.cs", "SpeakerKey",
                "ui.npc.<会話データの id> と ui.npc.self。Dialogue_EverySpeakerHasANameInEveryDictionary が見る"),
            ("CharacterSelect.cs", "\"ui.select.\" + id + \".name\"",
                "GameManager.PlayableCharacterIds。OtherKeySources_ExistInEveryDictionary が見る"),
            ("CharacterSelect.cs", "\"ui.select.\" + id + \".desc\"",
                "GameManager.PlayableCharacterIds。OtherKeySources_ExistInEveryDictionary が見る"),
            ("MapAttribution.cs", "Key",
                "定数 ui.credits.map。KeyShapedLiterals_ExistInEveryDictionary が見る"),
            ("PauseMenu.cs", "EntryKeys[i]",
                "EntryKeys のリテラル ui.pause.*。KeyShapedLiterals_ExistInEveryDictionary が見る"),
            ("QuestTrackerView.cs", "\"ui.npc.\" + step.Giver",
                "クエストの giver。Quests_GiversAndTargetsHaveNamesInEveryDictionary が見る"),
            ("ResultScreen.cs", "ChoiceKeys[i]",
                "ChoiceKeys のリテラル ui.result.*。KeyShapedLiterals_ExistInEveryDictionary が見る"),
            ("SettingsView.cs", "\"ui.settings.language.\" + L.Locale",
                "L.Locale は ja / en。OtherKeySources_ExistInEveryDictionary が見る"),
            ("CollectableItem.cs", "\"ui.item.\" + itemId",
                "隠しアイテムでない拾い物（クエストの collect 目標）。Quests_GiversAndTargetsHaveNamesInEveryDictionary が見る"),
            ("DormEntrance.cs", "\"ui.building.\" + _buildingId",
                "屋内のある建物（Models/Interiors/<id>.json）。OtherKeySources_ExistInEveryDictionary が見る"),
            ("EntranceTrigger.cs", "\"ui.building.\" + _buildingId",
                "屋内のある建物（Models/Interiors/<id>.json）。OtherKeySources_ExistInEveryDictionary が見る"),
            ("InteriorLoader.cs", "\"ui.building.\" + entry.Id",
                "屋内のある建物（Models/Interiors/<id>.json）。OtherKeySources_ExistInEveryDictionary が見る"),
            ("VisitZone.cs", "\"ui.place.\" + placeId",
                "クエストの visit 目標。写真スポットは photo.*.name を、表示名の無い屋内 poi_* はトーストを出さない。" +
                "残りは Quests_GiversAndTargetsHaveNamesInEveryDictionary が見る"),
            ("WorldBounds.cs", "ToastKey",
                "定数 ui.hud.returned_to_campus。KeyShapedLiterals_ExistInEveryDictionary が見る")
        };

        private static readonly Regex CallPattern = new Regex(@"\bL\s*\.\s*(Get|Format)\s*\(");

        private static readonly Regex StringLiteral = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"$");

        private static readonly Regex LeadingLiteral = new Regex("^\"([^\"\\\\]*)\"\\s*\\+");

        /// <summary>辞書キーの形をした文字列（ui.〜）。連結の一部でなければ、それ自体がキーとして使われる。</summary>
        private static readonly Regex KeyShape = new Regex(@"^ui\.[A-Za-z0-9_.]*[A-Za-z0-9_]$");

        [Test]
        public void LiteralKeys_ExistInEveryDictionary()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            var missing = new SortedSet<string>();
            var keys = new List<string>();

            foreach (KeyCall call in ScanScripts().SelectMany(file => file.Calls))
            {
                Match literal = StringLiteral.Match(call.Argument);
                if (!literal.Success)
                {
                    continue;
                }

                string key = literal.Groups[1].Value;
                keys.Add(key);
                Require(dictionaries, key, call.Where, missing);
            }

            Assert.Greater(keys.Count, 0, "L.Get / L.Format の呼び出しを 1 つも拾えていない（走査が壊れている）");
            CollectionAssert.Contains(keys, "ui.interact.sit", "SeatInteractable の L.Get(\"ui.interact.sit\") を拾えていない");
            Assert.IsEmpty(missing, "辞書に無いキー:\n" + string.Join("\n", missing));
        }

        [Test]
        public void DynamicKeys_AreListedWithWhereTheyAreChecked()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            var unlisted = new List<string>();
            var used = new HashSet<int>();

            foreach (KeyCall call in ScanScripts().SelectMany(file => file.Calls))
            {
                if (StringLiteral.IsMatch(call.Argument))
                {
                    continue;
                }

                int index = Array.FindIndex(DynamicKeyCalls,
                    entry => entry.File == call.File && entry.Expression == call.Argument);
                if (index < 0)
                {
                    unlisted.Add(call.Where + ": " + call.Argument);
                    continue;
                }

                used.Add(index);
            }

            Assert.IsEmpty(unlisted,
                "第1引数がリテラルでない呼び出しが DynamicKeyCalls に無い。キーの出どころを確かめる検査と一緒に理由を書いて足す:\n" +
                string.Join("\n", unlisted));

            var stale = new List<string>();
            var noPrefix = new SortedSet<string>();
            for (int i = 0; i < DynamicKeyCalls.Length; i++)
            {
                (string file, string expression, string reason) = DynamicKeyCalls[i];
                Assert.IsNotEmpty(reason, file + " の " + expression + " に理由が無い");
                if (!used.Contains(i))
                {
                    stale.Add(file + ": " + expression);
                }

                Match prefix = LeadingLiteral.Match(expression);
                if (!prefix.Success)
                {
                    continue;
                }

                foreach (KeyValuePair<string, Dictionary<string, object>> dictionary in dictionaries)
                {
                    if (!dictionary.Value.Keys.Any(key => key.StartsWith(prefix.Groups[1].Value, StringComparison.Ordinal)))
                    {
                        noPrefix.Add(dictionary.Key + " に " + prefix.Groups[1].Value + "* のキーが 1 つも無い（" + file + "）");
                    }
                }
            }

            Assert.IsEmpty(stale, "もうコードに無い呼び出しが DynamicKeyCalls に残っている:\n" + string.Join("\n", stale));
            Assert.IsEmpty(noPrefix, string.Join("\n", noPrefix));
        }

        [Test]
        public void KeyShapedLiterals_ExistInEveryDictionary()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            var missing = new SortedSet<string>();
            int checkedCount = 0;

            foreach (ScannedFile file in ScanScripts())
            {
                foreach (Literal literal in file.Literals)
                {
                    if (!KeyShape.IsMatch(literal.Content) || file.IsConcatenated(literal))
                    {
                        continue;
                    }

                    checkedCount++;
                    Require(dictionaries, literal.Content, file.Name + ":" + file.LineOf(literal.Start), missing);
                }
            }

            Assert.Greater(checkedCount, 0, "ui.〜 の文字列を 1 つも拾えていない（走査が壊れている）");
            Assert.IsEmpty(missing, "コードに書いてあるキーが辞書に無い:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Dialogue_EverySpeakerHasANameInEveryDictionary()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            var missing = new SortedSet<string>();
            string[] files = Directory.GetFiles(Path.Combine(Application.dataPath, "Data", "Dialogue"), "*.json");
            Assert.Greater(files.Length, 0);

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                DialogueData data = DialogueData.FromJson(MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                Assert.IsNotNull(data, name + " から DialogueData を作れない");
                Require(dictionaries, DialogueData.SpeakerKeyPrefix + data.Id, name, missing);

                foreach (DialogueTopic topic in data.Topics)
                {
                    foreach (DialogueLine line in topic.Lines)
                    {
                        // 話者が空の行はプレイヤーの名前を出すので、辞書の名前は使わない。
                        if (string.IsNullOrEmpty(line.Speaker))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(line.SpeakerKey))
                        {
                            missing.Add(name + " / " + topic.Id + ": 話者「" + line.Speaker + "」に辞書キーが無く、英語でも日本語の名前が出る");
                            continue;
                        }

                        Require(dictionaries, line.SpeakerKey, name + " / " + topic.Id, missing);
                    }
                }
            }

            Require(dictionaries, DialogueData.SelfSpeakerKey, "プレイヤーの台詞", missing);
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void Quests_GiversAndTargetsHaveNamesInEveryDictionary()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            CollectibleCatalog catalog = CollectibleCatalog.Parse(
                File.ReadAllText(Path.Combine(Application.dataPath, "Data", "Collectibles", "collectibles.json")));
            var missing = new SortedSet<string>();
            string[] files = Directory.GetFiles(Path.Combine(Application.dataPath, "Data", "Quests"), "*.json");
            Assert.Greater(files.Length, 0);

            foreach (string file in files)
            {
                QuestData quest = QuestData.FromJson(MiniJson.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>);
                Assert.IsNotNull(quest, Path.GetFileName(file) + " から QuestData を作れない");

                // 再挑戦の案内（ui.hud.challenge_*）は giver の名前を出す。
                if (!string.IsNullOrEmpty(quest.Giver))
                {
                    Require(dictionaries, "ui.npc." + quest.Giver, quest.Id + " の giver", missing);
                }

                foreach (QuestStep step in quest.Steps)
                {
                    string where = quest.Id + " / " + step.Id;
                    if (step.Kind == QuestStepKind.Collect)
                    {
                        // 隠しアイテムは collectibles.json の名前で出る。それ以外の拾い物は ui.item.<id>。
                        CatalogItem item = catalog.FindItem(step.Target);
                        if (item == null || !item.IsHidden)
                        {
                            Require(dictionaries, "ui.item." + step.Target, where, missing);
                        }
                    }
                    else if (step.Kind == QuestStepKind.Visit)
                    {
                        // 写真スポットは photo.<id>.name で出る。屋内の poi_* は InteriorStage が表示名を空にするのでトーストを出さない。
                        if (catalog.FindPhotoSpot(step.Target) == null && !step.Target.StartsWith("poi_", StringComparison.Ordinal))
                        {
                            Require(dictionaries, "ui.place." + step.Target, where, missing);
                        }
                    }
                }
            }

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void OtherKeySources_ExistInEveryDictionary()
        {
            Dictionary<string, Dictionary<string, object>> dictionaries = LoadDictionaries();
            var missing = new SortedSet<string>();

            int buildings = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(Application.dataPath, "Models", "Interiors"), "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (id.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                buildings++;
                Require(dictionaries, "ui.building." + id, "Models/Interiors/" + id + ".json", missing);
            }

            Assert.Greater(buildings, 0, "Models/Interiors に建物が無い");

            foreach (string id in GameManager.PlayableCharacterIds)
            {
                Require(dictionaries, "ui.select." + id + ".name", "GameManager.PlayableCharacterIds", missing);
                Require(dictionaries, "ui.select." + id + ".desc", "GameManager.PlayableCharacterIds", missing);
            }

            foreach (string locale in Locales)
            {
                Require(dictionaries, "ui.settings.language." + locale, "SettingsView の言語名", missing);
            }

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void Scan_FindsOnlyRealCalls()
        {
            const string source =
                "// L.Get(\"ui.in_comment\")\n" +
                "/* L.Format(\"ui.in_block\") */\n" +
                "string a = \"L.Get(\\\"ui.in_string\\\")\";\n" +
                "string b = @\"L.Get(\"\"ui.in_verbatim\"\")\";\n" +
                "char c = '(';\n" +
                "string d = L.Get(\"ui.real\", \"x, y\");\n" +
                "string e = L.Format( \"ui.formatted\" ,\n    3);\n" +
                "string f = $\"{L.Get(\"ui.in_hole\")}!\";\n" +
                "string g = L.Get(Keys[Index(1, 2)], \"z\");\n" +
                "string h = L.Get(\"ui.prefix.\" + id);\n";

            var file = new ScannedFile("Sample.cs", source);

            CollectionAssert.AreEqual(
                new[] { "\"ui.real\"", "\"ui.formatted\"", "\"ui.in_hole\"", "Keys[Index(1, 2)]", "\"ui.prefix.\" + id" },
                file.Calls.Select(call => call.Argument).ToArray());
            CollectionAssert.AreEqual(new[] { 6, 7, 9, 10, 11 }, file.Calls.Select(call => call.Line).ToArray());

            Literal prefix = file.Literals.First(literal => literal.Content == "ui.prefix.");
            Literal real = file.Literals.First(literal => literal.Content == "ui.real");
            Assert.IsTrue(file.IsConcatenated(prefix));
            Assert.IsFalse(file.IsConcatenated(real));
        }

        private static Dictionary<string, Dictionary<string, object>> LoadDictionaries()
        {
            var dictionaries = new Dictionary<string, Dictionary<string, object>>();
            foreach (string folder in DictionaryFolders)
            {
                foreach (string locale in Locales)
                {
                    string name = folder + "/" + locale + ".json";
                    var root = MiniJson.Deserialize(File.ReadAllText(Path.Combine(Application.dataPath, folder, locale + ".json")))
                        as Dictionary<string, object>;
                    Assert.IsNotNull(root, name + " はオブジェクトとして読めない");
                    Dictionary<string, object> strings = MiniJson.GetObject(root, "strings");
                    Assert.IsNotNull(strings, name + " に strings が無い");
                    dictionaries.Add(name, strings);
                }
            }

            return dictionaries;
        }

        /// <summary>key が 4 つの辞書すべてに空でない文字列としてあるか。無ければ missing に足す。</summary>
        private static void Require(Dictionary<string, Dictionary<string, object>> dictionaries, string key, string where,
            SortedSet<string> missing)
        {
            foreach (KeyValuePair<string, Dictionary<string, object>> dictionary in dictionaries)
            {
                if (!dictionary.Value.TryGetValue(key, out object value) || string.IsNullOrEmpty(value as string))
                {
                    missing.Add(where + ": " + key + " が " + dictionary.Key + " に無い");
                }
            }
        }

        private static List<ScannedFile> ScanScripts()
        {
            string root = Path.Combine(Application.dataPath, "Scripts");
            string[] paths = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            Assert.Greater(paths.Length, 0, root + " に .cs が無い");
            return paths.Select(path => new ScannedFile(Path.GetFileName(path), File.ReadAllText(path))).ToList();
        }

        private readonly struct KeyCall
        {
            public KeyCall(string file, int line, string argument)
            {
                File = file;
                Line = line;
                Argument = argument;
            }

            public string File { get; }
            public int Line { get; }

            /// <summary>第1引数のソース（空白は 1 つに詰める）。</summary>
            public string Argument { get; }

            public string Where => File + ":" + Line;
        }

        /// <summary>文字列リテラル 1 つ。Start は開きの " の位置、End は閉じの " の次。Content は " の内側そのまま。</summary>
        private readonly struct Literal
        {
            public Literal(int start, int end, string content)
            {
                Start = start;
                End = end;
                Content = content;
            }

            public int Start { get; }
            public int End { get; }
            public string Content { get; }
        }

        /// <summary>
        /// .cs 1 本を字句だけ読む。Code はコメントを空白にしたもの、Masked はさらに文字列と文字の中身も空白にしたもの。
        /// どちらも元と同じ長さで改行の位置も同じなので、Masked で見つけた位置で Code を切り出せる。
        /// 補間文字列（$"..."）の {} の中はコードとして読む。
        /// </summary>
        private sealed class ScannedFile
        {
            private readonly string _text;
            private readonly char[] _code;
            private readonly char[] _masked;
            private readonly List<Literal> _literals = new List<Literal>();

            public ScannedFile(string name, string text)
            {
                Name = name;
                _text = text.Replace("\r\n", "\n");
                _code = _text.ToCharArray();
                _masked = _text.ToCharArray();
                ScanCode(0, false);
                Code = new string(_code);
                Masked = new string(_masked);
                Calls = FindCalls();
            }

            public string Name { get; }
            public string Code { get; }
            public string Masked { get; }
            public IReadOnlyList<Literal> Literals => _literals;
            public IReadOnlyList<KeyCall> Calls { get; }

            public int LineOf(int index)
            {
                int line = 1;
                for (int i = 0; i < index && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                    }
                }

                return line;
            }

            /// <summary>リテラルが + で前後とつながっているか（キーの接頭辞や接尾辞として使われているか）。</summary>
            public bool IsConcatenated(Literal literal)
            {
                int before = literal.Start - 1;
                while (before >= 0 && char.IsWhiteSpace(Masked[before]))
                {
                    before--;
                }

                int after = literal.End;
                while (after < Masked.Length && char.IsWhiteSpace(Masked[after]))
                {
                    after++;
                }

                return (before >= 0 && Masked[before] == '+') || (after < Masked.Length && Masked[after] == '+');
            }

            private List<KeyCall> FindCalls()
            {
                var calls = new List<KeyCall>();
                foreach (Match match in CallPattern.Matches(Masked))
                {
                    int start = match.Index + match.Length;
                    int end = EndOfArgument(start);
                    string argument = Regex.Replace(Code.Substring(start, end - start), @"\s+", " ").Trim();
                    calls.Add(new KeyCall(Name, LineOf(match.Index), argument));
                }

                return calls;
            }

            /// <summary>start から、括弧の外にある最初の , か ) の位置まで。</summary>
            private int EndOfArgument(int start)
            {
                int depth = 0;
                for (int i = start; i < Masked.Length; i++)
                {
                    char c = Masked[i];
                    if (c == '(' || c == '[' || c == '{')
                    {
                        depth++;
                    }
                    else if (c == ')' || c == ']' || c == '}')
                    {
                        if (depth == 0)
                        {
                            return i;
                        }

                        depth--;
                    }
                    else if ((c == ',' || c == ';') && depth == 0)
                    {
                        return i;
                    }
                }

                return Masked.Length;
            }

            /// <summary>コードを読む。nested なら補間の {} の中で、対応する } の位置を返す。</summary>
            private int ScanCode(int i, bool nested)
            {
                int depth = 0;
                int n = _text.Length;
                while (i < n)
                {
                    char c = _text[i];
                    char next = i + 1 < n ? _text[i + 1] : '\0';
                    if (c == '/' && next == '/')
                    {
                        int end = _text.IndexOf('\n', i);
                        end = end < 0 ? n : end;
                        Blank(_code, i, end);
                        Blank(_masked, i, end);
                        i = end;
                    }
                    else if (c == '/' && next == '*')
                    {
                        int end = _text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        end = end < 0 ? n : end + 2;
                        Blank(_code, i, end);
                        Blank(_masked, i, end);
                        i = end;
                    }
                    else if (c == '"' || ((c == '@' || c == '$') && StartsString(i)))
                    {
                        i = ScanString(i);
                    }
                    else if (c == '\'')
                    {
                        i = ScanChar(i);
                    }
                    else if (nested && c == '{')
                    {
                        depth++;
                        i++;
                    }
                    else if (nested && c == '}')
                    {
                        if (depth == 0)
                        {
                            return i;
                        }

                        depth--;
                        i++;
                    }
                    else
                    {
                        i++;
                    }
                }

                return n;
            }

            private bool StartsString(int i)
            {
                while (i < _text.Length && (_text[i] == '@' || _text[i] == '$'))
                {
                    i++;
                }

                return i < _text.Length && _text[i] == '"';
            }

            /// <summary>@ / $ の前置きから閉じの " までを読み、閉じの次の位置を返す。</summary>
            private int ScanString(int i)
            {
                int start = i;
                bool verbatim = false;
                bool interpolated = false;
                while (_text[i] == '@' || _text[i] == '$')
                {
                    verbatim |= _text[i] == '@';
                    interpolated |= _text[i] == '$';
                    i++;
                }

                i++;
                int contentStart = i;
                int n = _text.Length;
                while (i < n)
                {
                    char c = _text[i];
                    char next = i + 1 < n ? _text[i + 1] : '\0';
                    if (c == '"')
                    {
                        if (verbatim && next == '"')
                        {
                            Blank(_masked, i, i + 2);
                            i += 2;
                            continue;
                        }

                        if (!interpolated)
                        {
                            _literals.Add(new Literal(start, i + 1, _text.Substring(contentStart, i - contentStart)));
                        }

                        return i + 1;
                    }

                    if (!verbatim && c == '\n')
                    {
                        return i;
                    }

                    if (!verbatim && c == '\\')
                    {
                        Blank(_masked, i, Math.Min(i + 2, n));
                        i += 2;
                        continue;
                    }

                    if (interpolated && (c == '{' || c == '}') && next == c)
                    {
                        Blank(_masked, i, i + 2);
                        i += 2;
                        continue;
                    }

                    if (interpolated && c == '{')
                    {
                        i = ScanCode(i + 1, true) + 1;
                        continue;
                    }

                    Blank(_masked, i, i + 1);
                    i++;
                }

                return n;
            }

            private int ScanChar(int i)
            {
                int n = _text.Length;
                int j = i + 1;
                while (j < n && _text[j] != '\'' && _text[j] != '\n')
                {
                    j += _text[j] == '\\' ? 2 : 1;
                }

                j = Math.Min(j, n);
                Blank(_masked, i + 1, j);
                return j + 1;
            }

            private static void Blank(char[] chars, int from, int to)
            {
                for (int k = from; k < to && k < chars.Length; k++)
                {
                    if (chars[k] != '\n')
                    {
                        chars[k] = ' ';
                    }
                }
            }
        }
    }
}
