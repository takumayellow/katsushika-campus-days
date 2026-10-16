using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// キャンパスのタッチの操作 UI (#187)。指で遊んでいる間だけ出す（<see cref="InputMode"/>）。
    /// マウスに戻ったら丸ごと止める。仮想のゲームパッドは最後の OnScreenControl が止まると消えるので、
    /// PC でゲームパッドを使う人の <c>Gamepad.current</c> を取らない。
    /// 会話・メニューの間はスティックとボタンを隠す。写真モードの間は、動かす・向ける・撮る・戻るのボタンだけ出す。
    /// </summary>
    public sealed class TouchHud : MonoBehaviour
    {
        /// <summary>どのボタンを出すか。</summary>
        public enum Layout
        {
            Hidden,
            Play,
            Photo,
        }

        [SerializeField] private GameObject _controls;
        [SerializeField] private CanvasGroup _play;
        [SerializeField] private CanvasGroup _extras;
        [SerializeField] private CanvasGroup _pause;
        [SerializeField] private TMP_Text _jumpLabel;
        [SerializeField] private TMP_Text _interactLabel;
        [SerializeField] private TMP_Text _sprintLabel;
        [SerializeField] private TMP_Text _questLabel;
        [SerializeField] private TMP_Text _photoLabel;

        private Layout _shown = Layout.Hidden;

        /// <summary>
        /// SceneBuilder から差し込む。controls は既定で止めておく（PC で仮想のゲームパッドを作らない）。
        /// play はスティック・なぞる面・ジャンプ・写真のボタン、extras はその中の写真モードで隠すボタン。
        /// </summary>
        public void Bind(GameObject controls, CanvasGroup play, CanvasGroup extras, CanvasGroup pause)
        {
            _controls = controls;
            _play = play;
            _extras = extras;
            _pause = pause;
        }

        /// <summary>ボタンの文字。言語を切り替えたときと、写真モードに出入りしたときに書き直す。</summary>
        public void BindLabels(TMP_Text jump, TMP_Text interact, TMP_Text sprint, TMP_Text quest, TMP_Text photo)
        {
            _jumpLabel = jump;
            _interactLabel = interact;
            _sprintLabel = sprint;
            _questLabel = quest;
            _photoLabel = photo;
        }

        /// <summary>
        /// スティックとボタン（ポーズのボタンも）を出すか。指で遊んでいて、会話やメニューで操作を止めていないとき。
        /// ポーズ中は「ゲームに戻る」の行を叩いて閉じる。ボタンを残すと、上の真ん中でポーズと設定の枠に重なる。
        /// </summary>
        public static bool ShowsPlayControls(bool touch, bool gameplayBlocked)
        {
            return touch && !gameplayBlocked;
        }

        /// <summary>
        /// 出すボタン。写真モードの間は、ジャンプのボタンが撮影、写真のボタンが戻るになる。
        /// 撮影の瞬間は写真に写らないよう全部隠す（写真は画面をそのまま写す）。
        /// </summary>
        public static Layout LayoutFor(bool touch, bool gameplayBlocked, bool photoMode, bool capturing)
        {
            if (!touch || capturing)
            {
                return Layout.Hidden;
            }

            if (photoMode)
            {
                return Layout.Photo;
            }

            return ShowsPlayControls(true, gameplayBlocked) ? Layout.Play : Layout.Hidden;
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

        // 撮影はその Update で始まり、同じフレームの描画のあとに画面を写す。LateUpdate なら、その描画の前に隠せる。
        private void LateUpdate()
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

            PhotoSystem photo = PhotoSystem.Instance;
            Layout layout = LayoutFor(true, KCDInput.GameplayBlocked, KCDInput.PhotoMode,
                photo != null && photo.IsCapturing);
            Show(_play, layout != Layout.Hidden);
            Show(_extras, layout == Layout.Play);
            Show(_pause, layout == Layout.Play);
            if (layout != _shown)
            {
                _shown = layout;
                RefreshLabels();
            }
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
                _jumpLabel.text = _shown == Layout.Photo ? L.Pick("撮影", "Shoot") : L.Pick("ジャンプ", "Jump");
            }

            if (_photoLabel != null)
            {
                _photoLabel.text = _shown == Layout.Photo ? L.Pick("戻る", "Back") : L.Pick("写真", "Photo");
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
