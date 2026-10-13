#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace KCD
{
    /// <summary>
    /// Web 版で、ゲームが作ったファイルをブラウザにダウンロードさせる (#61)。
    /// Web 版の persistentDataPath は IndexedDB の中にあり、プレイヤーが取り出す手段が無いため。
    /// 中身は Assets/Plugins/WebGL/KCDDownload.jslib（Blob を作ってリンクを押す）。Web 版以外では何もしない。
    /// </summary>
    public static class WebDownload
    {
        /// <summary>この実行環境でダウンロードさせられるか。Web 版のプレイヤーだけ true（エディタでは false）。</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>渡せる中身か。名前か中身が空のものは渡さない。</summary>
        public static bool CanSend(string fileName, byte[] data)
        {
            return !string.IsNullOrEmpty(fileName) && data != null && data.Length > 0;
        }

        /// <summary>ダウンロードさせる。ブラウザに渡せたら true。Web 版以外と、渡せない中身では false。</summary>
        public static bool Send(string fileName, byte[] data, string mimeType)
        {
            if (!Supported || !CanSend(fileName, data))
            {
                return false;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return KCD_DownloadFile(fileName, data, data.Length, mimeType ?? string.Empty) != 0;
#else
            return false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int KCD_DownloadFile(string fileName, byte[] data, int length, string mimeType);
#endif
    }
}
