using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>Assets/Data 配下の実データが壊れていないことを検証する。</summary>
    public sealed class DataFilesTests
    {
        private static string DataRoot => Path.Combine(Application.dataPath, "Data");

        private static Dictionary<string, object> ReadObject(string path)
        {
            var node = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(node, path + " はオブジェクトとして読めない");
            return node;
        }

        [Test]
        public void EveryJsonUnderDataParses()
        {
            string[] files = Directory.GetFiles(DataRoot, "*.json", SearchOption.AllDirectories);

            Assert.Greater(files.Length, 0);
            foreach (string file in files)
            {
                Assert.IsNotNull(MiniJson.Deserialize(File.ReadAllText(file)), file + " を読めない");
            }
        }

        [Test]
        public void Quests_HaveUniqueIdsAndResolvablePrerequisites()
        {
            var quests = new List<QuestData>();
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Quests"), "*.json"))
            {
                QuestData quest = QuestData.FromJson(ReadObject(file));
                Assert.IsNotNull(quest, file + " から QuestData を作れない");
                Assert.AreEqual(Path.GetFileNameWithoutExtension(file), quest.Id, "ファイル名と id が違う");
                Assert.Greater(quest.Steps.Count, 0, quest.Id + " にステップが無い");
                quests.Add(quest);
            }

            var ids = new HashSet<string>();
            foreach (QuestData quest in quests)
            {
                Assert.IsTrue(ids.Add(quest.Id), "id が重複: " + quest.Id);
            }

            foreach (QuestData quest in quests)
            {
                foreach (string prerequisite in quest.Prerequisites)
                {
                    Assert.IsTrue(ids.Contains(prerequisite), quest.Id + " の前提 " + prerequisite + " が存在しない");
                }
            }
        }

        [Test]
        public void Localization_JapaneseAndEnglishShareKeys()
        {
            Dictionary<string, object> ja = ReadObject(Path.Combine(DataRoot, "Localization", "ja.json"));
            Dictionary<string, object> en = ReadObject(Path.Combine(DataRoot, "Localization", "en.json"));

            var jaKeys = new List<string>();
            var enKeys = new List<string>();
            CollectKeys(ja, string.Empty, jaKeys);
            CollectKeys(en, string.Empty, enKeys);

            CollectionAssert.AreEquivalent(jaKeys, enKeys);
            Assert.Greater(jaKeys.Count, 0);
        }

        private static void CollectKeys(Dictionary<string, object> node, string prefix, List<string> into)
        {
            foreach (KeyValuePair<string, object> pair in node)
            {
                string path = prefix + pair.Key;
                if (pair.Value is Dictionary<string, object> child)
                {
                    CollectKeys(child, path + ".", into);
                }
                else
                {
                    into.Add(path);
                }
            }
        }

        [Test]
        public void Dialogue_EveryLineHasBothLanguages()
        {
            foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "Dialogue"), "*.json"))
            {
                AssertBilingual(MiniJson.Deserialize(File.ReadAllText(file)), file);
            }
        }

        private static void AssertBilingual(object node, string file)
        {
            if (node is Dictionary<string, object> obj)
            {
                if (obj.ContainsKey("text"))
                {
                    Assert.IsTrue(obj.ContainsKey("text_en"), file + " に text_en の無い行がある");
                }

                foreach (object child in obj.Values)
                {
                    AssertBilingual(child, file);
                }
            }
            else if (node is List<object> list)
            {
                foreach (object child in list)
                {
                    AssertBilingual(child, file);
                }
            }
        }
    }
}
