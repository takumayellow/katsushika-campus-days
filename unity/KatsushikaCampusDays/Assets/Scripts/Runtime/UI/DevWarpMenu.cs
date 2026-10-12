using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 開発者用のワープメニュー。<see cref="DevMode"/> が有効なとき（エディタ・Development Build・URL の ?dev=1）だけ生まれ、
    /// F2 で開く。開いている間は PauseMenu と同じく時間を止めて操作を封鎖する。
    /// ↑↓ で項目、←→ で見出し（場所 / 建物の中 / 人 / 時刻）、Enter で移動、F2 か Esc で閉じる。マウスは使わない。
    /// 屋内にいるときにキャンパスの場所を選ぶと、先に外へ出てからワープする。
    /// </summary>
    public sealed class DevWarpMenu : MonoBehaviour
    {
        /// <summary>1 フレーム分の入力。<see cref="Update"/> が KCDInput から組んで <see cref="Tick"/> に渡す。</summary>
        public readonly struct FrameInput
        {
            public FrameInput(bool toggle = false, int vertical = 0, int horizontal = 0, bool submit = false,
                bool cancel = false)
            {
                Toggle = toggle;
                Vertical = vertical;
                Horizontal = horizontal;
                Submit = submit;
                Cancel = cancel;
            }

            /// <summary>F2（<see cref="KCDInput.DevMenuPressed"/>）。</summary>
            public bool Toggle { get; }

            /// <summary>上下。下が +1。</summary>
            public int Vertical { get; }

            /// <summary>左右。右が +1。</summary>
            public int Horizontal { get; }

            public bool Submit { get; }

            public bool Cancel { get; }
        }

        /// <summary>一度に見せる行の数。</summary>
        public const int VisibleRows = 12;

        /// <summary>出入りの暗転が終わるのを待つ上限（実時間の秒）。</summary>
        private const float TravelTimeoutSeconds = 6f;

        private static DevWarpMenu _instance;

        private GameObject _panel;
        private TMP_Text _body;
        private DevWarpCatalog _catalog;
        private readonly int[] _index = new int[4];
        private int _tab;
        private bool _open;
        private float _previousTimeScale = 1f;
        private bool _traveling;

        /// <summary>メニューを出している間 true。</summary>
        public bool IsOpen => _open;

        /// <summary>いまの見出し（<see cref="DevWarpKind"/> の順）。</summary>
        public int Tab => _tab;

        /// <summary>いまの見出しで選んでいる行。</summary>
        public int Index => _index[_tab];

        /// <summary>いま開いている一覧。閉じていれば null。</summary>
        public DevWarpCatalog Catalog => _catalog;

        /// <summary>移動の処理（暗転・外へ出る・ワープ）の最中か。</summary>
        public bool IsTraveling => _traveling;

        /// <summary>現在のシーンのメニュー。無効なとき（公開版）は null。</summary>
        public static DevWarpMenu Instance => _instance;

        /// <summary>メニューの画面を載せる Canvas。画像を撮るテストが描画先のカメラを差し替える。</summary>
        public Canvas Canvas { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!DevMode.Enabled || _instance != null)
            {
                return;
            }

            var go = new GameObject("DevWarpMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<DevWarpMenu>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            BuildUi();
            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Release();
                _instance = null;
            }
        }

        private void OnDisable()
        {
            Release();
        }

        private void Update()
        {
            Tick(new FrameInput(
                toggle: KCDInput.DevMenuPressed,
                vertical: KCDInput.MenuVertical,
                horizontal: KCDInput.MenuHorizontal,
                submit: KCDInput.SubmitPressed,
                cancel: KCDInput.CancelPressed));
        }

        /// <summary>1 フレーム分の処理。<see cref="Update"/> が毎フレーム呼ぶ。テストから直接呼ぶ。</summary>
        public void Tick(FrameInput input)
        {
            if (_open && SceneManager.GetActiveScene().name != GameManager.CampusSceneName)
            {
                Close();
                return;
            }

            if (_traveling)
            {
                return;
            }

            if (!_open)
            {
                if (input.Toggle)
                {
                    Open();
                }

                return;
            }

            if (input.Toggle || input.Cancel)
            {
                Close();
                return;
            }

            if (input.Horizontal != 0)
            {
                _tab = (_tab + input.Horizontal + DevWarpCatalog.TabCount) % DevWarpCatalog.TabCount;
                Redraw();
            }

            int count = _catalog.Tab(_tab).Count;
            if (input.Vertical != 0 && count > 0)
            {
                _index[_tab] = (_index[_tab] + input.Vertical + count) % count;
                Redraw();
            }

            if (input.Submit && count > 0)
            {
                Execute(_catalog.Tab(_tab)[_index[_tab]]);
            }
        }

        /// <summary>
        /// 開けるか。キャンパスにいて、会話・ポーズ・写真モード・結果画面などの封鎖が無いときだけ。
        /// 公開版（<see cref="DevMode.Enabled"/> が false）ではそもそも生まれない。
        /// </summary>
        public static bool CanOpen()
        {
            return DevMode.Enabled
                   && SceneManager.GetActiveScene().name == GameManager.CampusSceneName
                   && !SceneLoader.IsAnyLoading
                   && !KCDInput.GameplayBlocked
                   && !KCDInput.PhotoMode
                   && !ResultScreen.IsAnyOpen
                   && !DormEnding.IsAnyShowing
                   && !FormalEnding.IsAnyShowing;
        }

        /// <summary>開く。開けない状況なら何もしない。</summary>
        public void Open()
        {
            if (_open || !CanOpen())
            {
                return;
            }

            _catalog = DevWarpCatalog.CollectFromScene();
            _open = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            KCDInput.Block(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            for (int i = 0; i < _index.Length; i++)
            {
                _index[i] = Mathf.Clamp(_index[i], 0, Mathf.Max(0, _catalog.Tab(i).Count - 1));
            }

            _panel.SetActive(true);
            Redraw();
        }

        /// <summary>閉じる。止めた時間と自分の封鎖だけを戻す。</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            Release();
            _panel.SetActive(false);
            InputMode.CaptureCursor();

            // 同じ Esc を PauseMenu が拾ってポーズを開かないように。
            KCDInput.MarkModalClosed();
        }

        private void Release()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Time.timeScale = _previousTimeScale;
            KCDInput.Unblock(this);
        }

        private void Execute(DevWarpTarget target)
        {
            Close();
            _traveling = true;
            StartCoroutine(Run(target));
        }

        /// <summary>移動の最中を <see cref="IsTraveling"/> で示す。途中で壊れても立てたままにしない。</summary>
        private IEnumerator Run(DevWarpTarget target)
        {
            try
            {
                yield return Go(target);
            }
            finally
            {
                _traveling = false;
            }
        }

        private IEnumerator Go(DevWarpTarget target)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            InteriorLoader loader = InteriorLoader.Instance;
            if (target.Kind == DevWarpKind.Time)
            {
                DayNightCycle cycle = FindAnyObjectByType<DayNightCycle>();
                if (cycle != null)
                {
                    cycle.SetHours(target.Hours);
                }

                yield break;
            }

            if (player == null)
            {
                Debug.LogWarning("[KCD] ワープ: プレイヤーが見つかりません: " + target.Id);
                yield break;
            }

            // 同じ建物の中を選んだときは、そのまま。ほかの行き先は外へ出てから。
            if (target.Kind == DevWarpKind.Interior && loader != null && loader.CurrentId == target.Id)
            {
                yield break;
            }

            if (loader != null && loader.IsInside)
            {
                loader.Exit();
                yield return WaitForOutside(loader);
                if (loader.IsInside)
                {
                    Debug.LogWarning("[KCD] ワープ: 外へ出られません: " + target.Id);
                    yield break;
                }
            }

            if (target.HasPosition)
            {
                Vector3 stand = SnapToGround(target.Position);
                player.Teleport(stand, target.Yaw);
                Physics.SyncTransforms();
                CameraRig.SnapBehind(player.transform);
            }

            if (target.Kind == DevWarpKind.Interior)
            {
                if (loader == null || !loader.HasInterior(target.Id))
                {
                    Debug.LogWarning("[KCD] ワープ: 屋内に入れません: " + target.Id);
                    yield break;
                }

                // 物理の後始末を 1 フレーム待ってから入る（入口を踏んだ判定と競合しない）。
                yield return null;
                loader.Enter(target.Id);
                yield return WaitForInside(loader, target.Id);
            }
        }

        private static IEnumerator WaitForOutside(InteriorLoader loader)
        {
            float until = Time.realtimeSinceStartup + TravelTimeoutSeconds;
            while ((loader.IsInside || loader.IsBusy) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForInside(InteriorLoader loader, string id)
        {
            float until = Time.realtimeSinceStartup + TravelTimeoutSeconds;
            while ((loader.CurrentId != id || loader.IsBusy) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        /// <summary>stand の少し上から真下へ撃って、足元の地面に立たせる。当たらなければそのまま。</summary>
        private static Vector3 SnapToGround(Vector3 stand)
        {
            if (Physics.Raycast(stand + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                stand.y = hit.point.y + 0.05f;
            }

            return stand;
        }

        private void Redraw()
        {
            if (_body != null && _catalog != null)
            {
                _body.text = Compose(_catalog, _tab, _index[_tab]);
            }
        }

        /// <summary>見出しの行と、選んでいる行を中心にした <see cref="VisibleRows"/> 行ぶんの文面。</summary>
        public static string Compose(DevWarpCatalog catalog, int tab, int selected)
        {
            var builder = new StringBuilder(1024);
            builder.Append("<size=120%>").Append(L.Get("ui.dev.heading", "開発者用ワープ")).Append("</size>\n");

            string[] names =
            {
                L.Get("ui.dev.tab.place", "場所"), L.Get("ui.dev.tab.interior", "建物の中"),
                L.Get("ui.dev.tab.npc", "人"), L.Get("ui.dev.tab.time", "時刻")
            };
            for (int i = 0; i < names.Length; i++)
            {
                string title = names[i] + " " + catalog.Tab(i).Count;
                builder.Append(i == tab ? "<color=#FFD54A>[" + title + "]</color>" : " " + title + " ");
                builder.Append("  ");
            }

            builder.Append("\n\n");

            System.Collections.Generic.IReadOnlyList<DevWarpTarget> rows = catalog.Tab(tab);
            if (rows.Count == 0)
            {
                builder.Append(L.Get("ui.dev.empty", "（行き先がありません）")).Append('\n');
            }

            int first = FirstVisibleRow(rows.Count, selected);
            int last = Mathf.Min(rows.Count, first + VisibleRows);
            for (int i = first; i < last; i++)
            {
                if (i == selected)
                {
                    builder.Append("<color=#FFD54A>▶ ").Append(rows[i].Label).Append("</color>\n");
                }
                else
                {
                    builder.Append("   ").Append(rows[i].Label).Append('\n');
                }
            }

            builder.Append("\n<size=80%>").Append(L.Get("ui.dev.hint",
                "↑↓ えらぶ　←→ 見出し　Enter 移動　F2 / Esc 閉じる")).Append("</size>");
            return builder.ToString();
        }

        /// <summary>見せる行の先頭。選んでいる行が窓の真ん中あたりに来るようにし、端では詰める。</summary>
        public static int FirstVisibleRow(int count, int selected)
        {
            int first = selected - VisibleRows / 2;
            return Mathf.Clamp(first, 0, Mathf.Max(0, count - VisibleRows));
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // フェード (100) の手前、HUD の奥。
            Canvas.sortingOrder = 90;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            // 右上の「DEV」。有効になっていることが分かるように、メニューを閉じていても出す。
            RectTransform badge = NewRect("Badge", canvasGo.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -12f), new Vector2(96f, 34f));
            badge.pivot = new Vector2(1f, 1f);
            AddBackground(badge, new Color(0.75f, 0.1f, 0.1f, 0.85f));
            TMP_Text badgeLabel = NewLabel(badge, "Label", 22f, TextAlignmentOptions.Center);
            badgeLabel.text = "<b>" + L.Get("ui.dev.badge", "DEV") + "</b>";
            badgeLabel.color = Color.white;

            RectTransform panel = NewRect("Panel", canvasGo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 600f));
            AddBackground(panel, new Color(0.07f, 0.08f, 0.11f, 0.92f));
            _panel = panel.gameObject;

            _body = NewLabel(panel, "Body", 26f, TextAlignmentOptions.TopLeft);
            _body.color = Color.white;
            _body.richText = true;
            _body.overflowMode = TextOverflowModes.Truncate;
            _body.textWrappingMode = TextWrappingModes.NoWrap;
            var bodyRect = (RectTransform)_body.transform;
            bodyRect.offsetMin = new Vector2(28f, 20f);
            bodyRect.offsetMax = new Vector2(-28f, -20f);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static void AddBackground(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static TMP_Text NewLabel(RectTransform parent, string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize = size;
            label.alignment = align;
            label.raycastTarget = false;
            return label;
        }
    }
}
