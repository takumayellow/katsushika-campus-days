#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// Ctrl+F で全画面と窓表示を入れ替える常駐部品 (#48)。
    /// Web 版はキャンバスをクリックするとポインターロックでカーソルが消え、ページ下の全画面ボタンを押せなくなる。
    /// ブラウザはユーザー操作の中でしか全画面を許さないが、キー入力を処理する流れから要求を出せば通る。
    /// ゲームパッドには割り当てない。ブラウザはゲームパッド入力をユーザー操作と見なさず、全画面要求が弾かれるため。
    /// シーンに置かなくても起動時に自分で現れるので、SceneBuilder 側の変更は要らない。
    /// </summary>
    public sealed class ScreenModeHotkey : MonoBehaviour
    {
        /// <summary>
        /// キー割り当ての設定中など、1 打鍵を横取りしたい側が立てる。
        /// いまの設定画面（SettingsView）は上下左右と Esc しか見ず、キーを取り込まないので誰も立てていない。
        /// </summary>
        public static bool KeyCaptureActive { get; set; }

        /// <summary>シーンにインスタンスが無くても起動時に必ず用意する。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("KCD.ScreenModeHotkey");
            host.AddComponent<ScreenModeHotkey>();
            DontDestroyOnLoad(host);
        }

        /// <summary>この打鍵で全画面を切り替えてよいか。文字入力中とキー取り込み中は見送る。</summary>
        public static bool ShouldToggle(bool pressed, bool typing, bool capturingKeys)
        {
            return pressed && !typing && !capturingKeys;
        }

        /// <summary>切り替えた先の表示モード。全画面はビルド設定と同じ「枠なし全画面ウィンドウ」。</summary>
        public static FullScreenMode NextMode(bool fullscreenNow)
        {
            return fullscreenNow ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        }

        /// <summary>いま文字入力の欄にカーソルがあるか。EventSystem が無いシーン（いまは全部）では常に false。</summary>
        public static bool IsTypingInInputField()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }

            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused)
            {
                return true;
            }

            var legacy = selected.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        /// <summary>テストが全画面の可否を差し替える。null なら実際の端末の答えを使う。</summary>
        public static bool? FullscreenSupportOverride { get; set; }

        /// <summary>
        /// この端末のブラウザが全画面を許すか。iPhone の Safari は canvas の Fullscreen API を受け付けず、
        /// 全画面にしようとしても何も起きない（iPad は許す）。Web 版以外では常に true。
        /// </summary>
        public static bool FullscreenSupported
        {
            get
            {
                if (FullscreenSupportOverride.HasValue)
                {
                    return FullscreenSupportOverride.Value;
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                return KCD_FullscreenSupported() != 0;
#else
                return true;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int KCD_FullscreenSupported();
#endif

        /// <summary>全画面と窓表示を入れ替える。全画面を使えない端末では何もしない。</summary>
        public static void Toggle()
        {
            if (!FullscreenSupported)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Web 版に窓／全画面の区別は無く、ブラウザの全画面要求をこの打鍵のイベントの中で出すだけ。
            Screen.fullScreen = !Screen.fullScreen;
#else
            Screen.fullScreenMode = NextMode(Screen.fullScreen);
#endif
        }

        private void Update()
        {
            if (ShouldToggle(KCDInput.FullscreenPressed, IsTypingInInputField(), KeyCaptureActive))
            {
                Toggle();
            }
        }
    }
}
