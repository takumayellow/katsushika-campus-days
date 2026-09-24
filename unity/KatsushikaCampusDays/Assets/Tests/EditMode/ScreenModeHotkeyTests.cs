using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// F キーの全画面切り替えの条件 (#48)。
    /// 打鍵を拾ってよいかの判定と、切り替えた先の表示モードだけを見る（実際の Screen 操作は Editor では効かない）。
    /// </summary>
    public sealed class ScreenModeHotkeyTests
    {
        [TearDown]
        public void TearDown()
        {
            ScreenModeHotkey.KeyCaptureActive = false;
        }

        [Test]
        public void ShouldToggle_OnlyWhenPressedAndNothingIsEatingTheKey()
        {
            Assert.IsTrue(ScreenModeHotkey.ShouldToggle(true, false, false));

            Assert.IsFalse(ScreenModeHotkey.ShouldToggle(false, false, false), "押していないのに切り替わった");
            Assert.IsFalse(ScreenModeHotkey.ShouldToggle(true, true, false), "文字入力中に切り替わった");
            Assert.IsFalse(ScreenModeHotkey.ShouldToggle(true, false, true), "キー割り当ての取り込み中に切り替わった");
            Assert.IsFalse(ScreenModeHotkey.ShouldToggle(true, true, true));
        }

        [Test]
        public void NextMode_SwapsBetweenFullscreenAndWindowed()
        {
            Assert.AreEqual(FullScreenMode.Windowed, ScreenModeHotkey.NextMode(true));
            // ビルド設定（PlayerSettings.fullScreenMode）と同じ「枠なし全画面ウィンドウ」に戻る。
            Assert.AreEqual(FullScreenMode.FullScreenWindow, ScreenModeHotkey.NextMode(false));
        }

        [Test]
        public void IsTypingInInputField_IsSafeWithoutAnEventSystem()
        {
            // 常駐部品が毎フレーム呼ぶので、EventSystem を置いていないシーンでも例外を投げないこと。
            Assert.DoesNotThrow(() => ScreenModeHotkey.IsTypingInInputField());
        }
    }
}
