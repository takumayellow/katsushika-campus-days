namespace KCD
{
    /// <summary>
    /// フォトモードの操作案内の文 (#12)。キーボード・ゲームパッド・タッチ (#187)、日本語と英語で 6 通り。
    /// キーボードとゲームパッドは 2 行、タッチは案内の出し入れを足した 3 行。
    /// キーの割り当ては KCDInput の Photo* と、タッチのボタンの文字は TouchHud と合わせる。
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

        private const string TouchJa =
            "移動 左のスティック　向き なぞる　ズーム つまむ・広げる\n" +
            "撮影 「撮影」のボタン　戻る 「戻る」のボタン\n" +
            "案内 「案内」のボタン";

        private const string TouchEn =
            "Move Left stick   Look Drag   Zoom Pinch\n" +
            "Shoot the Shoot button   Back the Back button\n" +
            "Help the Help button";

        /// <summary>
        /// 案内の文。english で英語、gamepad でゲームパッドのボタン名、touch で画面のボタンの名前にする。
        /// 指で遊んでいる間は仮想のゲームパッドから入るので、touch を gamepad より先に見る。
        /// </summary>
        public static string Text(bool english, bool gamepad, bool touch = false)
        {
            if (touch)
            {
                return english ? TouchEn : TouchJa;
            }

            if (gamepad)
            {
                return english ? GamepadEn : GamepadJa;
            }

            return english ? KeyboardEn : KeyboardJa;
        }
    }
}
