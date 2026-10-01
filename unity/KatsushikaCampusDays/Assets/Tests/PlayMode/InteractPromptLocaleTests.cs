using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// ベンチの「座る」と拾い物の「拾う」が、遊んでいる途中で言語を切り替えると出し直されること (#95)。
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
                Assert.AreEqual("座る", seat.PromptLabel);
                Assert.AreEqual("拾う", item.PromptLabel);

                SwitchTo("en");
                Assert.AreEqual("Sit", seat.PromptLabel, "英語に切り替えてもベンチの案内が出し直されない");
                Assert.AreEqual("Pick up", item.PromptLabel, "英語に切り替えても拾い物の案内が出し直されない");

                SwitchTo("ja");
                Assert.AreEqual("座る", seat.PromptLabel, "日本語に戻してもベンチの案内が英語のまま");
                Assert.AreEqual("拾う", item.PromptLabel, "日本語に戻しても拾い物の案内が英語のまま");
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
