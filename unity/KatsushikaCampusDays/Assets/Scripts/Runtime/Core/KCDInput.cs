using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KCD
{
    /// <summary>
    /// Input System のデバイスを直接読む薄いファサード。
    /// .inputactions アセットへの GUID 参照を持たずに済むので、シーンをコードだけで組み立てられる。
    /// 操作: WASD または矢印キーで移動 / Shift（左右とも）ダッシュ / Space ジャンプ / E 会話 / Tab クエストログ / F 全画面 / Esc メニュー。
    /// </summary>
    public static class KCDInput
    {
        /// <summary>
        /// Block(owner) で掛けられている封鎖。オーナーごとに持つので、ほかのシステムの封鎖を外してしまわない (#40)。
        /// 以前は 1 本の bool だけで、建物の出入り（InteriorLoader.Travel）が暗転の終わりに無条件で false に戻し、
        /// その間に開いた会話・ポーズ・落下からの復帰などの封鎖まで外していた。
        /// </summary>
        private static readonly HashSet<object> Blockers = new HashSet<object>(ReferenceComparer.Instance);

        /// <summary>
        /// GameplayBlocked への代入で立てる、オーナーを持たない封鎖（旧来の書き方）。
        /// ポーズ・クエストログ・写真モード・リザルトは Block(this) に移した (#62)。
        /// </summary>
        private static bool _sharedBlock;

        /// <summary>
        /// UI がモーダル表示中などで、移動・視点・操作を止めているか。どれか 1 つでも封鎖があれば true。
        /// 代入はオーナーを持たない 1 枚の封鎖を立てる / 外すだけで、<see cref="Block"/> で掛けた封鎖は外さない。
        /// 新しく封鎖するシステムは代入ではなく Block(this) / Unblock(this) を使う。
        /// </summary>
        public static bool GameplayBlocked
        {
            get => _sharedBlock || Blockers.Count > 0;
            set => _sharedBlock = value;
        }

        /// <summary>owner の名前で操作を封鎖する。同じ owner が何度掛けても 1 枚として数え、Unblock 1 回で外れる。</summary>
        public static void Block(object owner)
        {
            if (owner != null)
            {
                Blockers.Add(owner);
            }
        }

        /// <summary>owner が掛けた封鎖だけを外す。ほかのオーナーの封鎖と GameplayBlocked への代入で立てた封鎖はそのまま。</summary>
        public static void Unblock(object owner)
        {
            if (owner != null)
            {
                Blockers.Remove(owner);
            }
        }

        /// <summary>owner が封鎖を掛けているか。</summary>
        public static bool IsBlockedBy(object owner) => owner != null && Blockers.Contains(owner);

        /// <summary>封鎖をすべて外す。シーンを切り替えるとき（封鎖を掛けた側がまとめて消えるとき）だけ使う。</summary>
        public static void ClearAllBlocks()
        {
            Blockers.Clear();
            _sharedBlock = false;
        }

        /// <summary>
        /// 参照の同一性だけで比べる。UnityEngine.Object は破棄後に == null と等しく見えるので、
        /// 既定の比較だと破棄済みのオーナー同士を取り違えるおそれがある。
        /// </summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

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

        /// <summary>画面をまだ出していないことを表すフレーム番号。</summary>
        public const int NoFrame = -1;

        /// <summary>
        /// 画面を出したフレームの入力を捨てるか。出した側と出された側が同じ Enter を同じフレームで拾うと、
        /// 出た画面がその場で決定されてしまう（タイトル → キャラクター選択が 1 フレームも操作できなかった, #6）。
        /// activatedFrame が負（まだ出していない）なら常に捨てる。
        /// </summary>
        public static bool IgnoresInput(int activatedFrame, int currentFrame)
        {
            return activatedFrame < 0 || currentFrame <= activatedFrame;
        }

        /// <summary>今のフレームで見る版。</summary>
        public static bool IgnoresInput(int activatedFrame)
        {
            return IgnoresInput(activatedFrame, Time.frameCount);
        }

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

        /// <summary>
        /// ダッシュ（押しっぱなし）。Shift は左右どちらでも効く。
        /// 操作説明の「Shift / L トリガー」に合わせ、ゲームパッドは L ボタンと L トリガーの両方を受け付ける。
        /// </summary>
        public static bool Sprint =>
            !GameplayBlocked && !MovementLocked &&
            ((Keyboard.current != null &&
              (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)) ||
             (Gamepad.current != null &&
              (Gamepad.current.leftShoulder.isPressed || Gamepad.current.leftTrigger.isPressed)));

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

        /// <summary>
        /// 全画面の切り替え（F）。メニューやポーズ中でも効かせたいので封鎖を見ない。
        /// ゲームパッドは割り当てない（ブラウザがゲームパッド入力をユーザー操作と見なさず、全画面要求が通らない, #48）。
        /// </summary>
        public static bool FullscreenPressed =>
            Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;

        /// <summary>クイックセーブ（F5）。</summary>
        public static bool QuickSavePressed =>
            Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;

        /// <summary>クイックロード（F9）。</summary>
        public static bool QuickLoadPressed =>
            Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame;
    }
}
