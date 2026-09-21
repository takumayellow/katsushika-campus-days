using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 表示文字列の辞書。Assets/Data/Localization/{ja,en}.json（DataBundler が Resources/KCD/Localization へ複製）を読む。
    /// キーが無いときは fallback（省略時はキーそのもの）を返すので、辞書が欠けても画面は成立する。
    /// </summary>
    public static class L
    {
        public const string PrefKey = "KCD.Locale";
        public const string ResourceFolder = "KCD/Localization";
        public const string DefaultLocale = "ja";

        private static Dictionary<string, string> _table;
        private static string _locale;

        /// <summary>言語が切り替わったとき。各ビューはこれを購読して描き直す。</summary>
        public static event Action LocaleChanged;

        /// <summary>現在の言語コード（ja / en）。</summary>
        public static string Locale
        {
            get
            {
                Ensure();
                return _locale;
            }
        }

        public static bool IsEnglish => Locale == "en";

        /// <summary>言語を切り替えて保存する。辞書が無い言語は無視する。</summary>
        public static void SetLocale(string locale)
        {
            Ensure();
            if (locale == _locale || !Load(locale))
            {
                return;
            }

            PlayerPrefs.SetString(PrefKey, locale);
            PlayerPrefs.Save();
            LocaleChanged?.Invoke();
        }

        public static void Toggle()
        {
            SetLocale(IsEnglish ? "ja" : "en");
        }

        /// <summary>キーに対応する文字列。無ければ fallback、それも無ければキー。</summary>
        public static string Get(string key, string fallback = null)
        {
            Ensure();
            if (key != null && _table.TryGetValue(key, out string value))
            {
                return value;
            }

            return fallback ?? key ?? string.Empty;
        }

        /// <summary>キーの文字列を string.Format に通す。書式が壊れていても落とさない。</summary>
        public static string Format(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>データ側の text / text_en のように、日本語と英語を並べて持つ値を選ぶ。</summary>
        public static string Pick(string ja, string en)
        {
            return IsEnglish && !string.IsNullOrEmpty(en) ? en : ja;
        }

        private static void Ensure()
        {
            if (_table != null)
            {
                return;
            }

            string wanted = PlayerPrefs.GetString(PrefKey, DefaultLocale);
            if (!Load(wanted) && !Load(DefaultLocale))
            {
                _table = new Dictionary<string, string>();
                _locale = DefaultLocale;
            }
        }

        private static bool Load(string locale)
        {
            if (string.IsNullOrEmpty(locale))
            {
                return false;
            }

            var asset = Resources.Load<TextAsset>(ResourceFolder + "/" + locale);
            if (asset == null)
            {
                return false;
            }

            var root = MiniJson.Deserialize(asset.text) as Dictionary<string, object>;
            Dictionary<string, object> strings = root != null ? MiniJson.GetObject(root, "strings") : null;
            if (strings == null)
            {
                return false;
            }

            var table = new Dictionary<string, string>(strings.Count);
            foreach (KeyValuePair<string, object> pair in strings)
            {
                table[pair.Key] = pair.Value as string ?? (pair.Value != null ? pair.Value.ToString() : string.Empty);
            }

            _table = table;
            _locale = locale;
            return true;
        }
    }
}
