using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>展示モードの遊ばれ方の記録 (#466)。</summary>
    public sealed class ExhibitLogTests
    {
        private string _folder;

        private string LogPath => Path.Combine(_folder, ExhibitLog.FileName);

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "kcd-exhibitlog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            SaveSystem.DirectoryOverride = _folder;
            ExhibitLog.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ExhibitLog.ResetForTests();
            ExhibitMode.ResetForTests();
            SaveSystem.DirectoryOverride = null;
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private string[] Lines()
        {
            return File.Exists(LogPath) ? File.ReadAllLines(LogPath) : new string[0];
        }

        [Test]
        public void Clock_FormatsGameHours()
        {
            Assert.That(ExhibitLog.Clock(9.5f), Is.EqualTo("09:30"));
            Assert.That(ExhibitLog.Clock(25.25f), Is.EqualTo("01:15"));
            Assert.That(ExhibitLog.Clock(float.NaN), Is.Empty);
        }

        [Test]
        public void Stamp_HasDateTimeAndOffset()
        {
            var t = new DateTimeOffset(2026, 11, 20, 10, 5, 7, TimeSpan.FromHours(9));
            Assert.That(ExhibitLog.Stamp(t), Is.EqualTo("2026-11-20T10:05:07+09:00"));
        }

        [Test]
        public void FormatLine_IsOneLineJson()
        {
            string line = ExhibitLog.FormatLine(new ExhibitLog.Entry { evt = "end", reason = "idle\nx" });
            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(JsonUtility.FromJson<ExhibitLog.Entry>(line).reason, Is.EqualTo("idle\nx"));
        }

        [Test]
        public void NotExhibit_WritesNothing()
        {
            ExhibitMode.Enabled = false;
            ExhibitLog.StartSession("mirai");
            ExhibitLog.EndSession("idle");
            Assert.That(File.Exists(LogPath), Is.False, "展示モードでないのに記録を書いた");
        }

        [Test]
        public void WriteFailure_DoesNotStopTheGame()
        {
            ExhibitMode.Enabled = true;
            SaveSystem.DirectoryOverride = Path.Combine(_folder, "missing");
            LogAssert.Expect(LogType.Warning, new Regex(@"exhibit log write failed: DirectoryNotFound$"));
            LogAssert.Expect(LogType.Warning, new Regex(@"exhibit log write failed: DirectoryNotFound$"));
            Assert.DoesNotThrow(() => ExhibitLog.StartSession("mirai"), "記録を書けないとキャンパスへ入れない");
            Assert.DoesNotThrow(() => ExhibitLog.EndSession("idle"), "記録を書けないとタイトルへ戻れない");
            Assert.That(ExhibitLog.InSession, Is.False, "書けなかったときに遊んでいる人が残った");
        }

        [Test]
        public void Kind_DropsTheExceptionSuffix()
        {
            Assert.That(ExhibitLog.Kind(new DirectoryNotFoundException()), Is.EqualTo("DirectoryNotFound"));
            Assert.That(ExhibitLog.Kind(new Exception()), Is.EqualTo("Exception"));
            Assert.That(ExhibitLog.Kind(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Session_WritesStartAndEndOnce()
        {
            ExhibitMode.Enabled = true;
            ExhibitLog.NoteSaveReset(true);
            ExhibitLog.StartSession("mirai");
            Assert.That(ExhibitLog.InSession, Is.True);

            ExhibitLog.EndSession("idle");
            ExhibitLog.EndSession("title");

            string[] lines = Lines();
            Assert.That(lines.Length, Is.EqualTo(2), "終わりを二重に書いた");
            var start = JsonUtility.FromJson<ExhibitLog.Entry>(lines[0]);
            var end = JsonUtility.FromJson<ExhibitLog.Entry>(lines[1]);
            Assert.That(start.evt, Is.EqualTo("start"));
            Assert.That(start.character, Is.EqualTo("mirai"));
            Assert.That(start.saveReset, Is.True);
            Assert.That(end.evt, Is.EqualTo("end"));
            Assert.That(end.reason, Is.EqualTo("idle"));
            Assert.That(end.session, Is.EqualTo(start.session), "始まりと終わりで別の人になっている");
        }

        [Test]
        public void NewSession_ClosesThePreviousOne()
        {
            ExhibitMode.Enabled = true;
            ExhibitLog.StartSession("mirai");
            ExhibitLog.StartSession("botchan");

            string[] lines = Lines();
            Assert.That(lines.Length, Is.EqualTo(3));
            var end = JsonUtility.FromJson<ExhibitLog.Entry>(lines[1]);
            var second = JsonUtility.FromJson<ExhibitLog.Entry>(lines[2]);
            Assert.That(end.reason, Is.EqualTo("new_game"));
            Assert.That(second.saveReset, Is.False, "セーブを消していないのに消したと書いた");
            Assert.That(second.session, Is.Not.EqualTo(end.session));
        }
    }
}
