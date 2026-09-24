using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 地図データ（OpenStreetMap, ODbL）の帰属表示を守る (#20)。
    ///
    /// キャンパスの建物・通路・緑地・ミニマップは OSM のデータから作っているので、ODbL の条件として
    /// 「© OpenStreetMap contributors」を見える場所に出す。以前はクレジット画面を開かないと出てこなかった。
    /// タイトル画面のラベルとクレジット画面の 1 行目は辞書の同じキー（MapAttribution.Key）から文面を取り、
    /// ここでは両者が同じ文面になっていること、辞書と docs/CREDITS.md に帰属が書かれていることを見る。
    /// </summary>
    public sealed class MapAttributionTests
    {
        private const string TitleScene = "Assets/Scenes/Title.unity";

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>言語を切り替えて read を呼び、PlayerPrefs の言語設定を元に戻す。</summary>
        private static T In<T>(string locale, Func<T> read)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
                Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
                return read();
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

        [TestCase("ja")]
        [TestCase("en")]
        public void 帰属表示に著作権表示とライセンス名が入る(string locale)
        {
            string line = In(locale, MapAttribution.Line);

            StringAssert.Contains(MapAttribution.Notice, line, locale + ": 帰属表示に著作権表示が無い");
            StringAssert.Contains(MapAttribution.License, line, locale + ": 帰属表示にライセンス名が無い");
        }

        [Test]
        public void 英語では英語の文面になる()
        {
            string ja = In("ja", MapAttribution.Line);
            string en = In("en", MapAttribution.Line);

            StringAssert.StartsWith("地図データ", ja);
            StringAssert.StartsWith("Map data", en);
        }

        [TestCase("ja")]
        [TestCase("en")]
        public void クレジット画面の先頭はタイトルと同じ帰属表示(string locale)
        {
            string line = In(locale, MapAttribution.Line);
            string credits = In(locale, CreditsView.BuildText);

            StringAssert.StartsWith(line, credits, locale + ": クレジット画面の 1 行目がタイトル画面の帰属表示と違う");
            StringAssert.Contains(MapAttribution.CopyrightUrl, credits, locale + ": クレジット画面に OSM の著作権ページの URL が無い");
        }

        [Test]
        public void 帰属が欠けた文面は既定文に戻す()
        {
            Assert.AreEqual(MapAttribution.DefaultJa, MapAttribution.OrFallback(null, MapAttribution.DefaultJa));
            Assert.AreEqual(MapAttribution.DefaultJa, MapAttribution.OrFallback(string.Empty, MapAttribution.DefaultJa));
            Assert.AreEqual(MapAttribution.DefaultJa, MapAttribution.OrFallback("地図データ", MapAttribution.DefaultJa));
            Assert.AreEqual(MapAttribution.DefaultJa,
                MapAttribution.OrFallback("© OpenStreetMap contributors", MapAttribution.DefaultJa), "ライセンス名が無い");
            Assert.AreEqual(MapAttribution.DefaultEn, MapAttribution.OrFallback(MapAttribution.DefaultEn, MapAttribution.DefaultJa));
            Assert.IsTrue(MapAttribution.IsComplete(MapAttribution.DefaultJa));
            Assert.IsTrue(MapAttribution.IsComplete(MapAttribution.DefaultEn));
        }

        [TestCase("Data/Localization")]
        [TestCase("Resources/KCD/Localization")]
        public void 辞書の帰属表示が両言語で欠けていない(string folder)
        {
            foreach (string locale in new[] { "ja", "en" })
            {
                string path = Path.Combine(Application.dataPath, folder, locale + ".json");
                Assert.IsTrue(File.Exists(path), path + " が無い");

                var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                Dictionary<string, object> strings = root != null ? MiniJson.GetObject(root, "strings") : null;
                Assert.IsNotNull(strings, path + " に strings が無い");
                Assert.IsTrue(strings.TryGetValue(MapAttribution.Key, out object value), path + " に " + MapAttribution.Key + " が無い");
                Assert.IsTrue(MapAttribution.IsComplete(value as string),
                    path + " の " + MapAttribution.Key + " に「" + MapAttribution.Notice + "」と「" + MapAttribution.License + "」が揃っていない");
            }
        }

        [Test]
        public void CREDITS_md_に同じ帰属が書かれている()
        {
            string path = Path.Combine(RepoRoot, "docs", "CREDITS.md");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            string doc = File.ReadAllText(path);

            StringAssert.Contains(MapAttribution.Notice, doc, "docs/CREDITS.md に OSM の著作権表示が無い");
            StringAssert.Contains(MapAttribution.License, doc, "docs/CREDITS.md に ODbL が無い");
            StringAssert.Contains(MapAttribution.CopyrightUrl, doc, "docs/CREDITS.md に OSM の著作権ページの URL が無い");
        }

        [Test]
        public void タイトル画面の右下に帰属表示がある()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScene, OpenSceneMode.Additive);
            try
            {
                var labels = new List<MapAttributionLabel>();
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    labels.AddRange(go.GetComponentsInChildren<MapAttributionLabel>(true));
                }

                Assert.AreEqual(1, labels.Count,
                    TitleScene + " に MapAttributionLabel が 1 つだけあるはず（無ければ SceneBuilder.BuildAll でシーンを組み直す）");
                MapAttributionLabel label = labels[0];

                Assert.IsTrue(label.gameObject.activeInHierarchy, "帰属表示が非表示になっている");
                Assert.IsTrue(MapAttribution.IsComplete(label.Text), "タイトル画面の帰属表示の文面が足りない: " + label.Text);

                for (Transform t = label.transform.parent; t != null; t = t.parent)
                {
                    Assert.AreNotEqual("TitleRoot", t.name, "TitleRoot の下にあると主人公選びの画面で消える");
                    Assert.AreNotEqual("SelectRoot", t.name, "SelectRoot の下にあるとタイトルの画面で見えない");
                }

                Assert.IsNotNull(label.GetComponentInParent<Canvas>(), "帰属表示が Canvas の下に無い");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
