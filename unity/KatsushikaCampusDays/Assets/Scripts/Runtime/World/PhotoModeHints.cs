namespace KCD
{
    /// <summary>
    /// フォトモードの操作案内の文 (#12)。キーボードとゲームパッド、日本語と英語で 4 通り。
    /// キーの割り当ては KCDInput の Photo* と合わせる。
    /// </summary>
    public static class PhotoModeHints
    {
        private const string KeyboardJa =
            "移動 WASD　上下 E / Q　向き マウス　ズーム ホイール　速く Shift\n" +
            "撮影 Enter / Space　元の位置 R　案内 H　戻る P / Esc";

        private const string KeyboardEn =
            "Move WASD   Up/Down E / Q   Look Mouse   Zoom Wheel   Fast Shift\n" +
            "Shoot Enter / Space   Reset R   Help H   Back P / Esc";

        private const string GamepadJa =
            "移動 左スティック　上下 RT / LT　向き 右スティック　ズーム 十字キー上下　速く LB\n" +
            "撮影 A / X　元の位置 R3　案内 L3　戻る Y / B";

        private const string GamepadEn =
            "Move L-stick   Up/Down RT / LT   Look R-stick   Zoom D-pad   Fast LB\n" +
            "Shoot A / X   Reset R3   Help L3   Back Y / B";

        /// <summary>案内の文。english で英語、gamepad でゲームパッドのボタン名にする。</summary>
        public static string Text(bool english, bool gamepad)
        {
            if (gamepad)
            {
                return english ? GamepadEn : GamepadJa;
            }

            return english ? KeyboardEn : KeyboardJa;
        }
    }
}
