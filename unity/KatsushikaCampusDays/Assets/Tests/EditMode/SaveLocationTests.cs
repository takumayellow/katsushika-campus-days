using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// セーブの置き場を差し替えると、読む・あるかを見る先もそこになる。テストとスモークはこれで、
    /// その端末で遊んでいるセーブ（persistentDataPath/kcd_save.json）に触らずに済む。
    /// </summary>
    public sealed class SaveLocationTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "kcd-editmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            SaveSystem.DirectoryOverride = _folder;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.DirectoryOverride = null;
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void DirectoryOverride_EmptyFolder_HasNoSave()
        {
            Assert.IsFalse(SaveSystem.HasSave, "空のフォルダに差し替えたのにセーブがあることになっている");
            Assert.IsNull(SaveSystem.Read(), "空のフォルダに差し替えたのにセーブが読めた");
        }

        [Test]
        public void DirectoryOverride_ReadsTheSaveInThatFolder()
        {
            var data = new SaveData { DayNumber = 3, CharacterId = "botchan" };
            File.WriteAllText(Path.Combine(_folder, SaveSystem.FileName), SaveDataRules.ToJson(data));

            Assert.IsTrue(SaveSystem.HasSave, "差し替えたフォルダのセーブを見ていない");
            SaveData read = SaveSystem.Read();
            Assert.IsNotNull(read, "差し替えたフォルダのセーブを読めない");
            Assert.AreEqual(3, read.DayNumber);
            Assert.AreEqual("botchan", read.CharacterId);
        }

        [Test]
        public void PathOf_FollowsTheOverrideAndOtherwiseThePersistentDataPath()
        {
            // セーブと同じ置き場の別ファイル（見たエンドの記録）も、差し替えたフォルダに置く。
            Assert.AreEqual(Path.Combine(_folder, "kcd_endings.json"), SaveSystem.PathOf("kcd_endings.json"));

            SaveSystem.DirectoryOverride = null;
            Assert.AreEqual(Path.Combine(Application.persistentDataPath, "kcd_endings.json"),
                SaveSystem.PathOf("kcd_endings.json"));
        }
    }
}
