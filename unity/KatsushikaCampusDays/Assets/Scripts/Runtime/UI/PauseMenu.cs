using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Esc で開くメニュー。再開 / セーブ / ロード / タイトルへ を上下で選ぶ。
    /// ボタンイベントに頼らずキーボードだけで完結させ、シーン内参照の配線を減らしている。
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        /// <summary>
        /// 1 フレーム分の入力。<see cref="Update"/> が KCDInput から組んで <see cref="Tick"/> に渡す。
        /// EditMode では Update が走らず Time.frameCount も進まないので、テストはこれを直接組んで渡す。
        /// </summary>
        public readonly struct FrameInput
        {
            public FrameInput(int frame, bool menu = false, bool submit = false, int vertical = 0,
                bool consumed = false)
            {
                Frame = frame;
                Menu = menu;
                Submit = submit;
                Vertical = vertical;
                Consumed = consumed;
            }

            /// <summary>このフレームの番号（Time.frameCount）。</summary>
            public int Frame { get; }

            /// <summary>Esc / Start（<see cref="KCDInput.MenuPressed"/>）。</summary>
            public bool Menu { get; }

            /// <summary>Enter / Space / 南ボタン（<see cref="KCDInput.SubmitPressed"/>）。</summary>
            public bool Submit { get; }

            /// <summary>上下（<see cref="KCDInput.MenuVertical"/>。下が +1）。</summary>
            public int Vertical { get; }

            /// <summary>
            /// このフレームのキーをほかの画面がもう使ったか。モーダルを閉じた（<see cref="KCDInput.ModalClosedThisFrame"/>）か、
            /// Esc でクエストログを閉じた（<see cref="QuestLogView.ClosedByMenuFrame"/>）。
            /// </summary>
            public bool Consumed { get; }
        }

        private static readonly string[] EntryKeys =
        {
            "ui.pause.resume", "ui.pause.save", "ui.pause.load", "ui.pause.settings", "ui.pause.title"
        };

        private static readonly string[] EntryFallbacks =
        {
            "ゲームに戻る", "セーブする", "ロードする", "設定", "タイトルへ戻る"
        };

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private SettingsView _settings;

        private int _index;

        /// <summary>時間を止めて封鎖を掛けているか。設定パネルを出している間（本体は隠れている）も true。</summary>
        private bool _paused;

        /// <summary>
        /// 「設定」を Enter で選んだフレーム。設定パネルはこの次のフレームで開く (#103)。
        /// 選んでいなければ <see cref="KCDInput.NoFrame"/>。
        /// </summary>
        private int _settingsRequestedFrame = KCDInput.NoFrame;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>
        /// ポーズで時間を止めているか。<see cref="IsOpen"/> と違い、ポーズから開いた設定パネルの間も true。
        /// </summary>
        public bool IsPaused => _paused;

        /// <summary>設定パネル。SceneBuilder が差し込む。</summary>
        public SettingsView Settings
        {
            get => _settings;
            set => _settings = value;
        }

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text body)
        {
            _root = root;
            _bodyLabel = body;
        }

        private void Start()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>開いたまま無効にされたり、シーンごと消されたりしても、止めた時間と封鎖を残さない (#62)。</summary>
        private void OnDisable()
        {
            Abandon();
        }

        /// <summary>
        /// 開いたまま片付けられるときの後始末。自分が止めた時間と自分の封鎖だけを戻す。
        /// 開いていなければ何もしない（ほかの画面が止めた timeScale を 1 に戻さない）。
        /// 音とカーソルは次の画面に任せる。<see cref="OnDisable"/> が呼ぶ。
        /// </summary>
        public void Abandon()
        {
            _settingsRequestedFrame = KCDInput.NoFrame;
            if (!_paused)
            {
                return;
            }

            _paused = false;
            Time.timeScale = 1f;
            KCDInput.Unblock(this);
        }

        /// <summary>
        /// Esc / Start を受けてよいか (#105)。開いているポーズを閉じるのはいつでもできる。
        /// 開くのは、自分以外の封鎖（会話・建物の出入りや翌朝への暗転・写真モード・クエストログ・落下からの復帰など）が
        /// 無いときだけ。自分の封鎖は数えない（ポーズから開いた設定パネルの間は、本体を隠したまま自分で封鎖している）。
        /// </summary>
        public static bool AcceptsMenu(bool isOpen, bool blocked, bool blockedBySelf)
        {
            return isOpen || !blocked || blockedBySelf;
        }

        private void Update()
        {
            QuestLogView log = HUD.Instance != null ? HUD.Instance.QuestLogView : null;
            int frame = Time.frameCount;
            Tick(new FrameInput(
                frame,
                menu: KCDInput.MenuPressed,
                submit: KCDInput.SubmitPressed,
                vertical: KCDInput.MenuVertical,
                consumed: KCDInput.ModalClosedThisFrame || (log != null && log.ClosedByMenuFrame == frame)));
        }

        /// <summary>1 フレーム分の処理。<see cref="Update"/> が毎フレーム呼ぶ。テストから直接呼ぶ。</summary>
        public void Tick(FrameInput input)
        {
            // DormEnding.IsAnyShowing: 裏エンド (#41) の暗転中と結果表示中は Esc を食わせない。
            // 食わせると暗転の裏でポーズが開き、閉じたときの SetOpen(false) が
            // Time.timeScale = 1 に戻してしまう（裏エンドの最中に時間が動き出す）。
            if (KCDInput.PhotoMode || input.Consumed || ResultScreen.IsAnyOpen ||
                DormEnding.IsAnyShowing || (_settings != null && _settings.IsOpen))
            {
                return;
            }

            // 「設定」を選んだ Enter のフレームでは設定パネルを開かず、次のフレームで開く (#103)。
            // 同じフレームに開くと、そのフレームのうちに SettingsView.Update（同じキャンバスで PauseMenu の後に付いている）が
            // 同じ Enter を拾い、最初の行（BGM）を +10% して PlayerPrefs に保存していた。
            // 次のフレームなら、その Enter はもう届かない（Update の順に関係ない）。
            if (_settingsRequestedFrame != KCDInput.NoFrame)
            {
                if (!KCDInput.IgnoresInput(_settingsRequestedFrame, input.Frame))
                {
                    _settingsRequestedFrame = KCDInput.NoFrame;
                    OpenSettings();
                }

                return;
            }

            // ほかの仕組みが封鎖している間は、閉じたポーズを開かない (#105)。
            // 建物の出入り（InteriorLoader.Travel）と翌朝への戻り（DayEndEvaluator.RestartDay）の暗転は
            // Block(this) を掛けるだけで上のフラグを立てないので、ここで見ないと暗転の裏でポーズが開く。
            // 会話を閉じる Esc も、DialogueSystem.Update がこれより後に走るフレームでは Finish 前の封鎖が残っているので
            // ここで止まり、先に走るフレームでは上の ModalClosedThisFrame で止まる。どちらの順でも、1 回の Esc で
            // 会話を閉じてポーズまで開くことはない。クエストログを開いている間もログ自身の封鎖で止まる。
            if (!AcceptsMenu(IsOpen, KCDInput.GameplayBlocked, KCDInput.IsBlockedBy(this)))
            {
                return;
            }

            if (input.Menu)
            {
                SetOpen(!IsOpen);
                return;
            }

            if (!IsOpen)
            {
                return;
            }

            int step = input.Vertical;
            if (step != 0)
            {
                _index = (_index + step + EntryKeys.Length) % EntryKeys.Length;
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (input.Submit)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Execute(_index, input.Frame);
            }
        }

        /// <summary>開閉する。開いている間は時間を止め、自分の名前で操作を封鎖する。</summary>
        public void SetOpen(bool open)
        {
            if (_root == null)
            {
                return;
            }

            if (open != _root.activeSelf)
            {
                AudioManager.Instance?.PlayUi(open ? "ui_open" : "ui_close");
            }

            // 開き直しても閉じても、選んだだけでまだ開いていない設定パネルは持ち越さない。
            _settingsRequestedFrame = KCDInput.NoFrame;
            _root.SetActive(open);
            Time.timeScale = open ? 0f : 1f;
            _paused = open;

            // 自分の封鎖だけを掛け外しする。共有の GameplayBlocked への代入だと、ポーズ中に開いた
            // クエストログを閉じたときにポーズの封鎖まで外れていた (#62)。
            if (open)
            {
                KCDInput.Block(this);
            }
            else
            {
                KCDInput.Unblock(this);
            }

            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

            if (open)
            {
                _index = 0;
                Redraw();
            }
        }

        private void Redraw()
        {
            if (_bodyLabel == null)
            {
                return;
            }

            var builder = new System.Text.StringBuilder(256);
            for (int i = 0; i < EntryKeys.Length; i++)
            {
                builder.Append(i == _index ? "<color=#FFD98A>▶ " : "   ");
                builder.Append(L.Get(EntryKeys[i], EntryFallbacks[i]));
                builder.Append(i == _index ? "</color>\n" : "\n");
            }

            _bodyLabel.text = builder.ToString();
        }

        /// <summary>選んだ行を実行する。frame は決めた Enter のフレーム。</summary>
        private void Execute(int index, int frame)
        {
            switch (index)
            {
                case 0:
                    SetOpen(false);
                    break;
                case 1:
                {
                    // 書けなかったのに「セーブしました」と出すと、次に遊ぶときまで気づけない (#61)。
                    bool saved = SaveSystem.Save();
                    HUD.Instance?.ShowToast(saved
                        ? L.Get("ui.hud.saved", "セーブしました")
                        : L.Get("ui.hud.save_failed", "セーブできませんでした"));
                    SetOpen(false);
                    break;
                }
                case 2:
                    SetOpen(false);
                    SaveSystem.Load();
                    break;
                case 3:
                    // ここでは開かない。次のフレームの Tick が開く (#103)。
                    _settingsRequestedFrame = frame;
                    break;
                case 4:
                    SetOpen(false);
                    GameManager.Instance.ReturnToTitle();
                    break;
            }
        }

        /// <summary>設定を開く。時間停止と入力ブロックは保ったまま、メニュー本体だけ隠す。</summary>
        private void OpenSettings()
        {
            if (_settings == null)
            {
                SetOpen(false);
                return;
            }

            _root.SetActive(false);
            _settings.Open(() =>
            {
                if (_root != null)
                {
                    _root.SetActive(true);
                }

                Redraw();
            });
        }
    }
}
