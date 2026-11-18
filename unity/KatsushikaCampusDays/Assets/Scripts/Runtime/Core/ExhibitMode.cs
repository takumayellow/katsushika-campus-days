using System;
using System.IO;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 展示モード (#466)。来場者が入れ替わりながら同じ PC で遊ぶときのための動き。
    ///
    /// exe と同じフォルダに <see cref="MarkerFileName"/> があるとき、または起動引数 <see cref="Arg"/> があるときだけ働く。
    /// 配信ソフトの起動設定は変えずに、展示用のフォルダへビルドを置くときに目印を置けば有効になる。
    ///
    /// 働いている間は、タイトルに「つづきから」を出さずいつも「はじめから」で始め（前の人の続きから始まらない）、
    /// キャンパスで <see cref="IdleSeconds"/> 秒操作が無ければタイトルへ戻す（<see cref="ExhibitIdle"/>）。
    /// </summary>
    public static class ExhibitMode
    {
        /// <summary>exe と同じフォルダに置く目印のファイル名。中身は見ない。</summary>
        public const string MarkerFileName = "kcd_exhibit.txt";

        /// <summary>目印のファイルの代わりに使える起動引数。</summary>
        public const string Arg = "-kcd-exhibit";

        /// <summary>キャンパスで操作が無いままこれだけ経つとタイトルへ戻す（秒）。</summary>
        public const float IdleSeconds = 180f;

        /// <summary>戻す何秒前に予告を出すか。</summary>
        public const float WarnBeforeSeconds = 20f;

        private static bool? _enabled;

        /// <summary>展示モードか。起動して最初に聞かれたときに決め、以後は変えない。テストからは差し替えられる。</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = Detect();
                }

                return _enabled.Value;
            }
            set => _enabled = value;
        }

        /// <summary>テストで差し替えた値を捨て、次に聞かれたときに決め直す。</summary>
        public static void ResetForTests()
        {
            _enabled = null;
        }

        /// <summary>起動引数か目印のファイルのどちらかがあれば展示モード。</summary>
        public static bool IsEnabled(string[] args, bool markerExists)
        {
            if (markerExists)
            {
                return true;
            }

            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Arg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 目印のファイルの場所。exe のフォルダは <c>Application.dataPath</c>（…/KatsushikaCampusDays_Data）の親。
        /// Web 版とエディタでは探さない（null）。
        /// </summary>
        public static string MarkerPath(string dataPath, bool isStandalonePlayer)
        {
            if (!isStandalonePlayer || string.IsNullOrEmpty(dataPath))
            {
                return null;
            }

            string root = Path.GetDirectoryName(dataPath);
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, MarkerFileName);
        }

        private static bool Detect()
        {
            bool marker = false;
            try
            {
                bool standalone = !Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer;
                string path = MarkerPath(Application.dataPath, standalone);
                marker = path != null && File.Exists(path);
            }
            catch (Exception e)
            {
                // 目印が読めなくても通常の動きで遊べるので、止めずに通常として続ける。
                Debug.LogWarning("[KCD] exhibit marker check failed: " + e.Message);
            }

            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                args = null;
            }

            bool enabled = IsEnabled(args, marker);
            if (enabled)
            {
                Debug.Log("[KCD] exhibit mode on (idle " + IdleSeconds + "s)");
            }

            return enabled;
        }

        /// <summary>タイトルで「つづきから」を出すか。展示モードでは前の人の続きを出さない。</summary>
        public static bool OffersContinue(bool exhibit, bool hasReadableSave)
        {
            return hasReadableSave && !exhibit;
        }

        /// <summary>放置の判定で何をするか。</summary>
        public enum IdleAction
        {
            None,
            Warn,
            ReturnToTitle
        }

        /// <summary>
        /// 操作が無いまま idle 秒経ったときにすること。warned は予告をもう出したか。
        /// busy（シーンの読み込みの最中）の間は何もしない（数えるのは呼ぶ側が続けてよい）。
        /// </summary>
        public static IdleAction Decide(float idle, bool warned, bool busy, float limit = IdleSeconds,
            float warnBefore = WarnBeforeSeconds)
        {
            if (busy)
            {
                return IdleAction.None;
            }

            if (idle >= limit)
            {
                return IdleAction.ReturnToTitle;
            }

            if (!warned && idle >= limit - warnBefore)
            {
                return IdleAction.Warn;
            }

            return IdleAction.None;
        }
    }
}
