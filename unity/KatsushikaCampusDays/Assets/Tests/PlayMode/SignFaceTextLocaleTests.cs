using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 看板の板に貼った名前（SignFaceText）が、遊んでいる途中で言語を切り替えると貼り直されること (#181)。
    /// OnEnable で L.LocaleChanged を購読するので、AddComponent で OnEnable まで走る PlayMode で見る。
    /// </summary>
    public sealed class SignFaceTextLocaleTests
    {
        [Test]
        public void 看板の板の名前は言語を切り替えると貼り直される()
        {
            bool hadKey = PlayerPrefs.HasKey(L.PrefKey);
            string savedPref = hadKey ? PlayerPrefs.GetString(L.PrefKey) : null;
            string before = L.Locale;
            var host = new GameObject("SignFaceTextLocaleTests");
            try
            {
                SwitchTo("ja");
                TextMeshPro text = host.AddComponent<TextMeshPro>();
                SignFaceText face = host.AddComponent<SignFaceText>();
                face.Bind("ui.building.library", "図書館");
                Assert.AreEqual("図書館", text.text);

                SwitchTo("en");
                Assert.AreEqual("Library", text.text, "英語に切り替えても看板の板の名前が貼り直されない");

                SwitchTo("ja");
                Assert.AreEqual("図書館", text.text, "日本語に戻しても看板の板の名前が英語のまま");

                // 無効にしているあいだの切り替えは、有効に戻したときに追いつく。
                face.enabled = false;
                SwitchTo("en");
                face.enabled = true;
                Assert.AreEqual("Library", text.text, "無効のあいだに切り替えた言語が、有効に戻しても反映されない");
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
