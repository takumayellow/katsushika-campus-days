using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 会話の文字送りの音（DialogueVoice）。「自分」と空の話者は操作キャラの声、先生は教授の声、
    /// 知らない話者も毎回同じ声になること、選ぶ音がすべて Assets/Audio に実在すること。
    /// </summary>
    public sealed class DialogueVoiceTests
    {
        private static readonly string[] AllVoices = { "talk_blip_f1", "talk_blip_f2", "talk_blip_m1", "talk_blip_prof" };

        [TestCase("mirai", "talk_blip_f1")]
        [TestCase("botchan", "talk_blip_m1")]
        [TestCase("madonna", "talk_blip_f2")]
        [TestCase("no_such_character", "talk_blip_f1")]
        [TestCase(null, "talk_blip_f1")]
        public void ForCharacter_PicksTheVoiceOfThePlayableCharacter(string characterId, string expected)
        {
            Assert.AreEqual(expected, DialogueVoice.ForCharacter(characterId));
        }

        [Test]
        public void ForCharacter_EveryPlayableCharacterHasAVoice()
        {
            foreach (string id in GameManager.PlayableCharacterIds)
            {
                CollectionAssert.Contains(AllVoices, DialogueVoice.ForCharacter(id), id + " の声が無い");
            }
        }

        [TestCase("自分", "botchan", "talk_blip_m1")]
        [TestCase("", "madonna", "talk_blip_f2")]
        [TestCase(null, "mirai", "talk_blip_f1")]
        public void ForSpeaker_MeIsThePlayerCharacter(string speaker, string playerId, string expected)
        {
            Assert.AreEqual(expected, DialogueVoice.ForSpeaker(speaker, playerId));
        }

        [TestCase("教授", "talk_blip_prof")]
        [TestCase("数学の先生", "talk_blip_prof")]
        [TestCase("prof. Sato", "talk_blip_prof")]
        [TestCase("花之木 いなり", "talk_blip_f1")]
        [TestCase("金町 そら", "talk_blip_f1")]
        [TestCase("中川 かなめ", "talk_blip_f2")]
        public void ForSpeaker_KnownSpeakers(string speaker, string expected)
        {
            Assert.AreEqual(expected, DialogueVoice.ForSpeaker(speaker, "botchan"),
                "「" + speaker + "」の声が違う（操作キャラの声に引きずられていないか）");
        }

        [TestCase("寮長", "talk_blip_f1")]
        [TestCase("売店の人", "talk_blip_f1")]
        [TestCase("守衛", "talk_blip_f1")]
        [TestCase("通りすがり", "talk_blip_f2")]
        public void ForSpeaker_UnknownSpeakers_AreSplitByNameLength(string speaker, string expected)
        {
            Assert.AreEqual(expected, DialogueVoice.ForSpeaker(speaker, "botchan"));
            Assert.AreEqual(DialogueVoice.ForSpeaker(speaker, "mirai"), DialogueVoice.ForSpeaker(speaker, "madonna"),
                "知らない話者の声が操作キャラで変わる");
        }

        [Test]
        public void EveryVoiceHasASoundFile()
        {
            // AudioManager には AudioFactory が Assets/Audio 以下の全クリップを名前で差し込む。形式と置き場所は問わない。
            string folder = Path.Combine(Application.dataPath, "Audio");
            foreach (string voice in AllVoices)
            {
                string[] files = Directory.GetFiles(folder, voice + ".*", SearchOption.AllDirectories);
                bool found = false;
                foreach (string file in files)
                {
                    if (!file.EndsWith(".meta") && Path.GetFileNameWithoutExtension(file) == voice)
                    {
                        found = true;
                    }
                }

                Assert.IsTrue(found, "Assets/Audio に " + voice + " の音が無い（文字送りが無音になる）");
            }
        }
    }
}
