using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KCD.Tests
{
    /// <summary>
    /// シーンの Canvas を、ある大きさの画面に置いたときの倍率 (#187)。テストでは画面の大きさを選べず、シーンに保存された
    /// Canvas の大きさも 0 なので、画面の大きさごとの置き場所を見るテストは、ここで倍率を求めて Canvas の大きさを出す。
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
    }
}
