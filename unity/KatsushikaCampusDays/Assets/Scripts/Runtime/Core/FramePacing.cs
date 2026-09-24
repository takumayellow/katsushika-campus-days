using System;
using UnityEngine;

namespace KCD
{
    /// <summary>フレームの刻み方。変えるかどうかと, 変えるならその値。</summary>
    public readonly struct FramePacingPlan
    {
        public FramePacingPlan(bool change, int vSyncCount, int targetFrameRate)
        {
            Change = change;
            VSyncCount = vSyncCount;
            TargetFrameRate = targetFrameRate;
        }

        /// <summary>false なら QualitySettings と Application に触らない。</summary>
        public bool Change { get; }

        public int VSyncCount { get; }

        /// <summary>-1 は上限なし（vSync に任せる）。</summary>
        public int TargetFrameRate { get; }
    }

    /// <summary>
    /// Windows 版のフレームの刻み (#15)。
    ///
    /// 品質レベルはどちらも vSyncCount 0 で, Windows 版は上限なしで回っていた（#15 の計測で中央値 3.7〜4.0 ms,
    /// CPU が律速）。画面の書き換えに合わせるため vSync を入れる。vSync を切って起動したときは 60 fps で止める。
    /// vSync を切るのは計測の比較用で, 起動引数 <c>-kcd-vsync off</c> で指定する（設定画面には出さない。
    /// 理由は docs/WEBGL_BUDGET.md）。
    ///
    /// Web 版には触らない。ブラウザは requestAnimationFrame で画面に合わせて回し, targetFrameRate を入れると
    /// setTimeout で回すように切り替わってかえって刻みが乱れる。エディタにも触らない（QualitySettings を
    /// 書き換えると ProjectSettings に残るため）。
    /// </summary>
    public static class FramePacing
    {
        public const string VSyncArgument = "-kcd-vsync";
        public const int FallbackFrameRate = 60;

        /// <summary>そのプラットフォームでの刻み方。</summary>
        public static FramePacingPlan Plan(RuntimePlatform platform, bool vSyncWanted)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.LinuxPlayer:
                    return vSyncWanted
                        ? new FramePacingPlan(true, 1, -1)
                        : new FramePacingPlan(true, 0, FallbackFrameRate);
                default:
                    return new FramePacingPlan(false, 0, -1);
            }
        }

        /// <summary>
        /// 起動引数から vSync を使うか読む。<c>-kcd-vsync off</c>・<c>0</c>・<c>false</c> なら使わない。
        /// 指定が無いか, それ以外の値なら使う。
        /// </summary>
        public static bool VSyncWanted(string[] args)
        {
            if (args == null)
            {
                return true;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = null;
                if (string.Equals(arg, VSyncArgument, StringComparison.OrdinalIgnoreCase))
                {
                    value = i + 1 < args.Length ? args[i + 1] : null;
                }
                else if (arg != null && arg.StartsWith(VSyncArgument + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = arg.Substring(VSyncArgument.Length + 1);
                }
                else
                {
                    continue;
                }

                return !IsOff(value);
            }

            return true;
        }

        /// <summary>起動時に一度だけ呼ぶ。変えたときは true。</summary>
        public static bool Apply()
        {
            FramePacingPlan plan = Plan(Application.platform, VSyncWanted(Environment.GetCommandLineArgs()));
            if (!plan.Change)
            {
                return false;
            }

            QualitySettings.vSyncCount = plan.VSyncCount;
            Application.targetFrameRate = plan.TargetFrameRate;
            return true;
        }

        private static bool IsOff(string value)
        {
            return string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "0", StringComparison.Ordinal)
                   || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
