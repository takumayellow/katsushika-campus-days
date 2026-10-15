using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面が縦長か。スマホを縦に持っても遊べるよう、UI の置き方と画角を縦長の画面に合わせて切り替える。
    /// 入力の種類（<see cref="InputMode"/>）とは別に決める。縦長の窓で遊ぶ PC も同じ置き方にする。
    /// </summary>
    public static class ScreenShape
    {
        private static bool? _forced;

        /// <summary>今の画面が縦長か。</summary>
        public static bool IsPortrait => _forced ?? IsPortraitSize(Screen.width, Screen.height);

        /// <summary>画面の向きを決め打ちする（テスト用）。null で実際の画面の大きさに戻す。</summary>
        public static void Force(bool? portrait)
        {
            _forced = portrait;
        }

        /// <summary>width×height の画面が縦長か。正方形は横長として扱う。</summary>
        public static bool IsPortraitSize(float width, float height)
        {
            return height > width;
        }
    }
}
