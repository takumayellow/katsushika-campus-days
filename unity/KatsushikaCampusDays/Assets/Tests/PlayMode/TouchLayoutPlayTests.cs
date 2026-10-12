using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        }

        [TearDown]
        public void Release()
        {
            InputMode.Force(null);
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
    }
}
