using System.IO;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>展示モード (#466) の判定。</summary>
    public sealed class ExhibitModeTests
    {
        [TearDown]
        public void TearDown()
        {
            ExhibitMode.ResetForTests();
        }

        [Test]
        public void IsEnabled_MarkerFile_TurnsOn()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe" }, true), Is.True);
        }

        [Test]
        public void IsEnabled_Argument_TurnsOn_IgnoringCase()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe", "-KCD-Exhibit" }, false), Is.True);
        }

        [Test]
        public void IsEnabled_NeitherMarkerNorArgument_StaysOff()
        {
            Assert.That(ExhibitMode.IsEnabled(new[] { "game.exe", "-kcd-scene", "Campus" }, false), Is.False);
            Assert.That(ExhibitMode.IsEnabled(null, false), Is.False);
        }

        [Test]
        public void MarkerPath_IsNextToExe()
        {
            string data = Path.Combine("C:" + Path.DirectorySeparatorChar + "kcd", "KatsushikaCampusDays_Data");
            string expected = Path.Combine("C:" + Path.DirectorySeparatorChar + "kcd", ExhibitMode.MarkerFileName);
            Assert.That(ExhibitMode.MarkerPath(data, true), Is.EqualTo(expected));
        }

        [Test]
        public void MarkerPath_EditorAndWeb_NotLooked()
        {
            Assert.That(ExhibitMode.MarkerPath("C:/kcd/KatsushikaCampusDays_Data", false), Is.Null);
            Assert.That(ExhibitMode.MarkerPath(string.Empty, true), Is.Null);
        }

        [Test]
        public void OffersContinue_HiddenInExhibit()
        {
            Assert.That(ExhibitMode.OffersContinue(false, true), Is.True);
            Assert.That(ExhibitMode.OffersContinue(true, true), Is.False);
            Assert.That(ExhibitMode.OffersContinue(false, false), Is.False);
        }

        [Test]
        public void Enabled_CanBeOverriddenForTests()
        {
            ExhibitMode.Enabled = true;
            Assert.That(ExhibitMode.Enabled, Is.True);
            ExhibitMode.Enabled = false;
            Assert.That(ExhibitMode.Enabled, Is.False);
        }

        [Test]
        public void Decide_BeforeWarning_DoesNothing()
        {
            float early = ExhibitMode.IdleSeconds - ExhibitMode.WarnBeforeSeconds - 1f;
            Assert.That(ExhibitMode.Decide(early, false, false), Is.EqualTo(ExhibitMode.IdleAction.None));
        }

        [Test]
        public void Decide_WarnsOnceBeforeReturning()
        {
            float warnAt = ExhibitMode.IdleSeconds - ExhibitMode.WarnBeforeSeconds;
            Assert.That(ExhibitMode.Decide(warnAt, false, false), Is.EqualTo(ExhibitMode.IdleAction.Warn));
            Assert.That(ExhibitMode.Decide(warnAt + 1f, true, false), Is.EqualTo(ExhibitMode.IdleAction.None));
        }

        [Test]
        public void Decide_ReturnsAtLimit_EvenWithoutWarning()
        {
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds, true, false),
                Is.EqualTo(ExhibitMode.IdleAction.ReturnToTitle));
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds + 30f, false, false),
                Is.EqualTo(ExhibitMode.IdleAction.ReturnToTitle));
        }

        [Test]
        public void Decide_WaitsWhileLoading()
        {
            Assert.That(ExhibitMode.Decide(ExhibitMode.IdleSeconds + 30f, false, true),
                Is.EqualTo(ExhibitMode.IdleAction.None));
        }
    }
}
