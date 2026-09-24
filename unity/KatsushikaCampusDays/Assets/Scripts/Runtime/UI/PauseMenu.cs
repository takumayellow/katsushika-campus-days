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

        /// <summary>時間を止めて封鎖を掛けているか。設定パネルを出している間（本体は隠れている）も true。</summary>
        private bool _paused;

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
            if (!_paused)
            {
                return;
            }

            _paused = false;
            Time.timeScale = 1f;
            KCDInput.Unblock(this);
        }

        private void Update()
        {
            // DormEnding.IsAnyShowing: 裏エンド (#41) の暗転中と結果表示中は Esc を食わせない。
            // 食わせると暗転の裏でポーズが開き、閉じたときの SetOpen(false) が
            // Time.timeScale = 1 に戻してしまう（裏エンドの最中に時間が動き出す）。
            if (IgnoresMenuKey(KCDInput.PhotoMode, KCDInput.ModalClosedThisFrame, ResultScreen.IsAnyOpen,
                    DormEnding.IsAnyShowing, _settings != null && _settings.IsOpen))
            {
                return;
            }

            QuestLogView log = HUD.Instance != null ? HUD.Instance.QuestLogView : null;
            if (QuestLogKeepsTheMenuKey(IsOpen, log != null && log.IsOpen,
                    log != null && log.ClosedByMenuFrame == Time.frameCount))
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
                _index = MoveCursor(_index, step);
                Redraw();
                AudioManager.Instance?.PlayUi("ui_move");
            }

            if (KCDInput.SubmitPressed)
            {
                AudioManager.Instance?.PlayUi("ui_confirm");
                Execute(_index);
            }
        }

        /// <summary>メニューの項目数（再開 / セーブ / ロード / 設定 / タイトルへ）。</summary>
        public static int EntryCount => EntryKeys.Length;

        /// <summary>
        /// Esc（メニューキー）を見ないか。写真モード中、モーダルを閉じたそのフレーム、結果画面・寮の締めの表示中、
        /// ポーズから開いた設定パネルの間は、ポーズを開きも閉じもしない。
        /// </summary>
        public static bool IgnoresMenuKey(bool photoMode, bool modalClosedThisFrame, bool resultOpen,
            bool dormEndingShowing, bool settingsOpen)
        {
            return photoMode || modalClosedThisFrame || resultOpen || dormEndingShowing || settingsOpen;
        }

        /// <summary>
        /// ポーズが閉じているとき、Esc をクエストログに譲るか。ログが開いている間と、ログを Esc で閉じたそのフレーム
        /// （同じ Esc でポーズが開かないように）。ポーズが開いていれば譲らない（Esc で閉じられる）。
        /// </summary>
        public static bool QuestLogKeepsTheMenuKey(bool pauseOpen, bool questLogOpen, bool questLogClosedThisFrame)
        {
            return !pauseOpen && (questLogOpen || questLogClosedThisFrame);
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

        private void Execute(int index)
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
