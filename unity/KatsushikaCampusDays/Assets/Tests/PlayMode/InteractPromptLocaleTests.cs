using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ベンチの「座る」・拾い物の「拾う」・NPC とモブの「話す」が、遊んでいる途中で言語を切り替えると
    /// 出し直されること (#95, #17)。
    /// どちらも OnEnable で L.LocaleChanged を購読するので、AddComponent で OnEnable まで走る PlayMode で見る。
    /// </summary>
    public sealed class InteractPromptLocaleTests
    {
        [Test]
        public void SeatAndPickUpPrompts_FollowALanguageSwitch()
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            var host = new GameObject("InteractPromptLocaleTests");
            try
            {
                SwitchTo("ja");
                SeatInteractable seat = host.AddComponent<SeatInteractable>();
                CollectableItem item = host.AddComponent<CollectableItem>();
                NPCTalker npc = new GameObject("npc", typeof(BoxCollider)).AddComponent<NPCTalker>();
                npc.transform.SetParent(host.transform);
                MobTalker mob = new GameObject("mob", typeof(BoxCollider)).AddComponent<MobTalker>();
                mob.transform.SetParent(host.transform);
                Assert.AreEqual("座る", seat.PromptLabel);
                Assert.AreEqual("拾う", item.PromptLabel);
                Assert.AreEqual("話す", npc.PromptLabel);
                Assert.AreEqual("話す", mob.PromptLabel);

                SwitchTo("en");
                Assert.AreEqual("Sit", seat.PromptLabel, "英語に切り替えてもベンチの案内が出し直されない");
                Assert.AreEqual("Pick up", item.PromptLabel, "英語に切り替えても拾い物の案内が出し直されない");
                Assert.AreEqual("Talk", npc.PromptLabel, "英語に切り替えても NPC の案内が出し直されない");
                Assert.AreEqual("Talk", mob.PromptLabel, "英語に切り替えてもモブの案内が出し直されない");

                SwitchTo("ja");
                Assert.AreEqual("座る", seat.PromptLabel, "日本語に戻してもベンチの案内が英語のまま");
                Assert.AreEqual("拾う", item.PromptLabel, "日本語に戻しても拾い物の案内が英語のまま");
                Assert.AreEqual("話す", npc.PromptLabel, "日本語に戻しても NPC の案内が英語のまま");
                Assert.AreEqual("話す", mob.PromptLabel, "日本語に戻してもモブの案内が英語のまま");
            }
            finally
            {
                Object.DestroyImmediate(host);
                L.SetLocale(before);
                if (hadKey)
                {
                    PlayerPrefs.SetString(L.PrefKey, savedPref);
                }
                else
                {
                    PlayerPrefs.DeleteKey(L.PrefKey);
                }

                PlayerPrefs.Save();
            }
        }

        private static void SwitchTo(string locale)
        {
            L.SetLocale(locale);
            Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
        }
    }
}
