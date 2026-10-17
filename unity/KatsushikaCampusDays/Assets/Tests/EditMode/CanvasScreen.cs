using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// シーンの Canvas を、ある大きさの画面に置いたときの倍率と、枠の画面での四角 (#187)。テストでは画面の大きさを選べず、
    /// シーンに保存された Canvas の大きさも 0 なので、画面の大きさごとの置き場所を見るテストは、ここで倍率を求めて
    /// Canvas の大きさを出し、枠を RectTransform と同じ式で置いて画面の px にする。
    /// </summary>
    internal static class CanvasScreen
    {
        /// <summary>
        /// Canvas の 1 単位が何 px か。CanvasScaler の ScaleWithScreenSize・MatchWidthOrHeight の式に、
        /// TouchCanvasScale が画面の向きと入力で入れる基準をあてる。touch は指で遊んでいるか。
        /// </summary>
        internal static float Factor(CanvasScaler scaler, Vector2 screen, bool touch)
        {
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode, scaler.name);
            Assert.AreEqual(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, scaler.screenMatchMode, scaler.name);
            Assert.IsTrue(scaler.TryGetComponent(out TouchCanvasScale scale), scaler.name + " に TouchCanvasScale が無い");
            var serialized = new SerializedObject(scale);
            Vector2 reference;
            float match;
            if (ScreenShape.IsPortraitSize(screen.x, screen.y))
            {
                // 縦長の画面では幅に合わせる。
                reference = TouchCanvasScale.PortraitReference(serialized.FindProperty("_portraitWidth").floatValue);
                match = 0f;
            }
            else
            {
                reference = serialized.FindProperty("_followTouch").boolValue
                    ? TouchCanvasScale.ReferenceFor(touch)
                    : scaler.referenceResolution;
                match = scaler.matchWidthOrHeight;
            }

            float byWidth = Mathf.Log(screen.x / reference.x, 2f);
            float byHeight = Mathf.Log(screen.y / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(byWidth, byHeight, match));
        }

        /// <summary>scaler の Canvas にある rect の、screen の画面の px での四角（左下が原点）。</summary>
        internal static Rect OnScreen(CanvasScaler scaler, RectTransform rect, Vector2 screen, bool touch)
        {
            float factor = Factor(scaler, screen, touch);
            bool portrait = ScreenShape.IsPortraitSize(screen.x, screen.y);
            Rect inCanvas = InCanvas(rect, (RectTransform)scaler.transform, screen / factor, portrait);
            return new Rect(inCanvas.position * factor, inCanvas.size * factor);
        }

        /// <summary>
        /// rect の、Canvas の単位での四角（左下が原点）。親をたどり、縦長の画面では PortraitPosition の置き方にする。
        /// 親の倍率は子に掛けないので、倍率を変えた枠の中の枠には使わない。
        /// </summary>
        internal static Rect InCanvas(RectTransform rect, RectTransform canvas, Vector2 canvasSize, bool portrait)
        {
            if (rect == canvas)
            {
                return new Rect(Vector2.zero, canvasSize);
            }

            Rect parent = InCanvas((RectTransform)rect.parent, canvas, canvasSize, portrait);
            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;
            Vector2 position = rect.anchoredPosition;
            if (portrait && rect.TryGetComponent(out PortraitPosition moved))
            {
                var serialized = new SerializedObject(moved);
                anchorMin = serialized.FindProperty("_portraitAnchor").vector2Value;
                anchorMax = anchorMin;
                position = serialized.FindProperty("_portraitPosition").vector2Value;
            }

            return Place(parent, anchorMin, anchorMax, rect.pivot, position, rect.sizeDelta, rect.localScale.x);
        }

        /// <summary>RectTransform と同じ置き方。ピボットを中心に scale 倍する。</summary>
        internal static Rect Place(Rect parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
            Vector2 sizeDelta, float scale)
        {
            Vector2 low = parent.min + Vector2.Scale(parent.size, anchorMin);
            Vector2 high = parent.min + Vector2.Scale(parent.size, anchorMax);
            Vector2 size = (high - low + sizeDelta) * scale;
            Vector2 at = low + Vector2.Scale(high - low, pivot) + position;
            return new Rect(at - Vector2.Scale(pivot, size), size);
        }

        /// <summary>rect が screen の画面の中に収まる。</summary>
        internal static void AssertInside(Vector2 screen, Rect rect, string message)
        {
            const float slack = 0.01f;
            Assert.IsTrue(rect.xMin >= -slack && rect.yMin >= -slack && rect.xMax <= screen.x + slack &&
                          rect.yMax <= screen.y + slack, message + ": " + rect);
        }
    }
}
