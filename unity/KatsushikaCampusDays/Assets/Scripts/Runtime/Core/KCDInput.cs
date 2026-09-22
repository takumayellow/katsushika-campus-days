using UnityEngine;
using UnityEngine.InputSystem;

namespace KCD
{
    /// <summary>
    /// Input System のデバイスを直接読む薄いファサード。
    /// .inputactions アセットへの GUID 参照を持たずに済むので、シーンをコードだけで組み立てられる。
    /// 操作: WASD または矢印キーで移動 / Shift ダッシュ / Space ジャンプ / E 会話 / Tab クエストログ / Esc メニュー。
    /// </summary>
    public static class KCDInput
    {
        /// <summary>UI がモーダル表示中は移動入力を殺す。DialogueSystem などが立てる。</summary>
        public static bool GameplayBlocked { get; set; }

        /// <summary>座っている間など、UI は生きているが移動だけ止めたいときに立てる。</summary>
        public static bool MovementLocked { get; set; }

        /// <summary>フォトモード中。移動は止まるがカメラは動かせる。</summary>
        public static bool PhotoMode { get; set; }

        /// <summary>視点操作を止めるか。フォトモードではカメラだけ動かす。</summary>
        public static bool LookBlocked => GameplayBlocked && !PhotoMode;

        private static int _modalClosedFrame = -1;

        /// <summary>モーダルを閉じた側が呼ぶ。同じフレームで下の画面が同じキーを拾うのを防ぐ。</summary>
        public static void MarkModalClosed()
        {
            _modalClosedFrame = Time.frameCount;
        }

        /// <summary>このフレームでモーダルが閉じられたか。</summary>
        public static bool ModalClosedThisFrame => _modalClosedFrame == Time.frameCount;

        /// <summary>移動入力（x = 左右、y = 前後）。カメラ相対に変換するのは PlayerController 側。</summary>
        public static Vector2 Move
        {
            get
            {
                if (GameplayBlocked || MovementLocked)
                {
                    return Vector2.zero;
                }

                var move = Vector2.zero;
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    // WASD と矢印キーのどちらでも動く（#36）。両方同時に押しても 1 軸 1 以内。
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) { move.y += 1f; }
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) { move.y -= 1f; }
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) { move.x += 1f; }
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) { move.x -= 1f; }
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    move += gamepad.leftStick.ReadValue();
                }

                return Vector2.ClampMagnitude(move, 1f);
            }
        }

        /// <summary>視点入力。マウス移動量とゲームパッド右スティック。</summary>
        public static Vector2 Look
        {
            get
            {
                var look = Vector2.zero;
                Mouse mouse = Mouse.current;
                if (mouse != null)
                {
                    look += mouse.delta.ReadValue() * 0.06f;
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    look += gamepad.rightStick.ReadValue() * (180f * Time.deltaTime);
                }

                return look;
            }
        }

        /// <summary>カメラのズーム操作（ホイール）。前方向が正。</summary>
        public static float Zoom
        {
            get
            {
                Mouse mouse = Mouse.current;
                return mouse == null ? 0f : mouse.scroll.ReadValue().y * 0.01f;
            }
        }

        /// <summary>ダッシュ（押しっぱなし）。</summary>
        public static bool Sprint =>
            !GameplayBlocked && !MovementLocked &&
            ((Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
             (Gamepad.current != null && Gamepad.current.leftShoulder.isPressed));

        /// <summary>ジャンプ（押した瞬間）。</summary>
        public static bool JumpPressed =>
            !GameplayBlocked && !MovementLocked &&
            ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
             (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame));

        /// <summary>調べる / 会話を進める（押した瞬間）。会話中も受け付ける。</summary>
        public static bool InteractPressed =>
            (Keyboard.current != null &&
             (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

        /// <summary>クエストログの開閉。</summary>
        public static bool QuestLogPressed =>
            (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);

        /// <summary>メニュー / キャンセル。</summary>
        public static bool MenuPressed =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        /// <summary>キャラクター選択などの水平メニュー移動。-1 / 0 / +1。</summary>
        public static int MenuHorizontal
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) { return 1; }
                    if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) { return -1; }
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    if (gamepad.dpad.right.wasPressedThisFrame) { return 1; }
                    if (gamepad.dpad.left.wasPressedThisFrame) { return -1; }
                }

                return 0;
            }
        }

        /// <summary>メニューの上下移動。-1 = 上、+1 = 下。</summary>
        public static int MenuVertical
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) { return 1; }
                    if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) { return -1; }
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    if (gamepad.dpad.down.wasPressedThisFrame) { return 1; }
                    if (gamepad.dpad.up.wasPressedThisFrame) { return -1; }
                }

                return 0;
            }
        }

        /// <summary>決定（タイトル / キャラ選択）。</summary>
        public static bool SubmitPressed =>
            (Keyboard.current != null &&
             (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        /// <summary>移動キーが押されているか（ロック中でも読む。座りから立つ判定用）。</summary>
        public static bool MoveRequested
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null &&
                    (keyboard.wKey.isPressed || keyboard.sKey.isPressed ||
                     keyboard.aKey.isPressed || keyboard.dKey.isPressed ||
                     keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed ||
                     keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed))
                {
                    return true;
                }

                Gamepad gamepad = Gamepad.current;
                return gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > 0.2f;
            }
        }

        /// <summary>ジャンプキーが押された瞬間（ロック中でも読む）。</summary>
        public static bool JumpRequested =>
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        /// <summary>フォトモードの切り替え（P / 北ボタン）。</summary>
        public static bool PhotoPressed =>
            (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);

        /// <summary>手を振る（Q / 右ショルダー）。</summary>
        public static bool WavePressed =>
            (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);

        /// <summary>戻る（Esc / 東ボタン）。パネルを閉じる。</summary>
        public static bool CancelPressed =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        /// <summary>タイトルでクレジットを開く（C / 北ボタン）。</summary>
        public static bool CreditsPressed =>
            (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);

        /// <summary>タイトルで設定を開く（O / セレクト）。</summary>
        public static bool SettingsPressed =>
            (Keyboard.current != null && Keyboard.current.oKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);

        /// <summary>クイックセーブ（F5）。</summary>
        public static bool QuickSavePressed =>
            Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;

        /// <summary>クイックロード（F9）。</summary>
        public static bool QuickLoadPressed =>
            Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame;
    }
}
