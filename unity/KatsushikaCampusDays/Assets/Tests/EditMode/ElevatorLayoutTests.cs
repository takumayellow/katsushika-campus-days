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

        /// <summary>
        /// 指で遊ぶ横長の画面の Canvas。基準 1280x720 に幅と高さを 0.5 ずつ合わせる（CanvasScaler）ので、
        /// 844x390 も 780x360 も Canvas の高さは 652。
        /// </summary>
        private static Vector2 LandscapeCanvas(float width, float height)
        {
            return new Vector2(width, height) / LandscapeScale(width, height);
        }

        /// <summary>横長の画面で Canvas の 1 単位が何 px か。</summary>
        private static float LandscapeScale(float width, float height)
        {
            return Mathf.Sqrt(width / 1280f * (height / 720f));
        }

        private const float FontSize = 30f;

        [Test]
        public void Compose_WithLinks_WidensTheRowsAndDropsTheBlankLines()
        {
            ElevatorPanel.Stop[] stops = Stops(7);
            string keys = ElevatorMenu.Compose(stops, 0, 0, false);
            string portrait = ElevatorMenu.Compose(stops, 0, 0, true, false, true);
            string landscape = ElevatorMenu.Compose(stops, 0, 0, true, false, false);

            StringAssert.DoesNotContain("<line-height", keys);
            StringAssert.StartsWith(TmpLinkTap.RowHeight(ElevatorMenu.PortraitRowEm), portrait);
            StringAssert.StartsWith(TmpLinkTap.RowHeight(ElevatorMenu.RowEm(7, false)), landscape);
            // 縦は見出し、7 階、「やめる」の 9 行。空行を挟まない。
            Assert.AreEqual(9, portrait.TrimEnd('\n').Split('\n').Length, portrait);
            // 横の 7 階は「やめる」を見出しの行に入れて 8 行。
            Assert.AreEqual(8, landscape.TrimEnd('\n').Split('\n').Length, landscape);
            StringAssert.Contains("<link=\"choice:7\">", landscape.Split('\n')[0], "「やめる」が見出しの行に無い");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(landscape, "choice:7").Count);
        }

        [Test]
        public void Compose_WithFewFloors_KeepsCancelOnItsOwnRow()
        {
            string landscape = ElevatorMenu.Compose(Stops(5), 0, 0, true, false, false);
            Assert.AreEqual(7, landscape.TrimEnd('\n').Split('\n').Length, landscape);
            StringAssert.DoesNotContain("choice:5", landscape.Split('\n')[0]);
            StringAssert.StartsWith(TmpLinkTap.RowHeight(3f), landscape);
        }

        [TestCase(7, false, 2.6f)]
        [TestCase(6, false, 2.6f)]
        [TestCase(5, false, 3f)]
        [TestCase(4, false, 3f)]
        [TestCase(2, false, 3f)]
        [TestCase(7, true, 3.3f)]
        [TestCase(5, true, 3.3f)]
        public void RowEm_FollowsTheNumberOfFloors(int floors, bool portrait, float expected)
        {
            Assert.AreEqual(expected, ElevatorMenu.RowEm(floors, portrait), 1e-4f);
        }

        [Test]
        public void MergesCancel_OnlyForTallBuildingsOnLandscapeTouch()
        {
            Assert.IsTrue(ElevatorMenu.MergesCancel(7, true, false));
            Assert.IsFalse(ElevatorMenu.MergesCancel(7, true, true), "縦は行が足りる");
            Assert.IsFalse(ElevatorMenu.MergesCancel(7, false, false), "キーは行を詰めない");
            Assert.IsFalse(ElevatorMenu.MergesCancel(6, true, false));
        }

        [Test]
        public void FrameSize_WithKeysIsTheBuiltOne()
        {
            WithMenu((frame, body) => Assert.AreEqual(ElevatorMenu.FrameSize(false, false), frame.sizeDelta));
        }

        [TestCase("ja", true, 7)]
        [TestCase("ja", true, 5)]
        [TestCase("ja", true, 2)]
        [TestCase("ja", false, 7)]
        [TestCase("ja", false, 6)]
        [TestCase("ja", false, 5)]
        [TestCase("ja", false, 2)]
        [TestCase("en", true, 7)]
        [TestCase("en", false, 7)]
        [TestCase("en", false, 5)]
        public void EveryBuilding_FitsAtFullSizeWithFingerRows(string locale, bool portrait, int floors)
        {
            In(locale, () => WithMenu((frame, body) =>
            {
                Vector2 size = ElevatorMenu.FrameSize(true, portrait);
                Vector2[] canvases = portrait
                    ? new[] { ShortestPortraitCanvas }
                    : new[] { LandscapeCanvas(844f, 390f), LandscapeCanvas(780f, 360f) };
                foreach (Vector2 canvas in canvases)
                {
                    Assert.AreEqual(1f, FitInCanvas.ScaleToFit(size, canvas, FitInCanvas.Margin), 1e-4f,
                        (portrait ? "縦" : "横") + "で枠が縮む " + canvas);
                }

                // 今の階は「（いまの階）」が付いていちばん長くなる。いちばん長い名前の階に付ける。
                ElevatorPanel.Stop[] stops = Stops(floors);
                string text = ElevatorMenu.Compose(stops, floors - 1, 0, true, false, portrait);
                Vector2 inner = size + body.rectTransform.sizeDelta;
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "行が折り返す");
                Assert.LessOrEqual(needed, inner.y, (portrait ? "縦" : "横") + "で枠に入らない");

                // 行の間隔が指定どおり効いている（2 行目からの行数 × 間隔より低くならない）。
                int rows = text.TrimEnd('\n').Split('\n').Length;
                Assert.GreaterOrEqual(needed, (rows - 1) * ElevatorMenu.RowEm(floors, portrait) * FontSize * 0.95f,
                    "<line-height> が効いていない");
            }));
        }

        [TestCase("ja", 7)]
        [TestCase("en", 7)]
        public void MergedCancel_SitsOnTheHeadingRowInsideTheFrame(string locale, int floors)
        {
            In(locale, () => WithMenu((frame, body) =>
            {
                Vector2 size = ElevatorMenu.FrameSize(true, false);
                // 文字の位置は枠が出ている間しか組まれない。
                body.transform.parent.gameObject.SetActive(true);
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                body.text = ElevatorMenu.Compose(Stops(floors), floors - 1, 0, true, false, false);
                body.ForceMeshUpdate();
                TMP_TextInfo info = body.textInfo;
                int cancelFirst = -1;
                int cancelLast = -1;
                for (int i = 0; i < info.linkCount; i++)
                {
                    if (info.linkInfo[i].GetLinkID() == "choice:" + floors)
                    {
                        cancelFirst = info.linkInfo[i].linkTextfirstCharacterIndex;
                        cancelLast = cancelFirst + info.linkInfo[i].linkTextLength - 1;
                    }
                }

                Assert.GreaterOrEqual(cancelFirst, 0, "「やめる」の link が無い");
                Assert.AreEqual(info.characterInfo[0].lineNumber, info.characterInfo[cancelLast].lineNumber,
                    "「やめる」が見出しと別の行");
                // 本文の枠は中央が原点。右端が枠の内側の幅の半分に収まる。
                float halfInner = (size.x + body.rectTransform.sizeDelta.x) / 2f;
                Assert.LessOrEqual(info.characterInfo[cancelLast].topRight.x, halfInner, "「やめる」が枠の右にはみ出す");
                Assert.Greater(info.characterInfo[cancelFirst].bottomLeft.x, info.characterInfo[5].topRight.x,
                    "「やめる」が見出しに重なる");
            }));
        }

        [Test]
        public void FingerRows_AreAtLeastTheFingerHeight()
        {
            // 叩ける高さ（行の間隔）を画面の px で測る。縦持ちは幅 800 を 360 px、横持ちは Canvas の高さ 652 を画面の高さで描く。
            Assert.GreaterOrEqual(FontSize * ElevatorMenu.RowEm(7, true) * 360f / 800f, 44f, "縦で行が指より細い");
            Assert.GreaterOrEqual(FontSize * ElevatorMenu.RowEm(5, false) * LandscapeScale(780f, 360f), 44f, "5 階の横");
            Assert.GreaterOrEqual(FontSize * ElevatorMenu.RowEm(7, false) * LandscapeScale(844f, 390f), 44f, "7 階の横 844x390");
            Assert.GreaterOrEqual(FontSize * ElevatorMenu.RowEm(7, false) * LandscapeScale(780f, 360f), 43f, "7 階の横 780x360");
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
