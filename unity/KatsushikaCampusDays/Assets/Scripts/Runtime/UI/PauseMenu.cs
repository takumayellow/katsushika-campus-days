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

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

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

        private void Update()
        {
            if (KCDInput.PhotoMode || KCDInput.ModalClosedThisFrame || ResultScreen.IsAnyOpen ||
                (_settings != null && _settings.IsOpen))
            {
                return;
            }

            QuestLogView log = HUD.Instance != null ? HUD.Instance.QuestLogView : null;
            if (!IsOpen && log != null && (log.IsOpen || log.ClosedByMenuFrame == Time.frameCount))
            {
                return;
            }

            if (KCDInput.MenuPressed)
            {
                SetOpen(!IsOpen);
                return;
            }

            if (!IsOpen)
            {
                return;
            }

            int step = KCDInput.MenuVertical;
            if (step != 0)
            {
                _index = (_index + step + EntryKeys.Length) % EntryKeys.Length;
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (KCDInput.SubmitPressed)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Execute(_index);
            }
        }

        /// <summary>開閉する。開いている間は時間を止める。</summary>
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

            _root.SetActive(open);
            Time.timeScale = open ? 0f : 1f;
            KCDInput.GameplayBlocked = open;
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

        private void Execute(int index)
        {
            switch (index)
            {
                case 0:
                    SetOpen(false);
                    break;
                case 1:
                    SaveSystem.Save();
                    HUD.Instance?.ShowToast(L.Get("ui.hud.saved", "セーブしました"));
                    SetOpen(false);
                    break;
                case 2:
                    SetOpen(false);
                    SaveSystem.Load();
                    break;
                case 3:
                    OpenSettings();
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
