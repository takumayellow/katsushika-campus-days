using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    public sealed class SaveDataTests
    {
        [Test]
        public void SaveData_RoundTripsThroughJsonUtility()
        {
            var data = new SaveData
            {
                CharacterId = "madonna",
                TimeHours = 17.25f,
                PlayerX = 1.5f,
                PlayerY = -2f,
                PlayerZ = 30.75f,
                PlayerYaw = 271f
            };
            data.Quests.QuestIds.Add("q_intro");
            data.Quests.StepsDone.Add(2);
            data.Quests.States.Add(1);

            SaveData restored = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));

            Assert.AreEqual("madonna", restored.CharacterId);
            Assert.AreEqual(17.25f, restored.TimeHours, 1e-6f);
            Assert.AreEqual(1.5f, restored.PlayerX, 1e-6f);
            Assert.AreEqual(-2f, restored.PlayerY, 1e-6f);
            Assert.AreEqual(30.75f, restored.PlayerZ, 1e-6f);
            Assert.AreEqual(271f, restored.PlayerYaw, 1e-6f);
            CollectionAssert.AreEqual(new[] { "q_intro" }, restored.Quests.QuestIds);
            CollectionAssert.AreEqual(new[] { 2 }, restored.Quests.StepsDone);
            CollectionAssert.AreEqual(new[] { 1 }, restored.Quests.States);
        }

        [Test]
        public void SaveData_DefaultsMatchNewGame()
        {
            SaveData restored = JsonUtility.FromJson<SaveData>("{}");

            Assert.AreEqual("mirai", restored.CharacterId);
            Assert.AreEqual(8.5f, restored.TimeHours, 1e-6f);
            Assert.IsNotNull(restored.Quests);
            Assert.AreEqual(0, restored.Quests.QuestIds.Count);
        }
    }
}
