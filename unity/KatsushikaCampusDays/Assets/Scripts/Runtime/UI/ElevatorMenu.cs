using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// エレベーターの行き先を選ぶ画面 (#275)。<see cref="ElevatorPanel"/> が開く。
    /// 上の階を上に並べ（エレベーターのボタンと同じ向き）、いちばん下に「やめる」を置く。
    /// ↑↓ で行、Enter / Space で決める、Esc で閉じる。指で遊んでいるときは行を叩くとその行に決まる。
    /// 開いている間は時間を止めて操作を封鎖する。今の階を選んだときは閉じるだけ。
    /// </summary>
    public sealed class ElevatorMenu : MonoBehaviour
    {
        /// <summary>1 フレーム分の入力。<see cref="Update"/> が KCDInput から組んで <see cref="Tick"/> に渡す。</summary>
        public readonly struct FrameInput
        {
            public FrameInput(int frame, int vertical = 0, bool submit = false, bool cancel = false, int tap = -1)
            {
                Frame = frame;
                Vertical = vertical;
                Submit = submit;
                Cancel = cancel;
                Tap = tap;
            }

            /// <summary>このフレームの番号（Time.frameCount）。</summary>
            public int Frame { get; }

            /// <summary>上下。下が +1。</summary>
            public int Vertical { get; }

            public bool Submit { get; }

            /// <summary>Esc / 東ボタン。</summary>
            public bool Cancel { get; }

            /// <summary>指で叩いた行（止まる階の番号。「やめる」は階の数）。叩いていなければ -1。</summary>
            public int Tap { get; }
        }

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TmpLinkTap _tap;

        private ElevatorPanel _panel;
        private int _index;
        private int _openedFrame = KCDInput.NoFrame;
        private int _pendingTap = -1;
        private bool _drawnForTouch;
        private float _previousTimeScale = 1f;

        /// <summary>現在のシーンの画面。</summary>
        public static ElevatorMenu Instance { get; private set; }

        /// <summary>開いているか。</summary>
        public bool IsOpen => _panel != null;

        /// <summary>いま選んでいる行。止まる階の番号で、「やめる」は階の数。</summary>
        public int Index => _index;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text body)
        {
            _root = root;
            _body = body;
        }

        /// <summary>行を叩く受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (InputMode.IsTouch != _drawnForTouch)
            {
                Redraw();
            }

            int tap = _pendingTap;
            _pendingTap = -1;
            Tick(new FrameInput(Time.frameCount, KCDInput.MenuVertical, KCDInput.SubmitPressed,
                KCDInput.CancelPressed, tap));
        }

        private void OnTapped(string linkId)
        {
            if (InputMode.IsTouch && IsOpen)
            {
                _pendingTap = TmpLinkTap.ChoiceIn(linkId, _panel.Stops.Length + 1);
            }
        }

        /// <summary>
        /// 開く。ほかの画面が封鎖している間・建物の外・止まる階が 1 つしかないときは開かない。
        /// カーソルは今の階から始める。
        /// </summary>
        public void Open(ElevatorPanel panel)
        {
            if (IsOpen || panel == null || !panel.CanInteract || KCDInput.GameplayBlocked)
            {
                return;
            }

            _panel = panel;
            _index = Mathf.Max(0, panel.CurrentIndex);
            _openedFrame = Time.frameCount;
            _pendingTap = -1;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            KCDInput.Block(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (_root != null)
            {
                _root.SetActive(true);
            }

            AudioManager.Instance?.PlayUi("ui_open");
            Redraw();
        }

        /// <summary>閉じる。止めた時間と自分の封鎖だけを戻す。</summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _panel = null;
            Time.timeScale = _previousTimeScale;
            KCDInput.Unblock(this);
            if (_root != null)
            {
                _root.SetActive(false);
            }

            InputMode.CaptureCursor();

            // 同じ Esc / Enter を PauseMenu や「調べる」が拾わないように。
            KCDInput.MarkModalClosed();
        }

        /// <summary>1 フレーム分の処理。<see cref="Update"/> が毎フレーム呼ぶ。テストから直接呼ぶ。</summary>
        public void Tick(FrameInput input)
        {
            if (!IsOpen)
            {
                return;
            }

            // 開いたフレームの Enter（E と同じく「調べる」でもある）で、そのまま決まってしまわないように。
            if (KCDInput.IgnoresInput(_openedFrame, input.Frame))
            {
                return;
            }

            if (input.Cancel)
            {
                AudioManager.Instance?.PlayUi("ui_close");
                Close();
                return;
            }

            int count = _panel.Stops.Length;
            if (input.Vertical != 0)
            {
                _index = Step(_index, input.Vertical, count);
                AudioManager.Instance?.PlayUi("ui_move");
                Redraw();
            }

            if (input.Tap >= 0 && input.Tap <= count)
            {
                _index = input.Tap;
                Choose();
                return;
            }

            if (input.Submit)
            {
                Choose();
            }
        }

        /// <summary>選んでいる階へ行く。今の階と「やめる」は閉じるだけ（GoTo が今の階と範囲の外を断る）。</summary>
        private void Choose()
        {
            ElevatorPanel panel = _panel;
            int index = _index;
            AudioManager.Instance?.PlayUi("ui_confirm");
            Close();
            panel.GoTo(index);
        }

        /// <summary>画面の上から何行目か。上の階ほど上で、「やめる」（index == count）がいちばん下。</summary>
        public static int RowOf(int index, int count)
        {
            return index >= count ? count : count - 1 - index;
        }

        /// <summary>
        /// 上下でカーソルを 1 つずらした先。step は画面の向き（下が +1）で、端から先は反対の端へ回り込む。
        /// </summary>
        public static int Step(int index, int step, int count)
        {
            int rows = count + 1;
            int row = ((RowOf(index, count) + step) % rows + rows) % rows;
            return row >= count ? count : count - 1 - row;
        }

        private void Redraw()
        {
            if (_body == null || _panel == null)
            {
                return;
            }

            _drawnForTouch = InputMode.IsTouch;
            _body.text = Compose(_panel.Stops, _panel.CurrentIndex, _index, _drawnForTouch);
        }

        /// <summary>
        /// 見出しと階の一覧と「やめる」。今の階には「いまの階」を添え、selected の行にだけ ▶ と色を付ける。
        /// links なら各行を <c>&lt;link="choice:N"&gt;</c> で囲み、叩けるようにする（N は stops の番号、「やめる」は階の数）。
        /// キーの案内は指で遊んでいないときだけ出す。
        /// </summary>
        public static string Compose(ElevatorPanel.Stop[] stops, int current, int selected, bool links)
        {
            var builder = new StringBuilder(256);
            builder.Append("<size=110%>").Append(L.Pick("エレベーター", "Elevator")).Append("</size>\n\n");
            for (int i = stops.Length - 1; i >= 0; i--)
            {
                string label = stops[i].Floor;
                string caption = L.Pick(stops[i].CaptionJa, stops[i].CaptionEn);
                if (!string.IsNullOrEmpty(caption))
                {
                    label += "  " + caption;
                }

                if (i == current)
                {
                    label += L.Pick("（いまの階）", " (this floor)");
                }

                AppendRow(builder, i, label, i == selected, links);
            }

            builder.Append('\n');
            AppendRow(builder, stops.Length, L.Pick("やめる", "Cancel"), selected == stops.Length, links);
            if (!links)
            {
                builder.Append("\n<size=75%>").Append(L.Pick("↑↓ えらぶ　Enter 決める　Esc やめる",
                    "Up/Down choose   Enter go   Esc cancel")).Append("</size>");
            }

            return builder.ToString();
        }

        private static void AppendRow(StringBuilder builder, int index, string label, bool selected, bool links)
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
    }
}
