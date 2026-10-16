using System;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときのエレベーターの画面 (#187)。叩いた点は一番近い行になるので、叩ける高さは行の間隔そのもの。
    /// 文字の大きさ 30 の行の間隔は 36 ほどで、横持ちのスマホ（高さ 720 の Canvas を 390 px）では 20 px に届かなかった。
    /// 指のときは行の間隔を広げ、いちばん階の多い建物（7 階）でも枠を縮めずに収める。
    /// </summary>
    public sealed class ElevatorLayoutTests
    {
        /// <summary>縦持ちの Canvas の幅（TouchCanvasScale.PortraitWidth）と、いちばん低い縦持ちの Canvas の高さ（iPhone SE）。</summary>
        private static readonly Vector2 ShortestPortraitCanvas = new Vector2(800f, 1423f);

        /// <summary>指で遊ぶ横長の画面の Canvas（TouchCanvasScale.TouchReference）。</summary>
        private static readonly Vector2 TouchLandscapeCanvas = new Vector2(1280f, 720f);

        private const float FontSize = 30f;

        [Test]
        public void Compose_WithLinks_WidensTheRowsAndDropsTheBlankLines()
        {
            ElevatorPanel.Stop[] stops = Stops(7);
            string keys = ElevatorMenu.Compose(stops, 0, 0, false);
            string portrait = ElevatorMenu.Compose(stops, 0, 0, true, false, true);
            string landscape = ElevatorMenu.Compose(stops, 0, 0, true, false, false);

            StringAssert.DoesNotContain("<line-height", keys);
            StringAssert.StartsWith(TmpLinkTap.TapRowLineHeight, portrait);
            StringAssert.StartsWith(ElevatorMenu.LandscapeTapRowLineHeight, landscape);
            // 見出し、7 階、「やめる」の 9 行。空行を挟まない。
            Assert.AreEqual(9, portrait.TrimEnd('\n').Split('\n').Length, portrait);
            Assert.AreEqual(9, landscape.TrimEnd('\n').Split('\n').Length, landscape);
        }

        [Test]
        public void FrameSize_WithKeysIsTheBuiltOne()
        {
            WithMenu((frame, body) => Assert.AreEqual(ElevatorMenu.FrameSize(false, false), frame.sizeDelta));
        }

        [TestCase("ja", true)]
        [TestCase("ja", false)]
        [TestCase("en", true)]
        [TestCase("en", false)]
        public void TallestBuilding_FitsAtFullSizeWithFingerRows(string locale, bool portrait)
        {
            In(locale, () => WithMenu((frame, body) =>
            {
                Vector2 size = ElevatorMenu.FrameSize(true, portrait);
                Vector2 canvas = portrait ? ShortestPortraitCanvas : TouchLandscapeCanvas;
                Assert.AreEqual(1f, FitInCanvas.ScaleToFit(size, canvas, FitInCanvas.Margin), 1e-4f,
                    (portrait ? "縦" : "横") + "で枠が縮む");

                // 今の階は「（いまの階）」が付いていちばん長くなる。いちばん長い名前の階に付ける。
                ElevatorPanel.Stop[] stops = Stops(7);
                string text = ElevatorMenu.Compose(stops, 6, 0, true, false, portrait);
                Vector2 inner = size + body.rectTransform.sizeDelta;
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "行が折り返す");
                Assert.LessOrEqual(needed, inner.y, (portrait ? "縦" : "横") + "で枠に入らない");
            }));
        }

        [Test]
        public void FingerRows_AreAtLeastTheFingerHeight()
        {
            // 叩ける高さ（行の間隔）を CSS の画素で測る。縦持ちは幅 800 を 390 px、横持ちは高さ 720 を 390 px で描く。
            WithMenu((frame, body) =>
            {
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float portrait = RowPitch(body, true) * 390f / ShortestPortraitCanvas.x;
                float landscape = RowPitch(body, false) * 390f / TouchLandscapeCanvas.y;
                Assert.GreaterOrEqual(portrait, 40f, "縦で行が指より細い");
                Assert.GreaterOrEqual(landscape, 30f, "横で行が指より細い");
            });
        }

        /// <summary>行を 1 つ足したときに増える高さ。</summary>
        private static float RowPitch(TMP_Text body, bool portrait)
        {
            float seven = body.GetPreferredValues(ElevatorMenu.Compose(Stops(7), 0, 0, true, false, portrait), 100000f, 0f).y;
            float six = body.GetPreferredValues(ElevatorMenu.Compose(Stops(6), 0, 0, true, false, portrait), 100000f, 0f).y;
            return seven - six;
        }

        /// <summary>count 階の建物。説明はシーンにある中でいちばん長いもの。</summary>
        private static ElevatorPanel.Stop[] Stops(int count)
        {
            return Enumerable.Range(1, count).Select(i => new ElevatorPanel.Stop
            {
                Floor = i + "F",
                CaptionJa = "講義室・PC ルーム・ラウンジ",
                CaptionEn = "Classrooms, PC Room & Lounges",
            }).ToArray();
        }

        private static void WithMenu(Action<RectTransform, TMP_Text> body)
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Campus.unity", OpenSceneMode.Additive);
            try
            {
                ElevatorMenu view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<ElevatorMenu>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "ElevatorMenu が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(view);
                var root = serialized.FindProperty("_root").objectReferenceValue as GameObject;
                var label = serialized.FindProperty("_body").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                Assert.IsNotNull(label, "本文が差し込まれていない");
                body((RectTransform)root.transform, label);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>言語を切り替えて body を走らせ、言語と保存済みの設定を元に戻す（LocalizedTextTests と同じ）。</summary>
        private static void In(string locale, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
                Assert.AreEqual(locale, L.Locale);
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
    }
}
