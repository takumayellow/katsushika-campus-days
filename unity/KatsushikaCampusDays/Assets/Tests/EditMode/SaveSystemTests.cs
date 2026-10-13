using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// SaveSystem の書く・読むの端。書けない・壊れている・無いのどれでも例外を出さず、
    /// 「セーブ無し」か false に倒れること。タイトルの「つづきから」は Read() が null かどうかで出し分けるので、
    /// 壊れたファイルを null にし損ねると、選んでも始まらない項目が残る。
    /// GameManager.Instance を通る道（Save / Capture / 読めた後の PrepareContinue）は EditMode では通さない。
    /// </summary>
    public sealed class SaveSystemTests
    {
        private string _folder;

        private string SavePath => Path.Combine(_folder, SaveSystem.FileName);

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "kcd-editmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            SaveSystem.DirectoryOverride = _folder;
            SaveSystem.DiscardPending();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.DirectoryOverride = null;
            SaveSystem.DiscardPending();
            if (!Directory.Exists(_folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(_folder))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_folder, true);
        }

        [Test]
        public void Write_Null_ReturnsFalseAndWritesNothing()
        {
            Assert.IsFalse(SaveSystem.Write(null));
            Assert.IsFalse(SaveSystem.HasSave, "null を渡したのにファイルができている");
        }

        [Test]
        public void Write_ThenRead_RoundTrips()
        {
            var data = new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                CharacterId = "madonna",
                TimeHours = 14.25f,
                DayNumber = 2,
                Buildings = new List<string> { "gym", "library" }
            };

            Assert.IsTrue(SaveSystem.Write(data), "書けなかった");
            Assert.IsTrue(SaveSystem.HasSave, "書いたのにセーブが無いことになっている");

            SaveData read = SaveSystem.Read();
            Assert.IsNotNull(read, "書いたセーブを読めない");
            Assert.AreEqual("madonna", read.CharacterId);
            Assert.AreEqual(14.25f, read.TimeHours, 0.0001f);
            Assert.AreEqual(2, read.DayNumber);
            CollectionAssert.AreEqual(new[] { "gym", "library" }, read.Buildings);
        }

        [Test]
        public void Write_Twice_KeepsOnlyTheLatest()
        {
            SaveSystem.Write(new SaveData { SaveVersion = SaveData.CurrentVersion, DayNumber = 1 });
            SaveSystem.Write(new SaveData { SaveVersion = SaveData.CurrentVersion, DayNumber = 3 });

            SaveData read = SaveSystem.Read();
            Assert.IsNotNull(read);
            Assert.AreEqual(3, read.DayNumber, "後から書いた日数になっていない");
        }

        [Test]
        public void Write_FolderMissing_ReturnsFalseWithoutThrowing()
        {
            // 自動セーブは一日の終わりや建物の出入りのたびに走る。書けない端末でも遊びは止めない。
            SaveSystem.DirectoryOverride = Path.Combine(_folder, "missing");
            LogAssert.Expect(LogType.Error, new Regex("セーブに失敗しました"));

            bool written = true;
            Assert.DoesNotThrow(() => written = SaveSystem.Write(new SaveData()));
            Assert.IsFalse(written);
        }

        [Test]
        public void Write_ReadOnlyFile_ReturnsFalseWithoutThrowing()
        {
            File.WriteAllText(SavePath, SaveDataRules.ToJson(new SaveData { DayNumber = 1 }));
            File.SetAttributes(SavePath, FileAttributes.ReadOnly);
            LogAssert.Expect(LogType.Error, new Regex("セーブ"));

            bool written = true;
            Assert.DoesNotThrow(() => written = SaveSystem.Write(new SaveData { DayNumber = 5 }));
            Assert.IsFalse(written, "読み取り専用のファイルに書けたことになっている");
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("this is not json")]
        [TestCase("[1,2,3]")]
        public void Read_BrokenFile_IsTreatedAsNoSave(string content)
        {
            File.WriteAllText(SavePath, content);
            LogAssert.Expect(LogType.Warning, new Regex("セーブの形式が壊れている"));

            SaveData read = null;
            Assert.DoesNotThrow(() => read = SaveSystem.Read());
            Assert.IsNull(read, "壊れたセーブを読めたことにしている（タイトルに始まらない「つづきから」が出る）");

            // HasSave はファイルの有無しか見ない。「つづきから」を出すかは Read() で決める（TitleMenu）。
            Assert.IsTrue(SaveSystem.HasSave);
        }

        [Test]
        public void PrepareContinue_WithoutASave_ReturnsFalseAndLeavesNothingPending()
        {
            Assert.IsFalse(SaveSystem.PrepareContinue());

            // 保留が無いので、キャンパスの最初のフレームで呼ばれても何も当てない。
            Assert.DoesNotThrow(SaveSystem.ApplyPending);
        }

        [Test]
        public void PrepareContinue_WithABrokenSave_ReturnsFalse()
        {
            File.WriteAllText(SavePath, "{\"CharacterId\":");
            LogAssert.Expect(LogType.Warning, new Regex("セーブの形式が壊れている"));

            Assert.IsFalse(SaveSystem.PrepareContinue(), "壊れたセーブで「つづきから」を始めている");
        }

        [Test]
        public void Load_WithoutASave_ReturnsFalseAndChangesNothing()
        {
            Assert.IsFalse(SaveSystem.Load());
            Assert.IsFalse(SaveSystem.HasSave, "読むだけのはずが書いている");
        }

        [Test]
        public void DiscardPending_ThenApplyPending_DoesNothing()
        {
            SaveSystem.DiscardPending();

            Assert.DoesNotThrow(SaveSystem.ApplyPending);
            Assert.DoesNotThrow(SaveSystem.ApplyPending, "2 回目の ApplyPending も何もしない");
        }
    }
}
