using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// フォトモードの操作案内 (#12)。日本語と英語、キーボードとゲームパッドで 4 通りあり、
    /// 書いてあるキーが KCDInput の Photo* の割り当てと合っていること。
    /// </summary>
    public sealed class PhotoModeHintsTests
    {
        [Test]
        public void Text_HasFourDistinctTwoLineVariants()
        {
            var texts = new HashSet<string>
            {
                PhotoModeHints.Text(false, false),
                PhotoModeHints.Text(true, false),
                PhotoModeHints.Text(false, true),
                PhotoModeHints.Text(true, true),
            };

            Assert.AreEqual(4, texts.Count, "言語か入力機器で文が変わらない組がある");
            foreach (string text in texts)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(text));
                Assert.AreEqual(2, text.Split('\n').Length, "案内の枠は 2 行分: " + text);
            }
        }

        [Test]
        public void KeyboardText_ListsTheFreeCameraKeys()
        {
            foreach (bool english in new[] { false, true })
            {
                string text = PhotoModeHints.Text(english, false);
                foreach (string key in new[] { "WASD", "E / Q", "Shift", "Enter / Space", " R", " H", "P / Esc" })
                {
                    StringAssert.Contains(key, text, (english ? "en" : "ja") + " に " + key + " が無い");
                }
            }
        }

        [Test]
        public void KeyboardText_ShootsWithoutE()
        {
            // E は自由カメラの上昇。以前の案内（Enter / E で撮影）のままだと、上がるつもりで撮ってしまう。
            AssertShootKeys(PhotoModeHints.Text(false, false), "撮影 ", "Enter", "Space");
            AssertShootKeys(PhotoModeHints.Text(true, false), "Shoot ", "Enter", "Space");
        }

        [Test]
        public void GamepadText_UsesButtonNamesOnly()
        {
            foreach (bool english in new[] { false, true })
            {
                string text = PhotoModeHints.Text(english, true);
                StringAssert.Contains("Y / B", text, "戻るは入ったのと同じ Y と、B");
                StringAssert.DoesNotContain("WASD", text);
                StringAssert.DoesNotContain("Esc", text);
            }

            AssertShootKeys(PhotoModeHints.Text(false, true), "撮影 ", "A", "X");
            AssertShootKeys(PhotoModeHints.Text(true, true), "Shoot ", "A", "X");
        }

        [Test]
        public void Text_JapaneseAndEnglishUseTheirOwnWords()
        {
            StringAssert.Contains("撮影", PhotoModeHints.Text(false, false));
            StringAssert.Contains("撮影", PhotoModeHints.Text(false, true));
            StringAssert.Contains("Shoot", PhotoModeHints.Text(true, false));
            StringAssert.Contains("Shoot", PhotoModeHints.Text(true, true));
            StringAssert.DoesNotContain("撮影", PhotoModeHints.Text(true, false));
            StringAssert.DoesNotContain("Shoot", PhotoModeHints.Text(false, false));
        }

        /// <summary>label の後ろから次の区切り（全角空白・半角空白 2 つ・改行）までのキーが expected と同じか。</summary>
        private static void AssertShootKeys(string text, string label, params string[] expected)
        {
            int start = text.IndexOf(label, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, label + " が無い: " + text);
            start += label.Length;

            int end = text.Length;
            foreach (string separator in new[] { "　", "  ", "\n" })
            {
                int found = text.IndexOf(separator, start, StringComparison.Ordinal);
                if (found >= 0 && found < end)
                {
                    end = found;
                }
            }

            string[] keys = text.Substring(start, end - start).Split(new[] { " / " }, StringSplitOptions.None);
            CollectionAssert.AreEqual(expected, keys);
        }
    }
}
