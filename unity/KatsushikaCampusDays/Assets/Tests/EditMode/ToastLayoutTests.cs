using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 知らせ（トースト）の文字 (#187)。枠の高さは 60 で、指のときは「話す」の案内のすぐ上に出る。
    /// 長い知らせを 28 のまま 2 行に折り返すと枠の上下にはみ出して案内にかかるので、22 まで縮めて枠の中に収める。
    /// </summary>
    public sealed class ToastLayoutTests
    {
        private const string CampusScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>縮めてよい下限。横持ちのスマホ（844×390、1 単位 CSS 約 0.6 px）で 13 px 前後。</summary>
        private const float SmallestReadableSize = 22f;

        /// <summary>1 行で出る知らせ（開始直後の地名）。</summary>
        private const string OneLine = "東京理科大学 葛飾キャンパス";

        /// <summary>幅 1044 では 22 でも 2 行に折り返す長さ（指のときの操作の案内を 2 つつないだもの）。</summary>
        private const string TwoLines =
            "左側をなぞって移動　右側をなぞって見回す　2 本指で寄る・引く　人の近くで「話す」　会話は画面を叩いて進める";

        [Test]
        public void CampusScene_ToastShrinksLongTextInsideItsBox()
        {
            Scene scene = EditorSceneManager.OpenScene(CampusScenePath, OpenSceneMode.Additive);
            try
            {
                RectTransform toast = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true))
                    .FirstOrDefault(r => r.name == "Toast" && r.Find("Backdrop") != null);
                Assert.IsNotNull(toast, "Toast の枠が無い");
                TMP_Text label = toast.Find("Label")?.GetComponent<TMP_Text>();
                Assert.IsNotNull(label, "Toast に文字が無い");
                Assert.IsTrue(label.enableAutoSizing, "長い知らせを縮めない（KCD/シーンを組み直す）");
                Assert.GreaterOrEqual(label.fontSizeMin, SmallestReadableSize, "知らせを縮めすぎて読めない");
                Assert.Less(label.fontSizeMin, label.fontSizeMax);

                Vector2 inner = toast.sizeDelta + label.rectTransform.sizeDelta;
                label.enableAutoSizing = false;

                label.fontSize = label.fontSizeMax;
                float one = label.GetPreferredValues(OneLine, inner.x, 0f).y;
                Assert.LessOrEqual(one, inner.y,
                    "1 行の知らせが縮む（要る高さ " + one + "、文字の入る高さ " + inner.y + "）");

                label.fontSize = label.fontSizeMin;
                float single = label.GetPreferredValues(OneLine, inner.x, 0f).y;
                float wrapped = label.GetPreferredValues(TwoLines, inner.x, 0f).y;
                Assert.Greater(wrapped, single * 1.5f, "例の文が 2 行に折り返していない（幅 " + inner.x + "）");
                Assert.LessOrEqual(wrapped, toast.sizeDelta.y,
                    "2 行の知らせが枠からはみ出す（要る高さ " + wrapped + "、枠 " + toast.sizeDelta.y + "）");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
