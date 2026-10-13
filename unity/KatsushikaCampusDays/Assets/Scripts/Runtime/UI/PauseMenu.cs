using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Esc で開くメニュー。再開 / セーブ / ロード / 設定 / タイトルへ を上下で選ぶ。タイトルへは確認してから戻る。
    /// ボタンイベントに頼らずキーボードだけで完結させ、シーン内参照の配線を減らしている。
    /// 指で遊んでいるときは、行を叩くとその行に決まる（確認の「はい」「いいえ」も同じ, #187）。
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
                bool consumed = false, bool cancel = false, int tap = -1)
            {
                Frame = frame;
                Menu = menu;
                Submit = submit;
                Vertical = vertical;
                Consumed = consumed;
                Cancel = cancel;
                Tap = tap;
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

            /// <summary>戻る（<see cref="KCDInput.CancelPressed"/>。Esc / 東ボタン）。「タイトルへ戻る」の確認でだけ使う。</summary>
            public bool Cancel { get; }

            /// <summary>指で叩いた行の番号。叩いていなければ -1。確認の間は 0 =「はい」、1 =「いいえ」。</summary>
            public int Tap { get; }
        }

        /// <summary>「タイトルへ戻る」の確認の選択肢の位置。上が「はい」、下が「いいえ」。</summary>
        private const int ConfirmYes = 0;

        private const int ConfirmNo = 1;

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
        [SerializeField] private TmpLinkTap _tap;

        private int _index;

        /// <summary>叩いた行の番号。次の <see cref="Update"/> が <see cref="FrameInput.Tap"/> にして渡す。無ければ -1。</summary>
        private int _pendingTap = -1;

        /// <summary>叩いたときに確認を出していたか。Update までに一覧と確認が入れ替わっていたら、叩いた行を捨てる。</summary>
        private bool _pendingTapConfirming;

        /// <summary>行を叩けるリンク付きで書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _drawnForTouch;

        /// <summary>時間を止めて封鎖を掛けているか。設定パネルを出している間（本体は隠れている）も true。</summary>
        private bool _paused;

        /// <summary>
        /// 「設定」を Enter で選んだフレーム。設定パネルはこの次のフレームで開く (#103)。
        /// 選んでいなければ <see cref="KCDInput.NoFrame"/>。
        /// </summary>
        private int _settingsRequestedFrame = KCDInput.NoFrame;

        /// <summary>「タイトルへ戻る」の確認を出しているか (#104)。</summary>
        private bool _confirmingTitle;

        /// <summary>確認でカーソルのある選択肢。出すたびに「いいえ」から始める（進行を消さない側）。</summary>
        private int _confirmIndex = ConfirmNo;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>「タイトルへ戻る」を決めたあとの確認（セーブしていない進行が消える）を出しているか。</summary>
        public bool IsConfirmingReturnToTitle => _confirmingTitle;

        /// <summary>
        /// 確認で「はい」を決めたときに呼ぶ処理。null なら GameManager.ReturnToTitle を呼ぶ。テストから差し込む。
        /// </summary>
        public System.Action ReturnToTitleAction { get; set; }

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

        /// <summary>行を叩く受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void Start()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        /// <summary>開いたまま無効にされたり、シーンごと消されたりしても、止めた時間と封鎖を残さない (#62)。</summary>
        private void OnDisable()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }

            Abandon();
        }

        /// <summary>
        /// 開いたまま片付けられるときの後始末。自分が止めた時間と自分の封鎖だけを戻す。
        /// 開いていなければ何もしない（ほかの画面が止めた timeScale を 1 に戻さない）。
        /// 選んだだけでまだ開いていない設定パネルと、出していた「タイトルへ戻る」の確認は、開いていてもいなくても捨てる。
        /// 音とカーソルは次の画面に任せる。<see cref="OnDisable"/> が呼ぶ。
        /// </summary>
        public void Abandon()
        {
            _settingsRequestedFrame = KCDInput.NoFrame;
            _confirmingTitle = false;
            _pendingTap = -1;
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
            if (IsOpen && InputMode.IsTouch != _drawnForTouch)
            {
                Redraw();
            }

            QuestLogView log = HUD.Instance != null ? HUD.Instance.QuestLogView : null;
            int frame = Time.frameCount;
            int tap = _pendingTapConfirming == _confirmingTitle ? _pendingTap : -1;
            _pendingTap = -1;
            Tick(new FrameInput(
                frame,
                menu: KCDInput.MenuPressed,
                submit: KCDInput.SubmitPressed,
                vertical: KCDInput.MenuVertical,
                consumed: KCDInput.ModalClosedThisFrame || (log != null && log.ClosedByMenuFrame == frame),
                cancel: KCDInput.CancelPressed,
                tap: tap));
        }

        /// <summary>
        /// 叩いたリンクを次の Update に預ける。マウスのクリックは TmpLinkTap が捨て、ここでは指で遊んでいるときだけ受ける。
        /// 行の外を叩いたときは何もしない。
        /// </summary>
        private void OnTapped(string linkId)
        {
            if (!InputMode.IsTouch || !IsOpen)
            {
                return;
            }

            _pendingTap = TapIndex(linkId, _confirmingTitle);
            _pendingTapConfirming = _confirmingTitle;
        }

        /// <summary>叩いたリンクの行の番号。確認の間は 0 =「はい」、1 =「いいえ」。行の外や形の違う id は -1。</summary>
        public static int TapIndex(string linkId, bool confirming)
        {
            return TmpLinkTap.ChoiceIn(linkId, confirming ? 2 : EntryKeys.Length);
        }

        /// <summary>1 フレーム分の処理。<see cref="Update"/> が毎フレーム呼ぶ。テストから直接呼ぶ。</summary>
        public void Tick(FrameInput input)
        {
            // タイトルへ戻る読み込みの間もキャンパスは残っていて、時間を止めてある。ここで開閉させると、
            // 閉じたときの SetOpen(false) が Time.timeScale = 1 に戻して読み込みの裏で時計が進む (#15)。
            if (SceneLoader.IsAnyLoading)
            {
                return;
            }

            // DormEnding / FormalEnding.IsAnyShowing: 裏エンド (#41) と本編のエンディング (#176) の最中は Esc を食わせない。
            // 食わせると暗転の裏でポーズが開き、閉じたときの SetOpen(false) が
            // Time.timeScale = 1 に戻してしまう（裏エンドの最中に時間が動き出す）。
            if (IgnoresMenuKey(KCDInput.PhotoMode, input.Consumed, ResultScreen.IsAnyOpen,
                    DormEnding.IsAnyShowing || FormalEnding.IsAnyShowing, _settings != null && _settings.IsOpen))
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

            // 「タイトルへ戻る」の確認の間は、確認だけがキーを使う (#104)。Esc もポーズを閉じずに一覧へ戻す。
            if (_confirmingTitle && IsOpen)
            {
                TickTitleConfirm(input);
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
                _index = MoveCursor(_index, step);
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (input.Tap >= 0 && input.Tap < EntryKeys.Length)
            {
                _index = input.Tap;
                Redraw();
                AudioManager.Instance?.PlayUi("ui_confirm");
                Execute(_index, input.Frame);
                return;
            }

            if (input.Submit)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Execute(_index, input.Frame);
            }
        }

        /// <summary>メニューの項目数（再開 / セーブ / ロード / 設定 / タイトルへ）。</summary>
        public static int EntryCount => EntryKeys.Length;

        /// <summary>
        /// Esc（メニューキー）を見ないか。写真モード中、モーダル（会話・クエストログ）を閉じたそのフレーム、結果画面・寮の締めの表示中、
        /// ポーズから開いた設定パネルの間は、ポーズを開きも閉じもしない。
        /// </summary>
        public static bool IgnoresMenuKey(bool photoMode, bool modalClosedThisFrame, bool resultOpen,
            bool dormEndingShowing, bool settingsOpen)
        {
            return photoMode || modalClosedThisFrame || resultOpen || dormEndingShowing || settingsOpen;
        }

        /// <summary>上下でカーソルを 1 つずらした先。端から先へ進むと反対の端に回り込む。</summary>
        public static int MoveCursor(int index, int step)
        {
            return (index + step + EntryKeys.Length) % EntryKeys.Length;
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

            // 開き直しても閉じても、選んだだけでまだ開いていない設定パネルと、出していた確認と、叩いた行は持ち越さない。
            _settingsRequestedFrame = KCDInput.NoFrame;
            _confirmingTitle = false;
            _pendingTap = -1;
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

            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                InputMode.CaptureCursor();
            }

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

            _drawnForTouch = InputMode.IsTouch;
            _bodyLabel.text = Compose(_confirmingTitle, _confirmingTitle ? _confirmIndex : _index, _drawnForTouch);
        }

        /// <summary>
        /// 本体の文面。確認の間は問いと「はい」「いいえ」、それ以外は項目の一覧。selected の行にだけ ▶ と色を付ける。
        /// links なら各行を <c>&lt;link="choice:N"&gt;</c> で囲み、叩けるようにする。
        /// </summary>
        public static string Compose(bool confirming, int selected, bool links)
        {
            var builder = new System.Text.StringBuilder(256);
            if (confirming)
            {
                // 全角 28 字は文字の大きさ 30 のままだと約 840 になり、ラベルの内側の幅 784 を越えて末尾だけ次の行に落ちる。
                // 85% (25.5) なら約 714 で 1 行に収まる。
                builder.Append("<size=85%>");
                builder.Append(L.Get("ui.pause.confirm_title", "セーブしていない進行は失われます。タイトルへ戻りますか？"));
                builder.Append("</size>\n\n");
                AppendEntry(builder, ConfirmYes, L.Get("ui.common.yes", "はい"), selected == ConfirmYes, links);
                AppendEntry(builder, ConfirmNo, L.Get("ui.common.no", "いいえ"), selected == ConfirmNo, links);
            }
            else
            {
                for (int i = 0; i < EntryKeys.Length; i++)
                {
                    AppendEntry(builder, i, L.Get(EntryKeys[i], EntryFallbacks[i]), i == selected, links);
                }
            }

            return builder.ToString();
        }

        private static void AppendEntry(System.Text.StringBuilder builder, int index, string label, bool selected,
            bool links)
        {
            if (links)
            {
                builder.Append("<link=\"choice:").Append(index).Append("\">");
            }

            builder.Append(selected ? "<color=#FFD98A>▶ " : "   ");
            builder.Append(label);
            builder.Append(selected ? "</color>" : string.Empty);
            if (links)
            {
                builder.Append("</link>");
            }

            builder.Append('\n');
        }

        /// <summary>
        /// 「タイトルへ戻る」の確認の 1 フレーム分 (#104)。上下で「はい」「いいえ」を選び、Enter で決める。
        /// Esc / 戻るは「いいえ」と同じで、ポーズの一覧に戻る（ポーズは閉じない）。
        /// </summary>
        private void TickTitleConfirm(FrameInput input)
        {
            if (input.Menu || input.Cancel)
            {
                EndTitleConfirm();
                return;
            }

            if (input.Tap == ConfirmYes || input.Tap == ConfirmNo)
            {
                _confirmIndex = input.Tap;
                ConfirmChoice();
                return;
            }

            if (input.Vertical != 0)
            {
                // 選択肢は 2 つなので、上でも下でももう片方へ移る。
                _confirmIndex = _confirmIndex == ConfirmYes ? ConfirmNo : ConfirmYes;
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (input.Submit)
            {
                ConfirmChoice();
            }
        }

        /// <summary>確認でカーソルのある方に決める。「いいえ」なら一覧へ戻り、「はい」ならタイトルへ戻る。</summary>
        private void ConfirmChoice()
        {
            if (_confirmIndex != ConfirmYes)
            {
                EndTitleConfirm();
                return;
            }

            AudioManager.Instance?.PlayUi("ui_confirm");
            SetOpen(false);
            if (ReturnToTitleAction != null)
            {
                ReturnToTitleAction();
            }
            else
            {
                GameManager.Instance.ReturnToTitle();
            }
        }

        /// <summary>確認を消してポーズの一覧に戻る。カーソルは「タイトルへ戻る」の行のまま。</summary>
        private void EndTitleConfirm()
        {
            _confirmingTitle = false;
            Redraw();
            AudioManager.Instance?.PlayUi("ui_close");
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
                    // すぐには戻らない。ReturnToTitle はセーブを書かずにシーンを読み替えるので、
                    // セーブしていない進行が消えることを伝えて確かめる (#104)。戻るのは TickTitleConfirm の「はい」。
                    _confirmingTitle = true;
                    _confirmIndex = ConfirmNo;
                    Redraw();
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
