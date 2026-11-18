using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// 展示モードの放置の見張り (#466)。タイトル以外で <see cref="ExhibitMode.IdleSeconds"/> 秒操作が無ければ
    /// 予告を出してからタイトルへ戻す。来場者が途中で離れても、次の人がタイトルから始められる。
    ///
    /// ポーズ・リザルト・エンディング・フォトモードの最中でも戻す（どれも入力を待って止まるので、放っておくと
    /// 次の人まで固まったまま残る）。後始末はポーズメニューの「タイトルへ」と同じ <see cref="GameManager.ReturnToTitle"/> に任せる。
    /// 読み込みの最中だけは待つ。展示モードのときだけ <see cref="GameManager"/> が付ける。
    /// </summary>
    public sealed class ExhibitIdle : MonoBehaviour
    {
        /// <summary>スティック・トリガーをこれより倒したら操作とみなす（InputMode と同じ）。</summary>
        private const float PadThreshold = 0.25f;

        private float _lastInputAt;
        private bool _warned;

        /// <summary>最後に操作してからの実時間（秒）。</summary>
        public float IdleFor => Time.unscaledTime - _lastInputAt;

        private void OnEnable()
        {
            MarkActive();
        }

        private void Update()
        {
            if (AnyInput() || SceneManager.GetActiveScene().name == GameManager.TitleSceneName)
            {
                MarkActive();
                return;
            }

            switch (ExhibitMode.Decide(IdleFor, _warned, SceneLoader.IsAnyLoading))
            {
                case ExhibitMode.IdleAction.Warn:
                    _warned = true;
                    HUD.Instance?.ShowToast(L.Pick(
                        "操作が無いので、まもなくタイトルへ戻ります",
                        "No input. Returning to the title soon"));
                    break;
                case ExhibitMode.IdleAction.ReturnToTitle:
                    Debug.Log("[KCD] exhibit: idle " + Mathf.RoundToInt(IdleFor) + "s, back to title");
                    MarkActive();
                    GameManager.Instance.ReturnToTitle();
                    break;
            }
        }

        private void MarkActive()
        {
            _lastInputAt = Time.unscaledTime;
            _warned = false;
        }

        /// <summary>キー・マウス・ゲームパッド・指のどれかを使っているか（押し続けて歩いている間も含む）。</summary>
        private static bool AnyInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.isPressed)
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed ||
                                  mouse.middleButton.isPressed || mouse.delta.ReadValue().sqrMagnitude > 0f ||
                                  mouse.scroll.ReadValue().sqrMagnitude > 0f))
            {
                return true;
            }

            foreach (Gamepad pad in Gamepad.all)
            {
                if (PadInUse(pad))
                {
                    return true;
                }
            }

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.isPressed;
        }

        private static bool PadInUse(Gamepad pad)
        {
            return pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed ||
                   pad.buttonNorth.isPressed || pad.startButton.isPressed || pad.selectButton.isPressed ||
                   pad.leftShoulder.isPressed || pad.rightShoulder.isPressed ||
                   pad.leftStickButton.isPressed || pad.rightStickButton.isPressed ||
                   pad.dpad.ReadValue().sqrMagnitude > 0f ||
                   pad.leftTrigger.ReadValue() > PadThreshold || pad.rightTrigger.ReadValue() > PadThreshold ||
                   pad.leftStick.ReadValue().sqrMagnitude > PadThreshold * PadThreshold ||
                   pad.rightStick.ReadValue().sqrMagnitude > PadThreshold * PadThreshold;
        }
    }
}
