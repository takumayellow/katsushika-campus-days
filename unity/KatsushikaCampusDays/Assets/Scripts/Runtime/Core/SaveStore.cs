using System.IO;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// セーブ文字列の置き場。デスクトップは persistentDataPath のファイル、
    /// WebGL はファイルシステムが IndexedDB への手動同期を要するので PlayerPrefs に置く
    /// （PlayerPrefs.Save がブラウザ側へ書き切る）。
    /// </summary>
    public static class SaveStore
    {
        /// <summary>保存済みか。</summary>
        public static bool Exists(string path)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return PlayerPrefs.HasKey(Key(path));
#else
            return File.Exists(path);
#endif
        }

        /// <summary>文字列を書く。失敗は呼び出し側で IOException / UnauthorizedAccessException として扱う。</summary>
        public static void Write(string path, string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PlayerPrefs.SetString(Key(path), text);
            PlayerPrefs.Save();
#else
            File.WriteAllText(path, text);
#endif
        }

        /// <summary>文字列を読む。無ければ空文字。</summary>
        public static string Read(string path)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return PlayerPrefs.GetString(Key(path), string.Empty);
#else
            return File.ReadAllText(path);
#endif
        }

        /// <summary>PlayerPrefs のキー。パスの末尾（ファイル名）だけ使う。</summary>
        public static string Key(string path)
        {
            return "kcd.save." + Path.GetFileName(path);
        }
    }
}
