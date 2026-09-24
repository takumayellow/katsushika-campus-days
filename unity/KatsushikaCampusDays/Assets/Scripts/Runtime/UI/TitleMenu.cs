using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// タイトル画面。Enter で「はじめから / つづきから」を経てキャラクター選択へ移る。
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        [SerializeField] private GameObject _titleRoot;
        [SerializeField] private GameObject _selectRoot;
        [SerializeField] private TMP_Text _promptLabel;
        [SerializeField] private CharacterSelect _characterSelect;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _subtitleLabel;
        [SerializeField] private float _blinkSpeed = 2.2f;
        [SerializeField] private SettingsView _settings;
        [SerializeField] private CreditsView _credits;

        private bool _moved;
        private float _promptAlphaBase = 1f;

        /// <summary>タイトルが出たフレーム。ポーズの「タイトルへ戻る」で押した決定を拾い直さない（#6）。</summary>
        private int _shownFrame = KCDInput.NoFrame;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject titleRoot, GameObject selectRoot, TMP_Text prompt, CharacterSelect select)
        {
            _titleRoot = titleRoot;
            _selectRoot = selectRoot;
            _promptLabel = prompt;
            _characterSelect = select;
        }

        /// <summary>設定とクレジット。SceneBuilder から差し込む。</summary>
        public void BindExtras(SettingsView settings, CreditsView credits)
        {
            _settings = settings;
            _credits = credits;
        }

        /// <summary>題名と副題。言語切替で書き換えるために持つ。</summary>
        public void BindHeadings(TMP_Text title, TMP_Text subtitle)
        {
            _titleLabel = title;
            _subtitleLabel = subtitle;
        }

        private bool IsModalOpen =>
            (_settings != null && _settings.IsOpen) || (_credits != null && _credits.IsOpen);

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
        }

        private void RefreshPrompt()
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = L.Get("ui.title.name", "葛飾キャンパスデイズ");
            }

            if (_subtitleLabel != null)
            {
                _subtitleLabel.text = L.Get("ui.title.subtitle", "東京理科大学 葛飾キャンパス 探索記");
            }

            if (_promptLabel == null)
            {
                return;
            }

            string main = SaveSystem.HasSave
                ? L.Get("ui.title.continue_hint", "Enter でつづきから　/　F9 でセーブを読み込む")
                : L.Get("ui.title.press_start", "Enter ではじめる");
            string extras = L.Pick(
                "\nC クレジット　/　O 設定",
                "\nC: Credits   /   O: Settings");
            _promptLabel.text = main + "<size=70%>" + extras + "</size>";
        }

        private void Start()
        {
            _shownFrame = Time.frameCount;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            KCDInput.GameplayBlocked = false;

            if (_selectRoot != null)
            {
                _selectRoot.SetActive(false);
            }

            if (_promptLabel != null)
            {
                _promptAlphaBase = _promptLabel.color.a;
                RefreshPrompt();
            }
        }

        private void Update()
        {
            SyncTitleVisibility();
            BlinkPrompt();

            if (_moved || IsModalOpen || KCDInput.ModalClosedThisFrame ||
                KCDInput.IgnoresInput(_shownFrame))
            {
                return;
            }

            if (KCDInput.CreditsPressed && _credits != null)
            {
                _credits.Open();
                return;
            }

            if (KCDInput.SettingsPressed && _settings != null)
            {
                _settings.Open(null);
                return;
            }

            if (KCDInput.SubmitPressed)
            {
                EnterCharacterSelect();
            }
            else if (KCDInput.QuickLoadPressed && SaveSystem.HasSave)
            {
                SaveData data = SaveSystem.Read();
                if (data != null)
                {
                    GameManager.Instance.SelectedCharacterId = data.CharacterId;
                    GameManager.Instance.GameTimeHours = data.TimeHours;
                    // DayNightCycle.Start は初めてキャンパスに入るとき 8:30 から始め、GameTimeHours を見ない。
                    // セーブはキャンパスに入ったあとのものなので入場済みにして、ロードした時刻を引き継がせる（#38）。
                    GameManager.Instance.HasEnteredCampus = true;
                    GameManager.Instance.Quests?.Restore(data.Quests);
                    _moved = true;
                    AudioManager.Instance?.PlayUi("ui_confirm");
                    GameManager.Instance.EnterCampus();
                }
            }
        }

        /// <summary>
        /// 設定かクレジットを開いている間は、題名と案内を隠す。パネルは半透明なので、
        /// 大きな白い題名がパネルの見出しに重なって読めなくなる。
        /// </summary>
        private void SyncTitleVisibility()
        {
            if (_moved || _titleRoot == null)
            {
                return;
            }

            bool show = !IsModalOpen;
            if (_titleRoot.activeSelf != show)
            {
                _titleRoot.SetActive(show);
            }
        }

        private void BlinkPrompt()
        {
            if (_promptLabel == null || _moved)
            {
                return;
            }

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * _blinkSpeed);
            Color color = _promptLabel.color;
            _promptLabel.color = new Color(color.r, color.g, color.b, _promptAlphaBase * pulse);
        }

        private void EnterCharacterSelect()
        {
            _moved = true;
            AudioManager.Instance?.PlayUi("ui_confirm");

            if (_titleRoot != null)
            {
                _titleRoot.SetActive(false);
            }

            if (_selectRoot != null)
            {
                _selectRoot.SetActive(true);
            }

            if (_characterSelect != null)
            {
                _characterSelect.SetActiveSelection(true);
            }
        }
    }
}
