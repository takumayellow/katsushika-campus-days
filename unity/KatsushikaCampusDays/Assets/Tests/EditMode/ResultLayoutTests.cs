using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 縦持ちのスマホでのリザルト (#187)。幅 1100 の枠を Canvas の幅 800 に縮めると本文が 10 px ほどになるので、
    /// 縦長では縮めずに幅を合わせ、ランクの下に内訳を置く。横長は組んだときの並びのまま枠ごと縮める。
    /// 枠と中の箱の大きさは Campus.unity のものを使い、アンカーから箱の四角を計算して確かめる。
    /// </summary>
    public sealed class ResultLayoutTests
    {
        /// <summary>縦持ちの Canvas の幅（TouchCanvasScale.PortraitWidth）。</summary>
        private const float PortraitCanvasWidth = 800f;

        /// <summary>いちばん低い縦持ちの Canvas の高さ（iPhone SE、375×667）。</summary>
        private const float ShortestPortraitCanvasHeight = 1423f;

        private static readonly string[] Boxes = { "Heading", "Rank", "Body", "Choices" };

        [Test]
        public void Frame_ReflowsOnlyInPortrait()
        {
            WithResult(frame =>
            {
                var fit = new SerializedObject(frame.GetComponent<FitInCanvas>());
                Assert.IsTrue(fit.FindProperty("_reflow").boolValue, "縦持ちで枠ごと縮めている");
                Assert.IsTrue(fit.FindProperty("_portraitOnly").boolValue, "横持ちで幅を狭めるとランクと内訳が重なる");
                float height = fit.FindProperty("_portraitHeight").floatValue;
                Assert.Greater(height, frame.sizeDelta.y, "縦持ちでランクの下に内訳を置く高さが無い");
                Assert.LessOrEqual(height, ShortestPortraitCanvasHeight - FitInCanvas.Margin * 2f,
                    "iPhone SE の縦に収まらない");
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Boxes_StayInsideTheFrameWithoutOverlapping(bool portrait)
        {
            WithResult(frame =>
            {
                Vector2 size = frame.sizeDelta;
                if (portrait)
                {
                    float height = new SerializedObject(frame.GetComponent<FitInCanvas>())
                        .FindProperty("_portraitHeight").floatValue;
                    size = new Vector2(FitInCanvas.WidthToFit(size.x, PortraitCanvasWidth, FitInCanvas.Margin), height);
                }

                var frameRect = new Rect(Vector2.zero, size);
                var rects = new Dictionary<string, Rect>();
                foreach (string name in Boxes)
                {
                    var box = (RectTransform)frame.Find(name);
                    Assert.IsNotNull(box, name + " が無い");
                    rects[name] = Place(box, size, portrait);
                    Assert.IsTrue(Inside(frameRect, rects[name]),
                        (portrait ? "縦" : "横") + "で " + name + " が枠からはみ出す: " + rects[name]);
                }

                foreach (string a in Boxes)
                {
                    foreach (string b in Boxes.Where(b => string.CompareOrdinal(a, b) < 0))
                    {
                        Assert.IsFalse(rects[a].Overlaps(rects[b]),
                            (portrait ? "縦" : "横") + "で " + a + " と " + b + " が重なる");
                    }
                }
            });
        }

        /// <summary>枠の大きさが size のときの箱の四角（枠の左下が原点）。縦長では PortraitPosition の置き方を使う。</summary>
        private static Rect Place(RectTransform box, Vector2 size, bool portrait)
        {
            Vector2 anchorMin = box.anchorMin;
            Vector2 anchorMax = box.anchorMax;
            Vector2 position = box.anchoredPosition;
            PortraitPosition moved = box.GetComponent<PortraitPosition>();
            if (portrait && moved != null)
            {
                var serialized = new SerializedObject(moved);
                anchorMin = anchorMax = serialized.FindProperty("_portraitAnchor").vector2Value;
                position = serialized.FindProperty("_portraitPosition").vector2Value;
            }

            Vector2 boxSize = Vector2.Scale(anchorMax - anchorMin, size) + box.sizeDelta;
            // anchoredPosition はアンカーの四角の中で pivot と同じ割合の点から測る。
            Vector2 anchorPoint = Vector2.Scale(anchorMin, size) +
                                  Vector2.Scale(Vector2.Scale(anchorMax - anchorMin, size), box.pivot);
            Vector2 min = anchorPoint + position - Vector2.Scale(box.pivot, boxSize);
            return new Rect(min, boxSize);
        }

        private static bool Inside(Rect outer, Rect inner)
        {
            const float slack = 0.01f;
            return inner.xMin >= outer.xMin - slack && inner.yMin >= outer.yMin - slack &&
                   inner.xMax <= outer.xMax + slack && inner.yMax <= outer.yMax + slack;
        }

        private static void WithResult(Action<RectTransform> body)
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Campus.unity", OpenSceneMode.Additive);
            try
            {
                ResultScreen view = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<ResultScreen>(true)).FirstOrDefault();
                Assert.IsNotNull(view, "ResultScreen が無い（KCD/シーンを組み直す）");
                var root = new SerializedObject(view).FindProperty("_root").objectReferenceValue as GameObject;
                Assert.IsNotNull(root, "枠が差し込まれていない");
                body((RectTransform)root.transform);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
