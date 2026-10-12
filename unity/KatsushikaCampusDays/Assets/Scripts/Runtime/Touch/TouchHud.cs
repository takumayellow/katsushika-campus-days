using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// キャンパスのタッチの操作 UI (#187)。指で遊んでいる間だけ出す（<see cref="InputMode"/>）。
    /// マウスに戻ったら丸ごと止める。仮想のゲームパッドは最後の OnScreenControl が止まると消えるので、
    /// PC でゲームパッドを使う人の <c>Gamepad.current</c> を取らない。
    /// 会話・メニューの間はスティックとボタンを隠す。ポーズのボタンだけは、ポーズ中も閉じるために出す。
    /// </summary>
    public sealed class TouchHud : MonoBehaviour
    {
        [SerializeField] private GameObject _controls;
        [SerializeField] private CanvasGroup _play;
        [SerializeField] private CanvasGroup _pause;
        [SerializeField] private TMP_Text _jumpLabel;
        [SerializeField] private TMP_Text _interactLabel;
        [SerializeField] private TMP_Text _sprintLabel;

        /// <summary>SceneBuilder から差し込む。controls は既定で止めておく（PC で仮想のゲームパッドを作らない）。</summary>
        public void Bind(GameObject controls, CanvasGroup play, CanvasGroup pause)
        {
            _controls = controls;
            _play = play;
            _pause = pause;
        }

        /// <summary>ボタンの文字。言語を切り替えたときに書き直す。</summary>
        public void BindLabels(TMP_Text jump, TMP_Text interact, TMP_Text sprint)
        {
            _jumpLabel = jump;
            _interactLabel = interact;
            _sprintLabel = sprint;
        }

        /// <summary>スティックとボタンを出すか。指で遊んでいて、会話やメニューで操作を止めていないとき。</summary>
        public static bool ShowsPlayControls(bool touch, bool gameplayBlocked)
        {
            return touch && !gameplayBlocked;
        }

        /// <summary>ポーズのボタンを出すか。ポーズ中（設定を開いている間も）は閉じるために出したまま。</summary>
        public static bool ShowsPauseButton(bool touch, bool gameplayBlocked, bool paused)
        {
            return touch && (!gameplayBlocked || paused);
        }

        private void OnEnable()
        {
            L.LocaleChanged += RefreshLabels;
            RefreshLabels();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshLabels;
        }

        private void OnDestroy()
        {
            TouchInput.Clear();
        }

        private void Update()
        {
            bool touch = InputMode.IsTouch;
            if (_controls != null && _controls.activeSelf != touch)
            {
                _controls.SetActive(touch);
                if (!touch)
                {
                    TouchInput.Clear();
                }
            }

            if (!touch)
            {
                return;
            }

            bool blocked = KCDInput.GameplayBlocked;
            PauseMenu pause = HUD.Instance != null ? HUD.Instance.PauseMenu : null;
            bool paused = pause != null && pause.IsPaused;
            Show(_play, ShowsPlayControls(true, blocked));
            Show(_pause, ShowsPauseButton(true, blocked, paused));
        }

        private static void Show(CanvasGroup group, bool show)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = show ? 1f : 0f;
            group.blocksRaycasts = show;
            group.interactable = show;
        }

        private void RefreshLabels()
        {
            if (_jumpLabel != null)
            {
                _jumpLabel.text = L.Pick("ジャンプ", "Jump");
            }

            if (_interactLabel != null)
            {
                _interactLabel.text = L.Pick("話す\n調べる", "Talk\nUse");
            }

            if (_sprintLabel != null)
            {
                _sprintLabel.text = L.Pick("走る", "Run");
            }
        }
    }
}
