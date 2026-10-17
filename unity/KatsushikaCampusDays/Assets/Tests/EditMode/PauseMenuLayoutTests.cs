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
    /// 縦持ちで指で遊ぶときのポーズ画面 (#187)。枠 820x560 は幅 800 の Canvas で 0.937 倍に縮むので、
    /// 行の間隔 3em では 390 px 幅で 41.1 px、360 px 幅で 37.9 px と指より細かった。縦では 3.5em にして、
    /// 5 行が枠の文字の領域に入ることを見る。
    /// </summary>
    public sealed class PauseMenuLayoutTests
    {
        private const float FontSize = 30f;
        private const float CanvasWidth = 800f;
        private const float FingerPx = 44f;

        [Test]
        public void PortraitRows_AreAtLeastTheFingerHeightOnTheNarrowestPhone()
        {
            Vector2 frame = WithPause((rect, body) => rect.sizeDelta);
            float fit = FitInCanvas.ScaleToFit(frame, new Vector2(CanvasWidth, 1423f), FitInCanvas.Margin);
            Assert.AreEqual(0.937f, fit, 1e-3f, "ポーズの枠の縮み方");
            float px = FontSize * PauseMenu.PortraitRowEm * fit * 360f / CanvasWidth;
            Assert.GreaterOrEqual(px, FingerPx, "360 px 幅で行が指より細い");
        }

        [TestCase("ja", false)]
        [TestCase("en", false)]
        [TestCase("ja", true)]
        [TestCase("en", true)]
        public void PortraitRows_FitInsideTheFrame(string locale, bool confirming)
        {
            In(locale, () => WithPause((rect, body) =>
            {
                body.enableAutoSizing = false;
                body.fontSize = FontSize;
                string text = PauseMenu.Compose(confirming, 0, true, true);
                Vector2 inner = rect.sizeDelta + body.rectTransform.sizeDelta;
                float needed = body.GetPreferredValues(text, inner.x, 0f).y;
                float unwrapped = body.GetPreferredValues(text, 100000f, 0f).y;
                Assert.AreEqual(unwrapped, needed, 1f, "行が折り返す");
                Assert.LessOrEqual(needed, inner.y, "縦の行が枠に入らない");

                int rows = confirming ? 2 : PauseMenu.EntryCount;
                Assert.GreaterOrEqual(needed, (rows - 1) * PauseMenu.PortraitRowEm * FontSize * 0.95f,
                    "<line-height> が効いていない");
            }));
        }

        private static T WithPause<T>(Func<RectTransform, TMP_Text, T> body)
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Campus.unity", OpenSceneMode.Additive);
            try
            {
                PauseMenu view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<PauseMenu>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "PauseMenu が無い（KCD/シーンを組み直す）");
                var serialized = new SerializedObject(view);
                var root = serialized.FindProperty("_root").objectReferenceValue as GameObject;
                var label = serialized.FindProperty("_bodyLabel").objectReferenceValue as TMP_Text;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                Assert.IsNotNull(label, "本文が差し込まれていない");
                return body((RectTransform)root.transform, label);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void WithPause(Action<RectTransform, TMP_Text> body)
        {
            WithPause<int>((rect, label) =>
            {
                body(rect, label);
                return 0;
            });
        }

        /// <summary>言語を切り替えて body を走らせ、言語と保存済みの設定を元に戻す（ElevatorLayoutTests と同じ）。</summary>
        private static void In(string locale, Action body)
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            try
            {
                L.SetLocale(locale);
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
