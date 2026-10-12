using System;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 開発者用の機能（ワープメニューなど）を出してよいか。Unity エディタ、Development Build、
    /// またはブラウザの URL のクエリに dev=1 があるときだけ。公開版の通常の URL では常に false。
    /// </summary>
    public static class DevMode
    {
        /// <summary>URL のクエリで開発者用を有効にする名前と値（<c>?dev=1</c>）。</summary>
        public const string QueryKey = "dev";

        public const string QueryValue = "1";

        private static bool? _enabled;

        /// <summary>この実行で開発者用の機能を出してよいか。最初に問い合わせたときの条件で決まる。</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = IsEnabled(Application.isEditor, Debug.isDebugBuild, Application.absoluteURL);
                }

                return _enabled.Value;
            }
        }

        /// <summary>条件の判定。エディタか Development Build なら true、それ以外は URL のクエリで決める。</summary>
        public static bool IsEnabled(bool isEditor, bool isDevelopmentBuild, string absoluteUrl)
        {
            return isEditor || isDevelopmentBuild || HasDevQuery(absoluteUrl);
        }

        /// <summary>
        /// URL のクエリに dev=1 があるか。fragment（#...）は見ない。値が 1 と完全に一致するものだけを受け、
        /// dev=10・xdev=1・dev= は受けない。名前と値の大文字小文字は区別する。
        /// </summary>
        public static bool HasDevQuery(string absoluteUrl)
        {
            if (string.IsNullOrEmpty(absoluteUrl))
            {
                return false;
            }

            int fragment = absoluteUrl.IndexOf('#');
            string withoutFragment = fragment >= 0 ? absoluteUrl.Substring(0, fragment) : absoluteUrl;
            int question = withoutFragment.IndexOf('?');
            if (question < 0)
            {
                return false;
            }

            string[] pairs = withoutFragment.Substring(question + 1).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int equals = pairs[i].IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string key = pairs[i].Substring(0, equals);
                string value = pairs[i].Substring(equals + 1);
                if (string.Equals(key, QueryKey, StringComparison.Ordinal)
                    && string.Equals(value, QueryValue, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
