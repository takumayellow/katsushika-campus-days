using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 表示文字列の辞書（L）。キーが無ければ既定文、それも無ければキーを返して画面を空にしないこと、
    /// 書式が壊れていても落とさないこと、辞書の無い言語へは切り替えず設定も書かないこと、
    /// 同じ言語への切り替えでは描き直しを起こさないこと。各テストの後で言語と PlayerPrefs を元に戻す。
    /// </summary>
    public sealed class LocalizationTests
    {
        private const string MissingKey = "test.no_such_key";

        private bool _hadPref;
        private string _savedPref;
        private string _savedLocale;

        [SetUp]
        public void SetUp()
        {
            _hadPref = PlayerPrefs.HasKey(L.PrefKey);
            _savedPref = _hadPref ? PlayerPrefs.GetString(L.PrefKey) : null;
            _savedLocale = L.Locale;
            L.SetLocale("ja");
            Assert.AreEqual("ja", L.Locale, "Resources/KCD/Localization/ja.json を読めていない");
        }

        [TearDown]
        public void TearDown()
        {
            L.SetLocale(_savedLocale);
            if (_hadPref)
            {
                PlayerPrefs.SetString(L.PrefKey, _savedPref);
            }
            else
            {
                PlayerPrefs.DeleteKey(L.PrefKey);
            }

            PlayerPrefs.Save();
        }

        // ---- 引き当て ----

        [Test]
        public void Get_KnownKey_ReadsTheTableOfTheCurrentLanguage()
        {
            // 文言そのものは辞書の担当が変えるので、表から引けていることと言語で変わることだけを見る。
            string ja = L.Get("ui.hud.time_morning", "fallback");
            Assert.AreNotEqual("fallback", ja, "日本語の表から引けていない");

            L.SetLocale("en");
            Assert.AreEqual("en", L.Locale, "Resources/KCD/Localization/en.json を読めていない");
            string en = L.Get("ui.hud.time_morning", "fallback");
            Assert.AreNotEqual("fallback", en, "英語の表から引けていない");
            Assert.AreNotEqual(ja, en, "言語を切り替えても同じ表を引いている");
        }

        [Test]
        public void Get_MissingKey_FallsBackThenShowsTheKey()
        {
            Assert.AreEqual("既定の文", L.Get(MissingKey, "既定の文"));
            Assert.AreEqual(MissingKey, L.Get(MissingKey), "既定文も無いときはキーを見せる（空の帯にしない）");
        }

        [Test]
        public void Get_NullKey_DoesNotThrow()
        {
            Assert.AreEqual(string.Empty, L.Get(null));
            Assert.AreEqual("既定の文", L.Get(null, "既定の文"));
        }

        // ---- 書式 ----

        [Test]
        public void Format_FillsThePlaceholders()
        {
            string ja = L.Format("ui.hud.picked_up", "葉");
            StringAssert.Contains("葉", ja);
            StringAssert.DoesNotContain("{0}", ja, "差し込み口が残っている");

            L.SetLocale("en");
            string en = L.Format("ui.hud.picked_up", "leaf");
            StringAssert.Contains("leaf", en);
            StringAssert.DoesNotContain("{0}", en, "差し込み口が残っている");
        }

        [TestCase("test.{broken")]
        [TestCase("test.{0}.{1}")]
        public void Format_BrokenTemplate_ReturnsTheTemplateInsteadOfThrowing(string key)
        {
            // 辞書に無いキーはキーそのものが書式になる。壊れた書式でも例外で会話や HUD を止めない。
            string result = null;
            Assert.DoesNotThrow(() => result = L.Format(key, "x"));
            Assert.AreEqual(key, result);
        }

        // ---- データ側の日英 ----

        [Test]
        public void Pick_UsesEnglishOnlyWhenThereIsSome()
        {
            Assert.AreEqual("図書館", L.Pick("図書館", "Library"), "日本語表示で英語を選んでいる");

            L.SetLocale("en");
            Assert.AreEqual("Library", L.Pick("図書館", "Library"));
            Assert.AreEqual("図書館", L.Pick("図書館", string.Empty), "英語が空のとき空文字を出している");
            Assert.AreEqual("図書館", L.Pick("図書館", null));
        }

        // ---- 言語の切り替え ----

        [TestCase("xx")]
        [TestCase("")]
        [TestCase(null)]
        public void SetLocale_WithoutADictionary_IsIgnored(string locale)
        {
            PlayerPrefs.SetString(L.PrefKey, "ja");
            int raised = 0;
            void OnChanged() => raised++;
            L.LocaleChanged += OnChanged;
            try
            {
                L.SetLocale(locale);
            }
            finally
            {
                L.LocaleChanged -= OnChanged;
            }

            Assert.AreEqual("ja", L.Locale, "辞書の無い言語に切り替わった");
            Assert.AreNotEqual("ui.hud.time_morning", L.Get("ui.hud.time_morning"), "辞書の無い言語で表が空になった");
            Assert.AreEqual("ja", PlayerPrefs.GetString(L.PrefKey), "辞書の無い言語を設定に書いた（次の起動で読めない）");
            Assert.AreEqual(0, raised, "切り替わっていないのに描き直しを起こした");
        }

        [Test]
        public void SetLocale_SameLanguage_DoesNotRaiseTheEvent()
        {
            int raised = 0;
            void OnChanged() => raised++;
            L.LocaleChanged += OnChanged;
            try
            {
                L.SetLocale("ja");
            }
            finally
            {
                L.LocaleChanged -= OnChanged;
            }

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void SetLocale_NewLanguage_SavesItAndRaisesTheEventOnce()
        {
            int raised = 0;
            void OnChanged() => raised++;
            L.LocaleChanged += OnChanged;
            try
            {
                L.SetLocale("en");
            }
            finally
            {
                L.LocaleChanged -= OnChanged;
            }

            Assert.AreEqual("en", L.Locale);
            Assert.IsTrue(L.IsEnglish);
            Assert.AreEqual("en", PlayerPrefs.GetString(L.PrefKey), "選んだ言語が次の起動に残らない");
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void Toggle_SwitchesBetweenJapaneseAndEnglish()
        {
            L.Toggle();
            Assert.AreEqual("en", L.Locale);

            L.Toggle();
            Assert.AreEqual("ja", L.Locale);
        }
    }
}
