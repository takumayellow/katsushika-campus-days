using System;
using System.IO;
using NUnit.Framework;

namespace KCD.Tests
{
    /// <summary>
    /// セーブ文字列の置き場（SaveStore）。エディタとデスクトップはファイル、Web 版は PlayerPrefs に置く。
    /// ここで試すのはエディタで通るファイルの道と、Web 版のキーの作り方。
    /// 一時フォルダだけを使い、その端末で遊んでいるセーブ（persistentDataPath）には触らない。
    /// </summary>
    public sealed class SaveStoreTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "kcd-editmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void Key_UsesOnlyTheFileName()
        {
            // Web 版は persistentDataPath がページごとに変わりうるので、フォルダを含めるとセーブを見失う。
            string a = SaveStore.Key(Path.Combine(_folder, SaveSystem.FileName));
            string b = SaveStore.Key(Path.Combine(Path.Combine(_folder, "other"), SaveSystem.FileName));

            Assert.AreEqual("kcd.save." + SaveSystem.FileName, a);
            Assert.AreEqual(a, b, "フォルダが違うと別のキーになっている");
        }

        [Test]
        public void Exists_MissingFile_IsFalse()
        {
            Assert.IsFalse(SaveStore.Exists(Path.Combine(_folder, SaveSystem.FileName)));
        }

        [Test]
        public void WriteThenRead_ReturnsTheSameText()
        {
            string path = Path.Combine(_folder, SaveSystem.FileName);
            const string text = "{\"CharacterId\":\"madonna\",\"DayNumber\":4}";

            SaveStore.Write(path, text);

            Assert.IsTrue(SaveStore.Exists(path), "書いたのに無いことになっている");
            Assert.AreEqual(text, SaveStore.Read(path));
        }

        [Test]
        public void Write_ReplacesTheOldTextInsteadOfAppending()
        {
            string path = Path.Combine(_folder, SaveSystem.FileName);

            SaveStore.Write(path, "{\"DayNumber\":1,\"CharacterId\":\"mirai\",\"padding\":\"long old text\"}");
            SaveStore.Write(path, "{\"DayNumber\":2}");

            Assert.AreEqual("{\"DayNumber\":2}", SaveStore.Read(path), "前のセーブの残りが混ざっている");
        }

        [Test]
        public void Write_KeepsJapaneseText()
        {
            string path = Path.Combine(_folder, SaveSystem.FileName);
            const string text = "{\"InteriorId\":\"図書館\"}";

            SaveStore.Write(path, text);

            Assert.AreEqual(text, SaveStore.Read(path), "日本語が化けている");
        }
    }
}
