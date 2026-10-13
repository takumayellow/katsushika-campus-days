using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 収集物・写真スポット・称号の文言が collectibles.json と ja / en の辞書で食い違わないこと (#65)。
    /// 表示は辞書（item.* / photo.* / ach.*）を先に引くので、片方だけ直すと画面に古い文が出る。
    /// Resources の写しが Data と同じことは ResultTotalsTests.ResourcesCopies_MatchTheData が見る。
    /// </summary>
    public sealed class CollectibleLocalizationTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static readonly string[] Locales = { "ja", "en" };

        private static CollectibleCatalog RealCatalog()
        {
            return CollectibleCatalog.Parse(File.ReadAllText(Path.Combine(DataRoot, "Collectibles", "collectibles.json")));
        }

        private static Dictionary<string, object> Strings(string locale)
        {
            string path = Path.Combine(DataRoot, "Localization", locale + ".json");
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            Dictionary<string, object> strings = MiniJson.GetObject(node, "strings");
            Assert.IsNotNull(strings, path + " に strings が無い");
            return strings;
        }

        /// <summary>辞書のキーと、collectibles.json 側の ja / en の値の組。</summary>
        private static List<(string Key, string Ja, string En)> CatalogTexts(CollectibleCatalog catalog)
        {
            var texts = new List<(string, string, string)>();
            foreach (CatalogItem item in catalog.Items)
            {
                texts.Add(("item." + item.Id + ".name", item.NameJa, item.NameEn));
                texts.Add(("item." + item.Id + ".hint", item.HintJa, item.HintEn));
            }

            foreach (CatalogPhotoSpot spot in catalog.PhotoSpots)
            {
                texts.Add(("photo." + spot.Id + ".name", spot.NameJa, spot.NameEn));
                texts.Add(("photo." + spot.Id + ".caption", spot.CaptionJa, spot.CaptionEn));
            }

            foreach (CatalogAchievement achievement in catalog.Achievements)
            {
                texts.Add(("ach." + achievement.Id + ".name", achievement.NameJa, achievement.NameEn));
                texts.Add(("ach." + achievement.Id + ".desc", achievement.DescJa, achievement.DescEn));
            }

            return texts;
        }

        [Test]
        public void CatalogTexts_AreTheSameInTheJsonAndInBothDictionaries()
        {
            CollectibleCatalog catalog = RealCatalog();
            List<(string Key, string Ja, string En)> texts = CatalogTexts(catalog);
            Assert.AreEqual((catalog.Items.Count + catalog.PhotoSpots.Count + catalog.Achievements.Count) * 2, texts.Count);

            var wrong = new List<string>();
            foreach (string locale in Locales)
            {
                Dictionary<string, object> strings = Strings(locale);
                foreach ((string key, string ja, string en) in texts)
                {
                    string want = locale == "ja" ? ja : en;
                    if (string.IsNullOrEmpty(want))
                    {
                        wrong.Add(locale + ": collectibles.json の " + key + " が空");
                        continue;
                    }

                    if (!strings.ContainsKey(key))
                    {
                        wrong.Add(locale + ".json に " + key + " が無い");
                        continue;
                    }

                    string got = MiniJson.GetString(strings, key);
                    if (got != want)
                    {
                        wrong.Add(locale + ".json の " + key + " が「" + got + "」、collectibles.json は「" + want + "」");
                    }
                }
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        [Test]
        public void HudTexts_ForPickupsAndPhotos_TakeTheNameInBothLanguages()
        {
            string[] formats = { "ui.hud.photo_spot", "ui.hud.collectible_found", "ui.hud.photo_taken", "ui.hud.picked_up" };
            foreach (string locale in Locales)
            {
                Dictionary<string, object> strings = Strings(locale);
                foreach (string key in formats)
                {
                    Assert.IsTrue(strings.ContainsKey(key), locale + ".json に " + key + " が無い");
                    StringAssert.Contains("{0}", MiniJson.GetString(strings, key), locale + ".json の " + key + " に名前の差し込み口 {0} が無い");
                }

                // 三脚に近づいたときの「E: 撮影する」。
                Assert.IsTrue(strings.ContainsKey("ui.interact.photo"), locale + ".json に ui.interact.photo が無い");
                Assert.IsNotEmpty(MiniJson.GetString(strings, "ui.interact.photo"), locale + ".json の ui.interact.photo が空");
            }
        }

        [Test]
        public void BothDictionaries_HaveTheSameCatalogAndHudKeys()
        {
            // 片方の言語だけにキーを足すと、もう片方では id や既定の文がそのまま出る。
            Dictionary<string, object> ja = Strings("ja");
            Dictionary<string, object> en = Strings("en");
            var onlyOne = new List<string>();
            foreach (string key in ja.Keys)
            {
                if (IsCollectibleKey(key) && !en.ContainsKey(key))
                {
                    onlyOne.Add("en.json に無い: " + key);
                }
            }

            foreach (string key in en.Keys)
            {
                if (IsCollectibleKey(key) && !ja.ContainsKey(key))
                {
                    onlyOne.Add("ja.json に無い: " + key);
                }
            }

            Assert.IsEmpty(onlyOne, string.Join("\n", onlyOne));
        }

        private static bool IsCollectibleKey(string key)
        {
            foreach (string prefix in new[] { "item.", "photo.", "ach.", "ui.hud." })
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
