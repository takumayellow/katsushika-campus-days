using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// スマホでのタイトル画面の置き方 (#187)。縦持ちでは 3 人が幅に収まらず選んだ子が画面の外に出ていた。
    /// 横持ちでは 1.5 倍に描いた題名と案内がキャラに重なっていた。
    /// 文字の枠どうしが重ならないこと、空いた帯にカメラが舞台を収めることを確かめる。
    /// </summary>
    public sealed class TitleLayoutTests
    {
        private const string TitleScenePath = "Assets/Scenes/Title.unity";

        /// <summary>地図の帰属表示の上端（下端から）。主人公選びの案内はこれより上に置く。</summary>
        private const float AttributionTop = 68f;

        private static readonly TitleLayout.Shape[] Shapes =
            { TitleLayout.Shape.Desktop, TitleLayout.Shape.LandscapeTouch, TitleLayout.Shape.Portrait };

        // ---- 画面の種類 ----

        [TestCase(false, false, TitleLayout.Shape.Desktop)]
        [TestCase(false, true, TitleLayout.Shape.LandscapeTouch)]
        [TestCase(true, false, TitleLayout.Shape.Portrait)]
        [TestCase(true, true, TitleLayout.Shape.Portrait)]
        public void ShapeFor_PicksPortraitBeforeTouch(bool portrait, bool touch, TitleLayout.Shape expected)
        {
            Assert.AreEqual(expected, TitleLayout.ShapeFor(portrait, touch));
        }

        // ---- 文字の枠 ----

        [Test]
        public void Slots_DoNotOverlapEachOther()
        {
            foreach (TitleLayout.Shape shape in Shapes)
            {
                TitleLayout.Slot title = TitleLayout.SlotFor(shape, TitleLayout.Part.Title);
                TitleLayout.Slot subtitle = TitleLayout.SlotFor(shape, TitleLayout.Part.Subtitle);
                Assert.GreaterOrEqual(-subtitle.Position.y, title.Reach, shape + ": 副題が題名に重なる");

                TitleLayout.Slot name = TitleLayout.SlotFor(shape, TitleLayout.Part.Name);
                TitleLayout.Slot tagline = TitleLayout.SlotFor(shape, TitleLayout.Part.Tagline);
                TitleLayout.Slot hint = TitleLayout.SlotFor(shape, TitleLayout.Part.Hint);
                Assert.GreaterOrEqual(name.Position.y, tagline.Reach, shape + ": 紹介文が名前に重なる");
                Assert.GreaterOrEqual(tagline.Position.y, hint.Reach, shape + ": 案内が紹介文に重なる");
                Assert.GreaterOrEqual(hint.Position.y, AttributionTop, shape + ": 案内が地図の帰属表示に重なる");
            }
        }

        [Test]
        public void TouchPrompt_HasRoomForTwoLines()
        {
            // 指のときの案内は「タップではじめる」とクレジット・設定の 2 行。高さ 64 では文字が極小になっていた。
            foreach (TitleLayout.Shape shape in new[] { TitleLayout.Shape.LandscapeTouch, TitleLayout.Shape.Portrait })
            {
                // 余白 24 を除いた高さに、2 行目を 0.7 倍の大きさで書いた 1.7 行分（行間 1.2）が入ること。
                TitleLayout.Slot prompt = TitleLayout.SlotFor(shape, TitleLayout.Part.Prompt);
                float inner = prompt.Size.y - 24f;
                Assert.GreaterOrEqual(inner, prompt.FontSize * 1.7f * 1.2f, shape + ": 案内の 2 行が入らない");
            }
        }

        // ---- カメラ ----

        /// <summary>横持ちのスマホ（Canvas 約 1412×653）と縦持ち（800×1731、iPhone SE は 800×1423）。</summary>
        [TestCase(TitleLayout.Shape.LandscapeTouch, 844f / 390f, 653f, false)]
        [TestCase(TitleLayout.Shape.LandscapeTouch, 844f / 390f, 653f, true)]
        [TestCase(TitleLayout.Shape.LandscapeTouch, 4f / 3f, 831f, false)]
        [TestCase(TitleLayout.Shape.Portrait, 390f / 844f, 1731f, false)]
        [TestCase(TitleLayout.Shape.Portrait, 390f / 844f, 1731f, true)]
        [TestCase(TitleLayout.Shape.Portrait, 375f / 667f, 1423f, false)]
        public void Camera_FitsTheStageBetweenTheLabels(TitleLayout.Shape shape, float aspect, float canvasHeight,
            bool selecting)
        {
            bool portrait = shape == TitleLayout.Shape.Portrait;
            const float pitch = 6f;
            float fieldOfView = portrait ? TitleLayout.PortraitFieldOfView : 42f;
            Vector2 band = TitleLayout.StageBand(shape, selecting, canvasHeight);
            Assert.Greater(band.y - band.x, 0.6f, "舞台を置く帯が狭すぎる");

            float halfWidth = portrait ? TitleLayout.CarouselHalfWidth : TitleLayout.RowHalfWidth;
            float distance = TitleLayout.FrameDistance(TitleLayout.StageHeight, halfWidth, pitch, fieldOfView,
                aspect, band.y, band.x, TitleLayout.WidthLimit, TitleLayout.MaxHalfHeight);
            float centerY = (band.x + band.y) * 0.5f;
            var center = new Vector3(0f, TitleLayout.StageCenterY, 0f);
            Vector3 position = TitleLayout.FramePosition(center, pitch, fieldOfView, distance, centerY);

            var go = new GameObject("TitleLayoutTestCamera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.fieldOfView = fieldOfView;
                camera.aspect = aspect;
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, 0f, 0f));

                Assert.AreEqual((centerY + 1f) * 0.5f, camera.WorldToViewportPoint(center).y, 1e-3f,
                    "舞台の真ん中が帯の真ん中に来ない");

                float half = TitleLayout.StageHeight * 0.5f;
                float topY = camera.WorldToViewportPoint(center + Vector3.up * half).y * 2f - 1f;
                float bottomY = camera.WorldToViewportPoint(center - Vector3.up * half).y * 2f - 1f;
                Assert.LessOrEqual(topY, band.y + 0.03f, "舞台の上が題名に掛かる");
                Assert.GreaterOrEqual(bottomY, band.x - 0.03f, "舞台の下が案内に掛かる");
                Assert.Greater(topY - bottomY, 0.6f, "キャラが小さすぎる");

                float sideX = camera.WorldToViewportPoint(new Vector3(halfWidth, 0f, 0f)).x * 2f - 1f;
                Assert.LessOrEqual(sideX, TitleLayout.WidthLimit + 1e-3f, "舞台の端が画面の外に出る");

                if (portrait)
                {
                    const float idleDepth = 1.6f;
                    float spacing = TitleLayout.NeighbourSpacing(distance + idleDepth, fieldOfView, aspect,
                        TitleLayout.NeighbourOffset);
                    float neighbourX = camera.WorldToViewportPoint(new Vector3(spacing, 0.8f, idleDepth)).x * 2f - 1f;
                    Assert.AreEqual(TitleLayout.NeighbourOffset, neighbourX, 0.06f, "隣の子が画面の端からのぞかない");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---- シーン ----

        [Test]
        public void TitleScene_BindsTheLayout()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                TitleLayout layout = roots.SelectMany(r => r.GetComponentsInChildren<TitleLayout>(true)).FirstOrDefault();
                Assert.IsNotNull(layout, "TitleLayout が無い（KCD/シーンを組み直す）");

                var serialized = new SerializedObject(layout);
                Assert.IsNotNull(serialized.FindProperty("_camera").objectReferenceValue, "カメラが差し込まれていない");
                Assert.IsNotNull(serialized.FindProperty("_select").objectReferenceValue, "主人公選びが差し込まれていない");
                SerializedProperty parts = serialized.FindProperty("_parts");
                Assert.AreEqual(System.Enum.GetValues(typeof(TitleLayout.Part)).Length, parts.arraySize);
                for (int i = 0; i < parts.arraySize; i++)
                {
                    // 枠は Part の順に並ぶ（TitleLayout は添字を Part として表を引く）。
                    Object part = parts.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.IsNotNull(part, "枠 " + i + " が無い");
                    Assert.AreEqual(((TitleLayout.Part)i).ToString(), part.name, "枠の並びが Part と違う");
                }

                Assert.IsEmpty(roots.SelectMany(r => r.GetComponentsInChildren<PortraitFieldOfView>(true)),
                    "タイトルのカメラに PortraitFieldOfView がある（TitleLayout と画角を取り合う）");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
