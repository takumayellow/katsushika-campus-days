using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// Windows 版のフレームの刻み (#15)。vSync を入れ, 切ったときは 60 fps で止める。
    /// Web 版とエディタは触らない（ブラウザは画面に合わせて回し, エディタは ProjectSettings に残るため）。
    /// </summary>
    public sealed class FramePacingTests
    {
        [TestCase(RuntimePlatform.WindowsPlayer)]
        [TestCase(RuntimePlatform.OSXPlayer)]
        [TestCase(RuntimePlatform.LinuxPlayer)]
        public void Plan_Desktop_UsesVSyncWithoutAFrameCap(RuntimePlatform platform)
        {
            FramePacingPlan plan = FramePacing.Plan(platform, true);

            Assert.IsTrue(plan.Change);
            Assert.AreEqual(1, plan.VSyncCount, "画面の書き換え 1 回ごとに 1 フレーム");
            Assert.AreEqual(-1, plan.TargetFrameRate, "vSync のときは上限を入れない");
        }

        [TestCase(RuntimePlatform.WindowsPlayer)]
        [TestCase(RuntimePlatform.OSXPlayer)]
        [TestCase(RuntimePlatform.LinuxPlayer)]
        public void Plan_DesktopWithoutVSync_CapsAtSixty(RuntimePlatform platform)
        {
            FramePacingPlan plan = FramePacing.Plan(platform, false);

            Assert.IsTrue(plan.Change);
            Assert.AreEqual(0, plan.VSyncCount);
            Assert.AreEqual(60, plan.TargetFrameRate);
        }

        [TestCase(RuntimePlatform.WebGLPlayer)]
        [TestCase(RuntimePlatform.WindowsEditor)]
        [TestCase(RuntimePlatform.OSXEditor)]
        [TestCase(RuntimePlatform.LinuxEditor)]
        public void Plan_WebAndEditor_ChangeNothing(RuntimePlatform platform)
        {
            Assert.IsFalse(FramePacing.Plan(platform, true).Change, platform + " vSync あり");
            Assert.IsFalse(FramePacing.Plan(platform, false).Change, platform + " vSync なし");
        }

        [Test]
        public void VSyncWanted_IsOnUnlessTurnedOffOnTheCommandLine()
        {
            Assert.IsTrue(FramePacing.VSyncWanted(null));
            Assert.IsTrue(FramePacing.VSyncWanted(new string[0]));
            Assert.IsTrue(FramePacing.VSyncWanted(new[] { "KCD.exe", "-screen-fullscreen", "0" }),
                          "ほかの引数の 0 は読まない");
            Assert.IsTrue(FramePacing.VSyncWanted(new[] { "KCD.exe", "-kcd-vsync", "on" }));
            Assert.IsTrue(FramePacing.VSyncWanted(new[] { "KCD.exe", "-kcd-vsync" }), "値が無ければ既定のまま");
        }

        [TestCase("off")]
        [TestCase("OFF")]
        [TestCase("0")]
        [TestCase("false")]
        public void VSyncWanted_OffValues_TurnItOff(string value)
        {
            Assert.IsFalse(FramePacing.VSyncWanted(new[] { "KCD.exe", "-kcd-vsync", value }), "-kcd-vsync " + value);
            Assert.IsFalse(FramePacing.VSyncWanted(new[] { "KCD.exe", "-kcd-vsync=" + value }), "-kcd-vsync=" + value);
            Assert.IsFalse(FramePacing.VSyncWanted(new[] { "KCD.exe", "-logFile", "x.log", "-KCD-VSYNC", value }),
                           "大文字の引数名");
        }
    }
}
