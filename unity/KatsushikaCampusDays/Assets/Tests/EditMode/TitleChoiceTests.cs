using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// タイトルの「つづきから / はじめから」(#53)。
    ///
    /// 以前はセーブがあると「Enter でつづきから」と出しながら、Enter がキャラ選択（はじめから）へ進み、
    /// 決定した瞬間に進行が初期値に戻っていた。選択肢の並び・既定・表示・文言が、Enter で実際に
    /// 起きることと食い違わないことを見る。
    /// </summary>
    public sealed class TitleChoiceTests
    {
        private const string ContinueLabel = "つづきから";
        private const string NewGameLabel = "はじめから";

        [Test]
        public void WithSave_EnterRightAwayContinues()
        {
            // タイトルに入ったときの選択は 0。遊んだ人が戻ってきて Enter を押しても進行を消さない。
            Assert.AreEqual(TitleChoice.Continue, TitleMenu.ChoiceAt(true, 0));
        }

        [Test]
        public void WithSave_TheSecondChoiceIsNewGame()
        {
            Assert.AreEqual(2, TitleMenu.ChoiceCount(true));
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.ChoiceAt(true, 1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        public void WithoutSave_EveryChoiceIsNewGame(int index)
        {
            // 読めるセーブが無いのに「つづきから」を選べると、Enter を押しても何も起きない。
            Assert.AreEqual(1, TitleMenu.ChoiceCount(false));
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.ChoiceAt(false, index));
        }

        [TestCase(2)]
        [TestCase(-1)]
        [TestCase(99)]
        public void WithSave_OutOfRangeIsNewGame(int index)
        {
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.ChoiceAt(true, index));
        }

        [TestCase(0, 1, 2, 1)]
        [TestCase(1, 1, 2, 0)]
        [TestCase(0, -1, 2, 1)]
        [TestCase(1, -1, 2, 0)]
        [TestCase(1, 0, 2, 1)]
        [TestCase(0, 1, 1, 0)]
        [TestCase(0, -1, 1, 0)]
        [TestCase(1, -3, 2, 0)]
        [TestCase(0, 1, 0, 0)]
        public void MoveIndex_WrapsAround(int index, int step, int count, int expected)
        {
            Assert.AreEqual(expected, TitleMenu.MoveIndex(index, step, count));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ChoiceLine_MarksTheChoiceThatEnterRuns(int selected)
        {
            string line = TitleMenu.ChoiceLine(selected, ContinueLabel, NewGameLabel);

            bool continues = TitleMenu.ChoiceAt(true, selected) == TitleChoice.Continue;
            string runs = continues ? ContinueLabel : NewGameLabel;
            string other = continues ? NewGameLabel : ContinueLabel;

            StringAssert.Contains("▶ " + runs, line, "Enter で実行する方に印が無い");
            StringAssert.DoesNotContain("▶ " + other, line, "Enter で実行しない方に印が付いている");
            StringAssert.Contains(other, line, "もう一方の選択肢が見えない");
        }

        [Test]
        public void ChoiceLine_ListsTheChoicesInTheOrderTheArrowsMove()
        {
            // → で 0 から 1 へ動く。左に「つづきから」、右に「はじめから」。
            string line = TitleMenu.ChoiceLine(0, ContinueLabel, NewGameLabel);

            Assert.Less(line.IndexOf(ContinueLabel, System.StringComparison.Ordinal),
                line.IndexOf(NewGameLabel, System.StringComparison.Ordinal));
        }

        [TestCase("Data/Localization/ja.json")]
        [TestCase("Data/Localization/en.json")]
        [TestCase("Resources/KCD/Localization/ja.json")]
        [TestCase("Resources/KCD/Localization/en.json")]
        public void Localization_HasTheWordsTheTitleShows(string relativePath)
        {
            Dictionary<string, object> strings = ReadStrings(relativePath);

            foreach (string key in new[] { "ui.title.continue", "ui.title.new_game", "ui.title.press_start" })
            {
                Assert.IsTrue(strings.ContainsKey(key), relativePath + " に " + key + " が無い");
                Assert.IsNotEmpty(strings[key] as string, relativePath + " の " + key + " が空");
            }
        }

        [TestCase("Data/Localization/ja.json")]
        [TestCase("Data/Localization/en.json")]
        [TestCase("Resources/KCD/Localization/ja.json")]
        [TestCase("Resources/KCD/Localization/en.json")]
        public void Localization_ContinueHintNamesTheKeysTheTitleReads(string relativePath)
        {
            // 選択肢の下の案内。選ぶ（←→）・決める（Enter）・F9 の 3 つを、TitleMenu.Update が読む順に言う。
            string hint = ReadStrings(relativePath)["ui.title.continue_hint"] as string;

            Assert.IsNotNull(hint, relativePath + " に ui.title.continue_hint が無い");
            StringAssert.Contains("←", hint);
            StringAssert.Contains("→", hint);
            StringAssert.Contains("Enter", hint);
            StringAssert.Contains("F9", hint);
        }

        private static Dictionary<string, object> ReadStrings(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), path + " が無い");

            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " をオブジェクトとして読めない");

            var strings = root["strings"] as Dictionary<string, object>;
            Assert.IsNotNull(strings, path + " に strings が無い");
            return strings;
        }
    }
}
