using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// PlayerPrefs に書くテストが、開発機に無かったキーを作って残さないこと (#101)。
    ///
    /// Editor の PlayerPrefs はプロジェクトごとに 1 つの置き場で、開発機のゲーム設定そのもの。
    /// 言語（<c>L.SetLocale</c>）とキャラ（<c>GameManager.SelectedCharacterId</c> の setter）は書くたびに
    /// SetString + Save するので、それを通すテストは「入る前にキーが無かったら消す」まで戻さないと、
    /// 言語もキャラも選んだことのない機械（新しい checkout・CI）にキーを作って残す。
    /// ここではキーを消した状態から各テストの手順を通し、終わったあともキーが無いままかを見る。
    /// </summary>
    public sealed class PlayerPrefsHygieneTests
    {
        /// <summary>このテストが消したり書いたりするキー。</summary>
        private static readonly string[] TouchedKeys = { L.PrefKey, GameManager.CharacterPrefKey };

        /// <summary>
        /// 開発機の本物の設定。ここは手で預かって手で戻す。預かる補助そのものが壊れていても、
        /// このテストがキーを消したまま開発機の設定を失くさないようにするため。
        /// </summary>
        private (string Key, bool Had, string Value)[] _saved;

        /// <summary>入る前のメモリ上の言語。</summary>
        private string _locale;

        [SetUp]
        public void SetUp()
        {
            _locale = L.Locale;
            _saved = TouchedKeys
                .Select(key => PlayerPrefs.HasKey(key)
                    ? (key, true, PlayerPrefs.GetString(key))
                    : (key, false, (string)null))
                .ToArray();
        }

        [TearDown]
        public void TearDown()
        {
            L.SetLocale(_locale);
            foreach ((string key, bool had, string value) in _saved)
            {
                if (had)
                {
                    PlayerPrefs.SetString(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            PlayerPrefs.Save();
            DayStats.Reset();
        }

        /// <summary>いまメモリにある言語とは別の言語。切り替えれば必ず KCD.Locale が書かれる。</summary>
        private static string OtherLocale()
        {
            return L.Locale == "en" ? "ja" : "en";
        }

        /// <summary>act の途中で言語が切り替わり、その時点で KCD.Locale が書かれていたか。</summary>
        private static bool LocaleKeyWrittenDuring(Action act)
        {
            bool written = false;
            void OnChanged() => written |= PlayerPrefs.HasKey(L.PrefKey);

            L.LocaleChanged += OnChanged;
            try
            {
                act();
            }
            finally
            {
                L.LocaleChanged -= OnChanged;
            }

            return written;
        }

        // ---- キャラ（KCD.SelectedCharacter）----

        [Test]
        public void ChosenCharacterTest_LeavesNoCharacterKeyWhereThereWasNone()
        {
            // キャラを一度も選んでいない機械。
            PlayerPrefs.DeleteKey(GameManager.CharacterPrefKey);

            var fixture = new NewGameTests();
            try
            {
                fixture.BeginNewGame_LeavesTheChosenCharacterAlone();
            }
            finally
            {
                fixture.TearDown();
            }

            // テストの中で setter が "madonna" を書く。finally が既定値 "mirai" を無条件に SetString して
            // いたころは、ここで KCD.SelectedCharacter = "mirai" が残っていた。
            Assert.IsFalse(PlayerPrefs.HasKey(GameManager.CharacterPrefKey),
                "キャラを選んだことのない機械に KCD.SelectedCharacter を作って残した");
        }

        // ---- 言語（KCD.Locale）----

        [Test]
        public void CreditsTextTest_LeavesNoLocaleKeyWhereThereWasNone()
        {
            PlayerPrefs.DeleteKey(L.PrefKey);
            string other = OtherLocale();

            bool written = LocaleKeyWrittenDuring(() => CreditsTextTests.TextIn(other));

            Assert.IsTrue(written, "前提: " + other + " への切り替えで KCD.Locale が書かれていない（確かめたことにならない）");
            Assert.IsFalse(PlayerPrefs.HasKey(L.PrefKey), "言語を選んだことのない機械に KCD.Locale を作って残した");
        }

        [Test]
        public void MapAttributionTest_LeavesNoLocaleKeyWhereThereWasNone()
        {
            PlayerPrefs.DeleteKey(L.PrefKey);
            string other = OtherLocale();

            bool written = LocaleKeyWrittenDuring(() => MapAttributionTests.In(other, MapAttribution.Line));

            Assert.IsTrue(written, "前提: " + other + " への切り替えで KCD.Locale が書かれていない（確かめたことにならない）");
            Assert.IsFalse(PlayerPrefs.HasKey(L.PrefKey), "言語を選んだことのない機械に KCD.Locale を作って残した");
        }
    }
}
