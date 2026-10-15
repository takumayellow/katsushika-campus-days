using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// 指で遊ぶときに HUD の部品が大きさと位置を変え、キーボード・マウスに戻すと元に戻ること (#187)。
    /// 大きさだけ縮めて角からの距離を縮めないと、縮めた分だけ枠が画面の内側へずれる。
    /// </summary>
    public sealed class TouchLayoutPlayTests
    {
        private GameObject _canvas;

        [SetUp]
        public void CreateCanvas()
        {
            _canvas = new GameObject("TouchLayoutPlayTests", typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            // Game ビューの縦横に左右されないよう、向きを決めておく。
            ScreenShape.Force(false);
        }

        [TearDown]
        public void Release()
        {
            InputMode.Force(null);
            ScreenShape.Force(null);
            KCDInput.Unblock(this);
            Object.Destroy(_canvas);
        }

        /// <summary>右上の角に置いた枠。ミニマップと時計と同じ置き方。</summary>
        private RectTransform Corner(Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject("Corner", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        [UnityTest]
        public IEnumerator KeepPointerSize_ShrinksWithItsDistanceFromTheCorner()
        {
            InputMode.Force(InputKind.Pointer);
            RectTransform rect = Corner(Vector2.one, new Vector2(-40f, -340f), new Vector2(300f, 108f));
            rect.gameObject.AddComponent<KeepPointerSize>();
            yield return null;
            Assert.AreEqual(Vector3.one, rect.localScale);

            InputMode.Force(InputKind.Touch);
            yield return null;
            float factor = KeepPointerSize.Factor(true);
            Assert.AreEqual(factor, rect.localScale.x, 1e-5f);
            Assert.AreEqual(new Vector2(-40f, -340f) * factor, rect.anchoredPosition);

            InputMode.Force(InputKind.Pointer);
            yield return null;
            Assert.AreEqual(Vector3.one, rect.localScale);
            Assert.AreEqual(new Vector2(-40f, -340f), rect.anchoredPosition, "キーボード・マウスに戻しても元の場所に戻らない");
        }

        [UnityTest]
        public IEnumerator KeepLeftOfCenter_ShrinksAWideTrackerTowardsItsCorner()
        {
            float width = ((RectTransform)_canvas.transform).rect.width;
            Assert.Greater(width, 0f);
            RectTransform rect = Corner(new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(width * 0.6f, 150f));
            rect.gameObject.AddComponent<KeepLeftOfCenter>();
            yield return null;

            float scale = KeepLeftOfCenter.ScaleToShare(40f + width * 0.6f, width, KeepLeftOfCenter.Share);
            Assert.Less(scale, 1f);
            Assert.AreEqual(scale, rect.localScale.x, 1e-5f);
            Assert.AreEqual(40f * scale, rect.anchoredPosition.x, 1e-3f);
            Assert.AreEqual(width * KeepLeftOfCenter.Share, rect.anchoredPosition.x + rect.rect.width * scale, 1e-2f,
                "右端が Canvas の幅の Share で止まらない");
        }

        [UnityTest]
        public IEnumerator TouchPosition_MovesOnlyWhileTheButtonsAreShown()
        {
            InputMode.Force(InputKind.Pointer);
            RectTransform rect = Corner(new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1080f, 60f));
            rect.gameObject.AddComponent<TouchPosition>().Bind(new Vector2(0f, 262f));
            yield return null;
            Assert.AreEqual(new Vector2(0f, 96f), rect.anchoredPosition);

            InputMode.Force(InputKind.Touch);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 262f), rect.anchoredPosition);

            KCDInput.Block(this);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 96f), rect.anchoredPosition, "ボタンが隠れるポーズ中も上げたまま");

            KCDInput.Unblock(this);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 262f), rect.anchoredPosition);

            InputMode.Force(InputKind.Pointer);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 96f), rect.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator TouchPosition_UsesThePortraitPlaceOnAPortraitScreen()
        {
            InputMode.Force(InputKind.Touch);
            RectTransform rect = Corner(new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1080f, 60f));
            rect.gameObject.AddComponent<TouchPosition>().Bind(new Vector2(0f, 262f), new Vector2(0f, 452f));
            yield return null;
            Assert.AreEqual(new Vector2(0f, 262f), rect.anchoredPosition);

            ScreenShape.Force(true);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 452f), rect.anchoredPosition);

            ScreenShape.Force(false);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 262f), rect.anchoredPosition, "横に戻しても縦の場所のまま");
        }

        [UnityTest]
        public IEnumerator PortraitPosition_MovesOnlyOnAPortraitScreen()
        {
            RectTransform rect = Corner(new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(96f, 96f));
            rect.gameObject.AddComponent<PortraitPosition>().Bind(new Vector2(0f, 1f), new Vector2(84f, -236f));
            yield return null;
            Assert.AreEqual(new Vector2(0.5f, 1f), rect.anchorMin);
            Assert.AreEqual(new Vector2(0f, -64f), rect.anchoredPosition);

            ScreenShape.Force(true);
            yield return null;
            Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMin);
            Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMax);
            Assert.AreEqual(new Vector2(84f, -236f), rect.anchoredPosition);

            ScreenShape.Force(false);
            yield return null;
            Assert.AreEqual(new Vector2(0.5f, 1f), rect.anchorMin, "横に戻しても縦のアンカーのまま");
            Assert.AreEqual(new Vector2(0.5f, 1f), rect.anchorMax);
            Assert.AreEqual(new Vector2(0f, -64f), rect.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator FitInCanvas_ReflowNarrowsInsteadOfShrinking()
        {
            float width = ((RectTransform)_canvas.transform).rect.width;
            Assert.Greater(width, FitInCanvas.Margin * 2f);
            RectTransform rect = Corner(new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(width * 2f, 100f));
            rect.gameObject.AddComponent<FitInCanvas>().BindReflow(300f);
            yield return null;
            Assert.AreEqual(Vector3.one, rect.localScale, "折り返す枠を縮めている");
            Assert.AreEqual(new Vector2(width - FitInCanvas.Margin * 2f, 100f), rect.sizeDelta);

            ScreenShape.Force(true);
            yield return null;
            Assert.AreEqual(300f, rect.sizeDelta.y, 1e-3f, "縦長の画面で高さを足していない");

            ScreenShape.Force(false);
            yield return null;
            Assert.AreEqual(100f, rect.sizeDelta.y, 1e-3f, "横に戻しても縦の高さのまま");
        }

        [UnityTest]
        public IEnumerator TouchCanvasScale_FollowsTheWidthOnAPortraitScreen()
        {
            InputMode.Force(InputKind.Pointer);
            CanvasScaler scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = TouchCanvasScale.PointerReference;
            scaler.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<TouchCanvasScale>();
            yield return null;
            Assert.AreEqual(TouchCanvasScale.PointerReference, scaler.referenceResolution);

            ScreenShape.Force(true);
            yield return null;
            Assert.AreEqual(TouchCanvasScale.PortraitReference(TouchCanvasScale.PortraitWidth), scaler.referenceResolution);
            Assert.AreEqual(0f, scaler.matchWidthOrHeight);

            ScreenShape.Force(false);
            yield return null;
            Assert.AreEqual(TouchCanvasScale.PointerReference, scaler.referenceResolution);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, "横に戻しても幅に合わせたまま");
        }
    }
}
