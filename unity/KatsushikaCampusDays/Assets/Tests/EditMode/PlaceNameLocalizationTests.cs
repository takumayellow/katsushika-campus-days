using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 場所に入ったときのトースト（VisitZone.PlaceLabel）の地名を、いまの言語で出す (#98)。
    /// CampusProps.PlaceZones がシーンに焼くのは日本語の displayName だけなので、表示は辞書の ui.place.&lt;placeId&gt; から引く。
    /// 引くのは入った瞬間なので、言語を切り替えたあとに入った場所は英語で出る。
    /// </summary>
    public sealed class PlaceNameLocalizationTests
    {
        /// <summary>辞書の 2 つの写し。Data が元で、DataBundler が Resources へ写す。</summary>
        private static readonly string[] DictionaryFolders = { "Data/Localization", "Resources/KCD/Localization" };

        /// <summary>ひらがな・カタカナ・漢字・全角記号。英語の地名に混じっていたら訳し忘れ。</summary>
        private static readonly Regex Japanese = new Regex(@"[\u3000-\u30FF\u3400-\u9FFF\uFF00-\uFFEF]");

        /// <summary>CampusProps.PlaceZones が指定している placeId → displayName（日本語）。</summary>
        private static Dictionary<string, string> DeclaredZoneNames()
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "Editor", "CampusProps.cs");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            string source = File.ReadAllText(path);

            var declared = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(source, @"Zone\(parent,\s*""(?<id>[a-z0-9_]+)"",\s*""(?<name>[^""]*)"""))
            {
                declared[m.Groups["id"].Value] = m.Groups["name"].Value;
            }

            Assert.GreaterOrEqual(declared.Count, 4, "CampusProps.PlaceZones を読めていない");
            return declared;
        }

        /// <summary>辞書ファイルの strings を読む。</summary>
        private static Dictionary<string, object> Strings(string folder, string locale)
        {
            string path = Path.Combine(Application.dataPath, folder, locale + ".json");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " を読めない");
            Dictionary<string, object> strings = MiniJson.GetObject(root, "strings");
            Assert.IsNotNull(strings, path + " に strings が無い");
            return strings;
        }

        /// <summary>言語設定（L.Locale と PlayerPrefs）を覆さないよう、終わったら元に戻して body を呼ぶ。</summary>
        private static void KeepingTheLocale(Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                body();
            }
            finally
            {
                L.SetLocale(before);
                if (hadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, savedPref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }

        private static void Switch(string locale)
        {
            L.SetLocale(locale);
            Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
        }

        [TestCase("Data/Localization")]
        [TestCase("Resources/KCD/Localization")]
        public void EveryCampusZone_HasItsNameInBothLanguages(string folder)
        {
            Dictionary<string, object> ja = Strings(folder, "ja");
            Dictionary<string, object> en = Strings(folder, "en");

            foreach (KeyValuePair<string, string> zone in DeclaredZoneNames())
            {
                string key = "ui.place." + zone.Key;

                // 日本語は CampusProps の displayName と同じ。日本語に戻したときの見た目を変えない。
                Assert.AreEqual(zone.Value, MiniJson.GetString(ja, key), folder + "/ja.json の " + key + " が CampusProps の名前と違う");

                string english = MiniJson.GetString(en, key);
                Assert.IsFalse(string.IsNullOrEmpty(english), folder + "/en.json に " + key + " が無い");
                Assert.IsFalse(Japanese.IsMatch(english), folder + "/en.json の " + key + " に日本語が残っている: " + english);
            }
        }

        [Test]
        public void ResourcesCopy_HasTheSamePlaceNamesAsData()
        {
            foreach (string locale in new[] { "ja", "en" })
            {
                Dictionary<string, object> data = Strings(DictionaryFolders[0], locale);
                Dictionary<string, object> resources = Strings(DictionaryFolders[1], locale);
                foreach (string id in DeclaredZoneNames().Keys)
                {
                    string key = "ui.place." + id;
                    Assert.AreEqual(MiniJson.GetString(data, key), MiniJson.GetString(resources, key),
                        locale + ".json の " + key + " が Data と Resources で違う（DataBundler で写し直す）");
                }
            }
        }

        [Test]
        public void AfterSwitchingToEnglish_TheNextArrivalToastNamesThePlaceInEnglish()
        {
            KeepingTheLocale(() =>
            {
                Switch("ja");
                Assert.AreEqual("理科大通り 正門", VisitZone.PlaceLabel("gate_main", "理科大通り 正門"));

                Switch("en");
                Assert.AreEqual("Main Gate, Rikadai Street", VisitZone.PlaceLabel("gate_main", "理科大通り 正門"),
                    "英語に切り替えたあとも正門のトーストが日本語のまま");
                Assert.AreEqual("Library Reflecting Pool", VisitZone.PlaceLabel("library_pond", "図書館の水盤"));

                Switch("ja");
                Assert.AreEqual("理科大通り 正門", VisitZone.PlaceLabel("gate_main", "理科大通り 正門"),
                    "日本語に戻したあとも英語のまま");
            });
        }

        [Test]
        public void InEnglish_EveryCampusZoneToastIsTheEnglishName()
        {
            Dictionary<string, object> en = Strings(DictionaryFolders[1], "en");
            KeepingTheLocale(() =>
            {
                Switch("en");
                foreach (KeyValuePair<string, string> zone in DeclaredZoneNames())
                {
                    Assert.AreEqual(MiniJson.GetString(en, "ui.place." + zone.Key), VisitZone.PlaceLabel(zone.Key, zone.Value),
                        zone.Key + " のトーストが英語の地名になっていない");
                }
            });
        }
    }
}
