using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// 地図データ（OpenStreetMap, ODbL）の帰属表示を守る (#20)。
    ///
    /// キャンパスの建物・通路・緑地・ミニマップは OSM のデータから作っているので、ODbL の条件として
    /// 「© OpenStreetMap contributors」を見える場所に出す。以前はクレジット画面を開かないと出てこなかった。
    /// タイトル画面のラベルとクレジット画面の 1 行目は辞書の同じキー（MapAttribution.Key）から文面を取り、
    /// ここでは両者が同じ文面になっていること、辞書と docs/CREDITS.md に帰属が書かれていることを見る。
    ///
    /// タイトル画面のラベルは、設定とクレジットの枠が開いて文字にかかる間だけ文字を隠す (#187)。横持ちのスマホでは
    /// 枠が Canvas の高さいっぱいに広がり、半透明の枠に透けて本文に重なる。画面の大きさごとに、隠れるか出たままかも見る。
    /// </summary>
    public sealed class MapAttributionTests
    {
        private const string TitleScene = "Assets/Scenes/Title.unity";

        private readonly List<GameObject> _created = new List<GameObject>();

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _created.Clear();
        }

        /// <summary>言語を切り替えて read を呼び、PlayerPrefs の言語設定を元に戻す。</summary>
        internal static T In<T>(string locale, Func<T> read)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
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
                }
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
            string credits = In(locale, () => CreditsView.BuildText());

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
                MapAttributionLabel label = Only<MapAttributionLabel>(scene);

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

        [Test]
        public void Covered_は開いて文字にかかる枠だけを数える()
        {
            RectTransform canvas = MakeCanvas(new Vector2(1000f, 600f));
            RectTransform label = MakeRect(canvas, "Label", new Vector2(1f, 0f), new Vector2(-24f, 12f), new Vector2(400f, 40f));
            RectTransform over = MakeRect(canvas, "Over", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 580f));
            RectTransform clear = MakeRect(canvas, "Clear", new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(960f, 200f));

            Assert.IsTrue(MapAttributionLabel.Covered(label, new[] { over }), "文字にかかる枠を数えない");
            Assert.IsFalse(MapAttributionLabel.Covered(label, new[] { clear }), "文字より上にある枠を数えた");
            Assert.IsTrue(MapAttributionLabel.Covered(label, new[] { clear, over }), "2 つ目の枠を見ていない");

            over.localScale = new Vector3(0.5f, 0.5f, 1f);
            Assert.IsFalse(MapAttributionLabel.Covered(label, new[] { over }), "縮めた後の大きさで比べていない");
            over.localScale = Vector3.one;

            over.gameObject.SetActive(false);
            Assert.IsFalse(MapAttributionLabel.Covered(label, new[] { over }), "閉じた枠を数えた");
            over.gameObject.SetActive(true);

            RectTransform closed = MakeRect(canvas, "Closed", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 600f));
            RectTransform inner = MakeRect(closed, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 580f));
            closed.gameObject.SetActive(false);
            Assert.IsFalse(MapAttributionLabel.Covered(label, new[] { inner }), "親が閉じている枠を数えた");

            Assert.IsFalse(MapAttributionLabel.Covered(label, new RectTransform[] { null }));
            Assert.IsFalse(MapAttributionLabel.Covered(label, new RectTransform[0]));
            Assert.IsFalse(MapAttributionLabel.Covered(label, null));
            Assert.IsFalse(MapAttributionLabel.Covered(null, new[] { over }));
        }

        [Test]
        public void 開いた枠が文字にかかる間だけ文字を隠す()
        {
            RectTransform canvas = MakeCanvas(new Vector2(1000f, 600f));
            RectTransform box = MakeRect(canvas, "MapAttribution", new Vector2(1f, 0f), new Vector2(-24f, 12f),
                new Vector2(400f, 56f));
            RectTransform text = MakeRect(box, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(364f, 32f));
            RectTransform panel = MakeRect(canvas, "Credits", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 580f));
            panel.gameObject.SetActive(false);

            MapAttributionLabel attribution = box.gameObject.AddComponent<MapAttributionLabel>();
            attribution.Bind(text.gameObject.AddComponent<TextMeshProUGUI>());
            attribution.Avoid(panel);

            attribution.Sync();
            Assert.IsTrue(attribution.IsShown, "枠を閉じているのに文字を隠した");

            panel.gameObject.SetActive(true);
            attribution.Sync();
            Assert.IsFalse(attribution.IsShown, "開いた枠が文字にかかるのに文字を出している");

            panel.anchoredPosition = new Vector2(0f, 120f);
            attribution.Sync();
            Assert.IsTrue(attribution.IsShown, "枠が文字から離れたのに文字を戻さない");

            panel.anchoredPosition = Vector2.zero;
            attribution.Sync();
            panel.gameObject.SetActive(false);
            attribution.Sync();
            Assert.IsTrue(attribution.IsShown, "枠を閉じたのに文字を戻さない");
        }

        [Test]
        public void タイトル画面の帰属表示は設定とクレジットの枠を避ける()
        {
            WithTitle(title =>
            {
                CollectionAssert.AreEquivalent(new[] { title.Settings, title.Credits }, title.Avoid,
                    "帰属表示が避ける枠が、設定とクレジットの枠と違う（SceneBuilder.BuildAll でシーンを組み直す）");
                Assert.IsTrue(title.Label.IsShown, "枠を閉じているのに帰属表示の文字が隠れている");
            });
        }

        [TestCase(844f, 390f)]
        [TestCase(667f, 375f)]
        [TestCase(915f, 412f)]
        public void 横持ちの指の画面ではクレジットを開くと帰属表示を隠す(float width, float height)
        {
            WithTitle(title => Assert.IsFalse(ShownWith(title, title.Credits, new Vector2(width, height), true),
                "クレジットの枠に帰属表示が透けて重なる (" + width + "x" + height + ")"));
        }

        [TestCase(1920f, 1080f, false)]
        [TestCase(1366f, 768f, false)]
        [TestCase(1024f, 768f, false)]
        [TestCase(390f, 844f, true)]
        [TestCase(360f, 800f, true)]
        [TestCase(430f, 932f, true)]
        public void PC_と縦持ちの画面では枠を開いても帰属表示を出したまま(float width, float height, bool touch)
        {
            WithTitle(title =>
            {
                foreach (RectTransform panel in new[] { title.Settings, title.Credits })
                {
                    Assert.IsTrue(ShownWith(title, panel, new Vector2(width, height), touch),
                        panel.name + " の枠が帰属表示にかかり、文字が隠れる (" + width + "x" + height + ")");
                }
            });
        }

        /// <summary>
        /// screen の画面で panel を開いたときに、帰属表示が文字を出しているか。シーンの Canvas の設定から Canvas の大きさを
        /// 求め、帰属表示と枠の RectTransform をメモリに写して、Sync で決めさせる。
        /// </summary>
        private bool ShownWith(TitleParts title, RectTransform panel, Vector2 screen, bool touch)
        {
            Vector2 size = screen / CanvasScreen.Factor(title.Scaler, screen, touch);
            bool portrait = ScreenShape.IsPortraitSize(screen.x, screen.y);
            RectTransform canvas = MakeCanvas(size);
            RectTransform box = Copy(canvas, title.Box, size, portrait);
            RectTransform text = Copy(box, title.TextRect, size, portrait);

            MapAttributionLabel attribution = box.gameObject.AddComponent<MapAttributionLabel>();
            attribution.Bind(text.gameObject.AddComponent<TextMeshProUGUI>());
            attribution.Avoid(Copy(canvas, panel, size, portrait));
            attribution.Sync();
            return attribution.IsShown;
        }

        /// <summary>
        /// from と同じ置き方の四角を parent の下に作る。from に FitInCanvas があれば、FitInCanvas と同じく
        /// 幅を Canvas に合わせて狭め（縦長の画面では高さを差し替え）、Canvas に収まるまで縮める。
        /// </summary>
        private static RectTransform Copy(RectTransform parent, RectTransform from, Vector2 canvas, bool portrait)
        {
            var go = new GameObject(from.name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = from.anchorMin;
            rect.anchorMax = from.anchorMax;
            rect.pivot = from.pivot;
            rect.anchoredPosition = from.anchoredPosition;
            rect.sizeDelta = from.sizeDelta;
            if (!from.TryGetComponent(out FitInCanvas fit))
            {
                return rect;
            }

            var serialized = new SerializedObject(fit);
            if (serialized.FindProperty("_reflow").boolValue && (portrait || !serialized.FindProperty("_portraitOnly").boolValue))
            {
                float portraitHeight = serialized.FindProperty("_portraitHeight").floatValue;
                rect.sizeDelta = new Vector2(FitInCanvas.WidthToFit(from.sizeDelta.x, canvas.x, FitInCanvas.Margin),
                    portrait && portraitHeight > 0f ? portraitHeight : from.sizeDelta.y);
            }

            float scale = FitInCanvas.ScaleToFit(rect.rect.size, canvas, FitInCanvas.Margin);
            rect.localScale = new Vector3(scale, scale, 1f);
            return rect;
        }

        /// <summary>Canvas の代わりにする、左下が原点の size の四角。TearDown で消す。</summary>
        private RectTransform MakeCanvas(Vector2 size)
        {
            var go = new GameObject("CanvasForTest", typeof(RectTransform));
            _created.Add(go);
            var rect = (RectTransform)go.transform;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>parent の anchor の点を基準に、position だけずらして置く size の四角。</summary>
        private static RectTransform MakeRect(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>シーンにちょうど 1 つある T。</summary>
        private static T Only<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                found.AddRange(go.GetComponentsInChildren<T>(true));
            }

            Assert.AreEqual(1, found.Count,
                TitleScene + " に " + typeof(T).Name + " が 1 つだけあるはず（無ければ SceneBuilder.BuildAll でシーンを組み直す）");
            return found[0];
        }

        /// <summary>SettingsView・CreditsView が開け閉めする枠。</summary>
        private static RectTransform ViewRoot(Component view)
        {
            var root = new SerializedObject(view).FindProperty("_root").objectReferenceValue as GameObject;
            Assert.IsNotNull(root, view.GetType().Name + " に開け閉めする枠が無い");
            return (RectTransform)root.transform;
        }

        /// <summary>Title シーンを開き、帰属表示とそれが避ける枠を渡す。</summary>
        private static void WithTitle(Action<TitleParts> body)
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScene, OpenSceneMode.Additive);
            try
            {
                MapAttributionLabel label = Only<MapAttributionLabel>(scene);
                var serialized = new SerializedObject(label);
                var text = serialized.FindProperty("_label").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(text, "帰属表示に文字が無い");
                SerializedProperty avoid = serialized.FindProperty("_avoid");
                var panels = new RectTransform[avoid.arraySize];
                for (int i = 0; i < panels.Length; i++)
                {
                    panels[i] = avoid.GetArrayElementAtIndex(i).objectReferenceValue as RectTransform;
                }

                var box = (RectTransform)label.transform;
                Assert.AreEqual(box, text.rectTransform.parent, "帰属表示の文字が枠の直下に無い");
                Assert.IsTrue(box.parent.TryGetComponent(out CanvasScaler scaler), "帰属表示が Canvas の直下に無い");
                RectTransform settings = ViewRoot(Only<SettingsView>(scene));
                RectTransform credits = ViewRoot(Only<CreditsView>(scene));
                Assert.AreEqual(box.parent, settings.parent, "設定の枠が帰属表示と同じ Canvas の直下に無い");
                Assert.AreEqual(box.parent, credits.parent, "クレジットの枠が帰属表示と同じ Canvas の直下に無い");
                body(new TitleParts(label, box, text.rectTransform, scaler, settings, credits, panels));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Title シーンの、帰属表示とそれが避ける枠。</summary>
        private sealed class TitleParts
        {
            public readonly MapAttributionLabel Label;
            public readonly RectTransform Box;
            public readonly RectTransform TextRect;
            public readonly CanvasScaler Scaler;
            public readonly RectTransform Settings;
            public readonly RectTransform Credits;
            public readonly RectTransform[] Avoid;

            public TitleParts(MapAttributionLabel label, RectTransform box, RectTransform textRect, CanvasScaler scaler,
                RectTransform settings, RectTransform credits, RectTransform[] avoid)
            {
                Label = label;
                Box = box;
                TextRect = textRect;
                Scaler = scaler;
                Settings = settings;
                Credits = credits;
                Avoid = avoid;
            }
        }
    }
}
