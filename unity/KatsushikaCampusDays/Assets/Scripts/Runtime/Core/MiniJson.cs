using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KCD
{
    /// <summary>
    /// 依存の無い最小 JSON パーサ。
    /// JsonUtility は [[x, z], ...] のような入れ子配列を扱えないため、campus.json や
    /// クエスト/会話データの読み込みはこちらを使う。エディタ側からも参照する。
    /// </summary>
    public static partial class MiniJson
    {
        /// <summary>JSON 文字列を Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null に変換する。</summary>
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            return value;
        }

        /// <summary>辞書から文字列を取り出す。欠けていれば fallback。</summary>
        public static string GetString(Dictionary<string, object> node, string key, string fallback = "")
        {
            if (node != null && node.TryGetValue(key, out object value) && value != null)
            {
                return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        /// <summary>辞書から数値を取り出す。欠けていれば fallback。</summary>
        public static float GetFloat(Dictionary<string, object> node, string key, float fallback = 0f)
        {
            if (node != null && node.TryGetValue(key, out object value) && value is double number)
            {
                return (float)number;
            }

            return fallback;
        }

        /// <summary>辞書から整数を取り出す。欠けていれば fallback。</summary>
        public static int GetInt(Dictionary<string, object> node, string key, int fallback = 0)
        {
            if (node != null && node.TryGetValue(key, out object value) && value is double number)
            {
                return (int)Math.Round(number);
            }

            return fallback;
        }

        /// <summary>辞書から真偽値を取り出す。欠けていれば fallback。</summary>
        public static bool GetBool(Dictionary<string, object> node, string key, bool fallback = false)
        {
            if (node != null && node.TryGetValue(key, out object value) && value is bool flag)
            {
                return flag;
            }

            return fallback;
        }

        /// <summary>辞書から入れ子の辞書を取り出す。欠けていれば null。</summary>
        public static Dictionary<string, object> GetObject(Dictionary<string, object> node, string key)
        {
            if (node != null && node.TryGetValue(key, out object value))
            {
                return value as Dictionary<string, object>;
            }

            return null;
        }

        /// <summary>辞書から配列を取り出す。欠けていれば空リスト。</summary>
        public static List<object> GetArray(Dictionary<string, object> node, string key)
        {
            if (node != null && node.TryGetValue(key, out object value) && value is List<object> list)
            {
                return list;
            }

            return new List<object>();
        }

        /// <summary>配列から文字列リストを作る。要素が文字列でなければ読み飛ばす。</summary>
        public static List<string> ToStringList(List<object> array)
        {
            var result = new List<string>();
            if (array == null)
            {
                return result;
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is string text)
                {
                    result.Add(text);
                }
            }

            return result;
        }
    }
}
