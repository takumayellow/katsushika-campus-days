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
    /// 見たエンドの記録 (#41) の書く・読むの端。セーブの 1 枠とは別のファイル kcd_endings.json に置き、
    /// 無い・壊れている・知らない版のどれでも例外を出さず、空として扱うこと。
    /// 置き場は <see cref="SaveSystem.DirectoryOverride"/> に従うので、テストは一時フォルダに向けて、
    /// この端末で遊んでいる記録（persistentDataPath/kcd_endings.json）に触らない。
    /// </summary>
    public sealed class EndingProgressTests
    {
        private string _folder;

        private string FilePath => Path.Combine(_folder, EndingProgress.FileName);

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
            if (!Directory.Exists(_folder))
            {
                return;
            }

            // 読み取り専用にしたファイルは、そのままだと消せない。
            foreach (string file in Directory.GetFiles(_folder))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_folder, true);
        }

        [Test]
        public void NoFile_MeansNothingSeen()
        {
            Assert.IsEmpty(EndingProgress.SeenIds);
            Assert.IsFalse(EndingProgress.Has(DormRoute.EndingId));
            Assert.IsFalse(File.Exists(FilePath), "読むだけのはずが書いている");
        }

        [Test]
        public void Note_RecordsAnEndingOnlyOnce()
        {
            int before = EndingProgress.Version;

            Assert.IsTrue(EndingProgress.Note(DormRoute.EndingId));
            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId));
            Assert.AreEqual(before + 1, EndingProgress.Version);

            int after = EndingProgress.Version;
            Assert.IsFalse(EndingProgress.Note(DormRoute.EndingId), "同じエンドを 2 回数えた");
            Assert.AreEqual(after, EndingProgress.Version, "同じエンドで数え直しの印が進んだ");
            Assert.AreEqual(1, EndingProgress.SeenIds.Count);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Note_IgnoresAMissingId(string endingId)
        {
            int before = EndingProgress.Version;

            Assert.IsFalse(EndingProgress.Note(endingId));
            Assert.IsFalse(EndingProgress.Has(endingId));
            Assert.AreEqual(before, EndingProgress.Version);
            Assert.IsEmpty(EndingProgress.SeenIds);
            Assert.IsFalse(File.Exists(FilePath), "空の id でファイルができた");
        }

        [Test]
        public void Note_WritesTheFileInTheAgreedShape()
        {
            EndingProgress.Note(DormRoute.EndingId);

            Assert.AreEqual("{\"version\":1,\"seen\":[\"end_skip_school\"]}", File.ReadAllText(FilePath));
        }

        [Test]
        public void Reload_ReadsWhatIsInTheFile()
        {
            File.WriteAllText(FilePath, "{\"version\":1,\"seen\":[\"end_skip_school\",\"end_other\"]}");

            EndingProgress.Reload();

            Assert.IsTrue(EndingProgress.Has("end_skip_school"));
            Assert.IsTrue(EndingProgress.Has("end_other"));
            Assert.AreEqual(2, EndingProgress.SeenIds.Count);
        }

        [Test]
        public void TheFileIsReadOnceUntilTheFolderChanges()
        {
            // GameManager は称号を数え直すか決めるのに毎フレーム Version を見る。そのたびにファイルは読まない。
            EndingProgress.Note(DormRoute.EndingId);
            File.WriteAllText(FilePath, "{\"version\":1,\"seen\":[]}");

            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "毎回ファイルを読み直している");
        }

        [Test]
        public void ChangingTheFolder_ReadsThatFoldersRecord()
        {
            EndingProgress.Note(DormRoute.EndingId);

            string other = Path.Combine(_folder, "other");
            Directory.CreateDirectory(other);
            SaveSystem.DirectoryOverride = other;
            Assert.IsFalse(EndingProgress.Has(DormRoute.EndingId), "別の置き場なのに前の置き場の記録が残っている");

            SaveSystem.DirectoryOverride = _folder;
            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "元の置き場に戻したのに記録を読み直さない");
        }

        [TestCase("{")]
        [TestCase("this is not json")]
        [TestCase("{\"version\":1,\"seen\":")]
        [TestCase("{\"version\":2,\"seen\":[\"end_skip_school\"]}")]
        public void BrokenFile_IsTreatedAsNothingSeen(string content)
        {
            File.WriteAllText(FilePath, content);
            LogAssert.Expect(LogType.Warning, new Regex("見たエンドの記録の形式が壊れている"));

            Assert.DoesNotThrow(EndingProgress.Reload);

            Assert.IsEmpty(EndingProgress.SeenIds);
            Assert.IsFalse(EndingProgress.Has(DormRoute.EndingId), "壊れた記録を見たことにしている");
        }

        [Test]
        public void Note_AfterABrokenFile_StartsTheRecordAgain()
        {
            File.WriteAllText(FilePath, "this is not json");
            LogAssert.Expect(LogType.Warning, new Regex("見たエンドの記録の形式が壊れている"));

            Assert.IsTrue(EndingProgress.Note(DormRoute.EndingId));

            Assert.AreEqual("{\"version\":1,\"seen\":[\"end_skip_school\"]}", File.ReadAllText(FilePath));
        }

        [Test]
        public void WriteFailure_KeepsTheEndingInMemoryAndDoesNotThrow()
        {
            // 書けない端末でも遊びは止めない。このプレイの間は称号が取れる。
            SaveSystem.DirectoryOverride = Path.Combine(_folder, "missing");
            LogAssert.Expect(LogType.Error, new Regex("見たエンドを記録できませんでした"));

            bool added = false;
            Assert.DoesNotThrow(() => added = EndingProgress.Note(DormRoute.EndingId));

            Assert.IsTrue(added);
            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "書けなかったとき、このプレイの記録まで失った");
            Assert.IsFalse(File.Exists(Path.Combine(_folder, "missing", EndingProgress.FileName)));
        }

        [Test]
        public void WriteToAReadOnlyFile_KeepsTheEndingInMemoryAndDoesNotThrow()
        {
            const string before = "{\"version\":1,\"seen\":[]}";
            File.WriteAllText(FilePath, before);
            File.SetAttributes(FilePath, FileAttributes.ReadOnly);
            LogAssert.Expect(LogType.Error, new Regex("見たエンドの記録先に書き込めません"));

            bool added = false;
            Assert.DoesNotThrow(() => added = EndingProgress.Note(DormRoute.EndingId));

            Assert.IsTrue(added);
            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "書けなかったとき、このプレイの記録まで失った");
            Assert.AreEqual(before, File.ReadAllText(FilePath), "読み取り専用のファイルが書き換わった");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("this is not json")]
        [TestCase("[\"end_skip_school\"]")]
        [TestCase("{\"seen\":[\"end_skip_school\"]}")]
        [TestCase("{\"version\":0,\"seen\":[\"end_skip_school\"]}")]
        [TestCase("{\"version\":2,\"seen\":[\"end_skip_school\"]}")]
        public void Parse_UnreadableOrUnknownVersion_IsNull(string json)
        {
            Assert.IsNull(EndingProgress.Parse(json));
        }

        [Test]
        public void Parse_MissingSeen_IsEmpty()
        {
            List<string> ids = EndingProgress.Parse("{\"version\":1}");

            Assert.IsNotNull(ids);
            Assert.IsEmpty(ids);
        }

        [Test]
        public void Parse_DropsEmptyIdsAndDuplicatesAndKeepsTheOrder()
        {
            List<string> ids = EndingProgress.Parse("{\"version\":1,\"seen\":[\"b\",\"\",\"a\",\"b\",\"A\"]}");

            CollectionAssert.AreEqual(new[] { "b", "a", "A" }, ids);
        }

        [Test]
        public void ToJson_SortsTheIdsAndReadsBack()
        {
            string json = EndingProgress.ToJson(new[] { "b", "a" });

            Assert.AreEqual("{\"version\":1,\"seen\":[\"a\",\"b\"]}", json);
            CollectionAssert.AreEqual(new[] { "a", "b" }, EndingProgress.Parse(json));
        }

        [Test]
        public void TheRecordSitsBesideTheSaveUnderItsOwnName()
        {
            Assert.AreEqual(Path.Combine(_folder, "kcd_endings.json"), SaveSystem.PathOf(EndingProgress.FileName));
            Assert.AreNotEqual(SaveSystem.FileName, EndingProgress.FileName, "セーブの 1 枠と同じファイルに書くことになる");

            // Web 版は PlayerPrefs のこのキーに入る。ファイル名を変えると、記録が読めなくなる。
            Assert.AreEqual("kcd.save.kcd_endings.json", SaveStore.Key(SaveSystem.PathOf(EndingProgress.FileName)));
        }
    }
}
