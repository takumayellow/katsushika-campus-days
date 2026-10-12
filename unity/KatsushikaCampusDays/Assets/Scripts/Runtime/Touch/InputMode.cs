using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace KCD
{
    /// <summary>最後に使った入力の種類。</summary>
    public enum InputKind
    {
        /// <summary>キーボード・マウス（ゲームパッドもこちらとして扱う）。</summary>
        Pointer,

        /// <summary>指。画面にタッチの操作 UI を出す。</summary>
        Touch,
    }

    /// <summary>
    /// タッチの操作 UI を出すかを、最後に使った入力で決める (#187)。端末の種類では決めない
    /// （キーボード付きのタブレットや、タッチ画面の PC があるため）。起動直後だけは端末から決める。
    /// 判定はフレームに 1 回、最初に読まれたときに行う。
    /// </summary>
    public static class InputMode
    {
        /// <summary>
        /// 指を離してからこの秒数の間は、マウスの操作で Pointer に戻さない。
        /// ブラウザは指で叩くと互換のマウスのイベントも出すので、それで切り替わるのを防ぐ。
        /// </summary>
        public const float MouseAfterTouchGrace = 0.5f;

        /// <summary>ゲームパッドを使ったとみなすスティックの倒し具合（の 2 乗）。遊びで戻らないよう半分より大きく。</summary>
        private const float PadStickThreshold = 0.25f;

        private static InputKind _current;
        private static bool _initialized;
        private static int _evaluatedFrame = -1;
        private static float _lastTouchTime = float.NegativeInfinity;
        private static InputKind? _forced;

        /// <summary>入力の種類が変わったときに、新しい種類を渡して呼ぶ。</summary>
        public static event Action<InputKind> Changed;

        /// <summary>今の入力の種類。</summary>
        public static InputKind Current
        {
            get
            {
                Evaluate();
                return _forced ?? _current;
            }
        }

        /// <summary>タッチの操作 UI を出しているか。</summary>
        public static bool IsTouch => Current == InputKind.Touch;

        /// <summary>
        /// 次の入力の種類。指が触れていれば Touch。Touch のときはキーかゲームパッドを使うか（keyPressed）、
        /// 指を離して <see cref="MouseAfterTouchGrace"/> 秒より後にマウスを使ったら Pointer に戻す。
        /// </summary>
        public static InputKind Next(
            InputKind current, bool touching, bool keyPressed, bool mouseUsed, float sinceTouch)
        {
            if (touching)
            {
                return InputKind.Touch;
            }

            if (current == InputKind.Touch &&
                (keyPressed || (mouseUsed && sinceTouch > MouseAfterTouchGrace)))
            {
                return InputKind.Pointer;
            }

            return current;
        }

        /// <summary>起動直後の種類。スマホ・タブレットのブラウザか、マウスが無くタッチ画面があれば Touch。</summary>
        public static InputKind Initial(bool isMobilePlatform, bool hasTouchscreen, bool hasMouse)
        {
            return isMobilePlatform || (hasTouchscreen && !hasMouse) ? InputKind.Touch : InputKind.Pointer;
        }

        /// <summary>テストから種類を決め打ちする。null で解除して、実際の入力で決める判定に戻す。</summary>
        public static void Force(InputKind? kind)
        {
            InputKind before = Current;
            _forced = kind;
            InputKind after = Current;
            if (before != after)
            {
                Changed?.Invoke(after);
            }
        }

        /// <summary>
        /// 操作中のポインタのロックを掛ける。マウスのときだけ掛け、指のときは外す
        /// （ロックするとタッチで叩いた位置が UI に届かない）。
        /// </summary>
        public static void CaptureCursor()
        {
            bool pointer = !IsTouch;
            Cursor.lockState = pointer ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = false;
        }

        private static void Evaluate()
        {
            int frame = Time.frameCount;
            if (_initialized && _evaluatedFrame == frame)
            {
                return;
            }

            _evaluatedFrame = frame;
            if (!_initialized)
            {
                _initialized = true;
                _current = Initial(Application.isMobilePlatform, Touchscreen.current != null, Mouse.current != null);
            }

            float now = Time.unscaledTime;
            bool touching = AnyTouch();
            if (touching)
            {
                _lastTouchTime = now;
            }

            // スマホのブラウザは指で叩くたびに互換のマウスを出すので、マウスでは戻さない（キーとゲームパッドで戻る）。
            bool mouseUsed = !Application.isMobilePlatform && MouseUsed();
            InputKind next = Next(_current, touching, KeyPressed() || PadUsed(), mouseUsed, now - _lastTouchTime);
            if (next == _current)
            {
                return;
            }

            _current = next;
            if (_forced == null)
            {
                if (next == InputKind.Touch)
                {
                    Cursor.lockState = CursorLockMode.None;
                }

                Changed?.Invoke(next);
            }
        }

        private static bool AnyTouch()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null)
            {
                return false;
            }

            foreach (TouchControl touch in screen.touches)
            {
                if (touch.press.isPressed || touch.press.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool KeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        /// <summary>
        /// 本物のゲームパッドのボタンを押したか、スティックを倒したか。
        /// タッチの操作 UI の仮想のゲームパッド（native でない）は数えない。
        /// </summary>
        private static bool PadUsed()
        {
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.native)
                {
                    continue;
                }

                if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                    pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                    pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
                    pad.leftStick.ReadValue().sqrMagnitude > PadStickThreshold ||
                    pad.rightStick.ReadValue().sqrMagnitude > PadStickThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MouseUsed()
        {
            Mouse mouse = Mouse.current;
            return mouse != null &&
                   (mouse.delta.ReadValue().sqrMagnitude > 0f ||
                    mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                    mouse.scroll.ReadValue().sqrMagnitude > 0f);
        }

        /// <summary>ドメインを読み直さない再生でも、前回の再生の判定を持ち越さない。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _initialized = false;
            _evaluatedFrame = -1;
            _lastTouchTime = float.NegativeInfinity;
            _forced = null;
            Changed = null;
        }
    }
}
