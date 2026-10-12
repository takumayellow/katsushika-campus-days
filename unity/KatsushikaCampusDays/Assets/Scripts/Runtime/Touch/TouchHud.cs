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
        [SerializeField] private TMP_Text _questLabel;

        /// <summary>SceneBuilder から差し込む。controls は既定で止めておく（PC で仮想のゲームパッドを作らない）。</summary>
        public void Bind(GameObject controls, CanvasGroup play, CanvasGroup pause)
        {
            _controls = controls;
            _play = play;
            _pause = pause;
        }

        /// <summary>ボタンの文字。言語を切り替えたときに書き直す。</summary>
        public void BindLabels(TMP_Text jump, TMP_Text interact, TMP_Text sprint, TMP_Text quest = null)
        {
            _jumpLabel = jump;
            _interactLabel = interact;
            _sprintLabel = sprint;
            _questLabel = quest;
        }

        /// <summary>
        /// スティックとボタン（ポーズのボタンも）を出すか。指で遊んでいて、会話やメニューで操作を止めていないとき。
        /// ポーズ中は「ゲームに戻る」の行を叩いて閉じる。ボタンを残すと、上の真ん中でポーズと設定の枠に重なる。
        /// </summary>
        public static bool ShowsPlayControls(bool touch, bool gameplayBlocked)
        {
            return touch && !gameplayBlocked;
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

            bool show = ShowsPlayControls(true, KCDInput.GameplayBlocked);
            Show(_play, show);
            Show(_pause, show);
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

            if (_questLabel != null)
            {
                _questLabel.text = L.Pick("クエスト", "Quests");
            }
        }
    }
}
