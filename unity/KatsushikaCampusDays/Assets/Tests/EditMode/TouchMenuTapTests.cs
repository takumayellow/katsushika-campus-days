using System;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// ポーズ・設定・クレジット・クエストログ・エンディング・タイトルを指で操作するときの文面とリンクの読み方 (#187)。
    /// 叩いた場所から link の id を拾うのは <see cref="TmpLinkTap"/> の仕事で、ここではその id をどう読むかと、
    /// 指のときだけ文面に link が入ること（キーで遊ぶ人の文面は変えないこと）を見る。
    /// </summary>
    public sealed class TouchMenuTapTests
    {
        private static int CountLinks(string text) => Regex.Matches(text, "<link=").Count;

        /// <summary>文面を日本語で組んでから元の言語に戻す。</summary>
        private static string InJapanese(Func<string> compose)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    L.SetLocale("ja");
                    return compose();
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        // ---- TmpLinkTap ----

        [TestCase("dec:3", "dec:", true, 3)]
        [TestCase("inc:0", "inc:", true, 0)]
        [TestCase("inc:0", "dec:", false, -1)]
        [TestCase("dec:", "dec:", false, -1)]
        [TestCase("dec:+1", "dec:", false, -1)]
        [TestCase("dec:1", "", false, -1)]
        [TestCase(null, "dec:", false, -1)]
        public void TmpLinkTap_TryParse(string id, string prefix, bool ok, int index)
        {
            Assert.AreEqual(ok, TmpLinkTap.TryParse(id, prefix, out int parsed));
            if (ok)
            {
                Assert.AreEqual(index, parsed);
            }
        }

        [TestCase("choice:0", 2, 0)]
        [TestCase("choice:1", 2, 1)]
        [TestCase("choice:2", 2, -1)]
        [TestCase("inc:0", 2, -1)]
        [TestCase(null, 2, -1)]
        public void TmpLinkTap_ChoiceIn_OnlyInsideTheCount(string id, int count, int expected)
        {
            Assert.AreEqual(expected, TmpLinkTap.ChoiceIn(id, count));
        }

        // ---- ポーズ ----

        [Test]
        public void PauseMenu_TapIndex_ListAndConfirmHaveTheirOwnRange()
        {
            int last = PauseMenu.EntryCount - 1;
            Assert.AreEqual(last, PauseMenu.TapIndex("choice:" + last, false));
            Assert.AreEqual(-1, PauseMenu.TapIndex("choice:" + PauseMenu.EntryCount, false));
            Assert.AreEqual(1, PauseMenu.TapIndex("choice:1", true), "確認の「いいえ」");
            Assert.AreEqual(-1, PauseMenu.TapIndex("choice:2", true), "確認は「はい」「いいえ」の 2 つだけ");
            Assert.AreEqual(-1, PauseMenu.TapIndex(null, false));
        }

        [Test]
        public void PauseMenu_Compose_LinksOnlyForTouch()
        {
            string plain = InJapanese(() => PauseMenu.Compose(false, 2, false));
            string linked = InJapanese(() => PauseMenu.Compose(false, 2, true));

            Assert.AreEqual(0, CountLinks(plain), "キーで遊ぶ人の文面に link が入った");
            Assert.AreEqual(PauseMenu.EntryCount, CountLinks(linked), "行ごとに link が無い");
            StringAssert.Contains("<link=\"choice:2\"><color=#FFD98A>▶ ", linked);
            StringAssert.StartsWith(TmpLinkTap.TapRowLineHeight, linked, "指のときは行の間隔を広げる");
            string portrait = InJapanese(() => PauseMenu.Compose(false, 2, true, true));
            StringAssert.StartsWith(TmpLinkTap.RowHeight(PauseMenu.PortraitRowEm), portrait, "縦の指は枠が縮むぶん広げる");
            Assert.AreEqual(3.5f, PauseMenu.PortraitRowEm);
            StringAssert.DoesNotContain("line-height", plain, "キーで遊ぶ人の行の間隔が変わった");
            Assert.AreEqual(plain, Regex.Replace(linked, "</?link[^>]*>|<line-height=[^>]*>", string.Empty),
                "link と行の高さを外すとキーのときの文面と同じ");
        }

        [Test]
        public void PauseMenu_Compose_ConfirmHasYesAndNoLinks()
        {
            string linked = InJapanese(() => PauseMenu.Compose(true, 1, true));

            Assert.AreEqual(2, CountLinks(linked));
            StringAssert.Contains("\n\n" + TmpLinkTap.TapRowLineHeight + "<link=\"choice:0\">   はい</link>", linked);
            string portrait = InJapanese(() => PauseMenu.Compose(true, 1, true, true));
            StringAssert.Contains("\n\n" + TmpLinkTap.RowHeight(PauseMenu.PortraitRowEm) + "<link=\"choice:0\">   はい</link>",
                portrait);
            StringAssert.Contains("<link=\"choice:1\"><color=#FFD98A>▶ いいえ</color></link>", linked);
        }

        // ---- 設定 ----

        [TestCase("dec:0", 0, -1)]
        [TestCase("inc:4", 4, 1)]
        [TestCase("choice:8", 8, 0)]
        public void SettingsView_TryParseTap_ReadsRowAndDirection(string id, int row, int direction)
        {
            Assert.IsTrue(SettingsView.TryParseTap(id, out int parsedRow, out int parsedDirection));
            Assert.AreEqual(row, parsedRow);
            Assert.AreEqual(direction, parsedDirection);
        }

        [TestCase("inc:9")]
        [TestCase("choice:12")]
        [TestCase("up:1")]
        [TestCase(null)]
        public void SettingsView_TryParseTap_RejectsOutsideTheRows(string id)
        {
            Assert.IsFalse(SettingsView.TryParseTap(id, out _, out _));
        }

        [Test]
        public void SettingsView_Step_MovesOneShownLevelFromTheDefaults()
        {
            foreach (float value in new[] { AudioManager.DefaultBgmVolume, AudioManager.DefaultSeVolume,
                         AudioManager.DefaultAmbientVolume })
            {
                int shown = SettingsView.Level(value);
                Assert.AreEqual(shown + 1, SettingsView.Level(SettingsView.Step(value, 1)), "▶ で 1 段上がらない: " + value);
                Assert.AreEqual(shown - 1, SettingsView.Level(SettingsView.Step(value, -1)), "◀ で 1 段下がらない: " + value);
            }
        }

        [TestCase(1f, 1, 1f)]
        [TestCase(0f, -1, 0f)]
        [TestCase(0.3f, 1, 0.4f)]
        public void SettingsView_Step_StaysOnTheGridAndInRange(float value, int direction, float expected)
        {
            Assert.AreEqual(expected, SettingsView.Step(value, direction), 1e-6f);
        }

        [Test]
        public void SettingsView_AppendRow_KeysKeepTheOldText()
        {
            var builder = new StringBuilder();
            SettingsView.AppendRow(builder, 3, true, "言語", "日本語", true, false);
            SettingsView.AppendRow(builder, 0, false, "音楽の音量", "■■□", false, false);

            Assert.AreEqual("<color=#FFD98A>▶ 言語　　◀ 日本語 ▶</color>\n   音楽の音量　　■■□\n", builder.ToString());
        }

        [Test]
        public void SettingsView_AppendRow_TouchLinksTheNameAndBothArrows()
        {
            var builder = new StringBuilder();
            SettingsView.AppendRow(builder, 1, false, "効果音の音量", "■■□", true, true);

            Assert.AreEqual("<link=\"choice:1\">   効果音の音量</link>　　<link=\"dec:1\">◀</link> ■■□ <link=\"inc:1\">▶</link>\n",
                builder.ToString());
        }

        [Test]
        public void SettingsView_AppendRow_CloseRowHasNoArrows()
        {
            var builder = new StringBuilder();
            SettingsView.AppendRow(builder, 8, true, "閉じる", string.Empty, false, true);

            Assert.AreEqual("<color=#FFD98A><link=\"choice:8\">▶ 閉じる</link></color>\n", builder.ToString());
        }

        // ---- クレジット・エンディング ----

        [Test]
        public void CreditsView_BuildText_TouchSaysTap()
        {
            string keys = InJapanese(() => CreditsView.BuildText());
            string touch = InJapanese(() => CreditsView.BuildText(true));

            StringAssert.Contains("Esc / Enter で戻る", keys);
            StringAssert.Contains("タップで戻る", touch);
            StringAssert.DoesNotContain("Esc", touch);
        }

        [Test]
        public void DormEnding_ChoiceText_TouchSaysTap()
        {
            Assert.AreEqual("タップでタイトルへ", DormEnding.ChoiceText(false, true));
            Assert.AreEqual("Tap — back to the title", DormEnding.ChoiceText(true, true));
            Assert.AreEqual("Enter でタイトルへ", DormEnding.ChoiceText(false));
        }

        [Test]
        public void FormalEnding_ChoiceText_TouchSaysTap()
        {
            int last = FormalEnding.PageCount - 1;
            string counter = "  (1/" + FormalEnding.PageCount + ")";
            Assert.AreEqual("タップで次へ" + counter, FormalEnding.ChoiceText(0, false, true));
            Assert.AreEqual("Tap — next" + counter, FormalEnding.ChoiceText(0, true, true));
            StringAssert.StartsWith("タップでキャンパスに戻る", FormalEnding.ChoiceText(last, false, true));
            StringAssert.StartsWith("Tap — back to campus", FormalEnding.ChoiceText(last, true, true));
            Assert.AreEqual("Enter で次へ" + counter, FormalEnding.ChoiceText(0, false));
            Assert.AreEqual("Enter — next" + counter, FormalEnding.ChoiceText(0, true));
        }

        // ---- タイトル ----

        [Test]
        public void TitleMenu_TouchExtras_LinksCreditsAndSettings()
        {
            string extras = TitleMenu.TouchExtras("クレジット", "設定");

            StringAssert.Contains("<link=\"" + TitleMenu.CreditsLink + "\">クレジット</link>", extras);
            StringAssert.Contains("<link=\"" + TitleMenu.SettingsLink + "\">設定</link>", extras);
        }

        [Test]
        public void TitleMenu_TouchStartLine_IsALinkThatStarts()
        {
            string line = TitleMenu.TouchStartLine("タップではじめる");

            Assert.AreEqual("<link=\"choice:0\">タップではじめる</link>", line);
            Assert.AreEqual(TitleChoice.NewGame, TitleMenu.TapChoice(false, "choice:0"));
        }

        [Test]
        public void TitleMenu_ExtrasLinks_AreNotChoices()
        {
            // セーブがあるとき、クレジットと設定を叩いても「つづきから」「はじめから」は選ばれない。
            Assert.IsNull(TitleMenu.TapChoice(true, TitleMenu.CreditsLink));
            Assert.IsNull(TitleMenu.TapChoice(true, TitleMenu.SettingsLink));
        }
    }
}
