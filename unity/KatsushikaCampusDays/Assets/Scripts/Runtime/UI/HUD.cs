using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面表示の窓口。各ビューはこの HUD にぶら下がり、ゲームロジック側は HUD だけを見る。
    /// 参照は SceneBuilder がコードで張るので、シーン外の GUID 参照を持たない。
    /// </summary>
    public sealed class HUD : MonoBehaviour
    {
        [SerializeField] private InteractionPromptView _promptView;
        [SerializeField] private ToastView _toastView;
        [SerializeField] private QuestLogView _questLogView;
        [SerializeField] private PauseMenu _pauseMenu;
        [SerializeField] private GameObject _gameplayRoot;

        /// <summary>現在のシーンの HUD。</summary>
        public static HUD Instance { get; private set; }

        /// <summary>操作説明・クエスト表示など、会話中に隠したい塊。</summary>
        public GameObject GameplayRoot
        {
            get => _gameplayRoot;
            set => _gameplayRoot = value;
        }

        /// <summary>インタラクト表示。</summary>
        public InteractionPromptView PromptView
        {
            get => _promptView;
            set => _promptView = value;
        }

        /// <summary>トースト表示。</summary>
        public ToastView ToastView
        {
            get => _toastView;
            set => _toastView = value;
        }

        /// <summary>クエストログ。</summary>
        public QuestLogView QuestLogView
        {
            get => _questLogView;
            set => _questLogView = value;
        }

        /// <summary>ポーズメニュー。</summary>
        public PauseMenu PauseMenu
        {
            get => _pauseMenu;
            set => _pauseMenu = value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>近くの Interactable を受け取り、プロンプトの出し分けを任せる。</summary>
        public void ShowInteractionPrompt(Interactable target)
        {
            if (_promptView != null)
            {
                _promptView.SetTarget(target);
            }
        }

        /// <summary>画面下に短いメッセージを流す。</summary>
        public void ShowToast(string message)
        {
            ShowToast(message, true);
        }

        /// <summary>画面下に短いメッセージを流す。ping が false なら通知音を鳴らさない（時報チャイムに重ねない, #38）。</summary>
        public void ShowToast(string message, bool ping)
        {
            if (_toastView != null)
            {
                _toastView.Push(message);
                if (ping)
                {
                    AudioManager.Instance?.PlayUi("ui_toast", 0.6f);
                }
            }
        }

        /// <summary>会話中など、ゲームプレイ用の表示をまとめて隠す。</summary>
        public void SetGameplayUIVisible(bool visible)
        {
            if (_gameplayRoot != null && _gameplayRoot.activeSelf != visible)
            {
                _gameplayRoot.SetActive(visible);
            }
        }
    }
}
