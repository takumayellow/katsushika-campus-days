using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 会話ウィンドウ。DialogueSystem の行更新を受けて、1 文字ずつ送る。
    /// 送り終わっていない状態で E を押すと全文表示、もう一度で次の行へ進む。
    /// </summary>
    public sealed class DialogueView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _speakerLabel;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private GameObject _continueMark;
        [SerializeField] private float _charactersPerSecond = 42f;

        private DialogueSystem _dialogue;
        private string _fullText = string.Empty;
        private float _revealed;
        private int _revealedFrame = -1;
        private string _voice = string.Empty;
        private int _lastBlipAt;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text speaker, TMP_Text body, GameObject continueMark)
        {
            _root = root;
            _speakerLabel = speaker;
            _bodyLabel = body;
            _continueMark = continueMark;
        }

        private void Start()
        {
            _dialogue = DialogueSystem.Instance;
            if (_dialogue == null)
            {
                Debug.LogError("[KCD] DialogueSystem がシーンにありません。");
                return;
            }

            _dialogue.LineChanged += OnLineChanged;
            _dialogue.Finished += OnFinished;
            _dialogue.AdvanceGate = IsFullyRevealed;
            L.LocaleChanged += OnLocaleChanged;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
            if (_dialogue != null)
            {
                _dialogue.LineChanged -= OnLineChanged;
                _dialogue.Finished -= OnFinished;
                _dialogue.AdvanceGate = null;
            }
        }

        /// <summary>文字送りが終わっているか。DialogueSystem の送り可否に使う。</summary>
        /// <summary>
        /// 全文が出ていて、しかも出し切ったのが前のフレーム以前なら true。
        /// 「送り途中の 1 押しで全文表示」と「次行へ送る」を同じフレームで両方起こさないためで、
        /// DialogueSystem と DialogueView のどちらの Update が先に走っても結果が変わらない。
        /// </summary>
        private bool IsFullyRevealed() => _revealed >= _fullText.Length && _revealedFrame < Time.frameCount;

        private void OnLineChanged(DialogueLine line)
        {
            if (line == null)
            {
                return;
            }

            SetVisible(true);
            ShowSpeaker(line);

            _fullText = line.DisplayText;
            _revealed = 0f;
            _revealedFrame = -1;
            _lastBlipAt = 0;
            _voice = DialogueVoice.ForSpeaker(line.Speaker, GameManager.Instance.SelectedCharacterId);

            if (_bodyLabel != null)
            {
                _bodyLabel.text = _fullText;
                _bodyLabel.maxVisibleCharacters = 0;
            }
        }

        /// <summary>話者欄の名前。話者が空ならプレイヤー、それ以外はいまの言語での話者名。</summary>
        public static string SpeakerLabel(DialogueLine line, string playerName)
        {
            return string.IsNullOrEmpty(line.Speaker) ? playerName : line.DisplaySpeaker;
        }

        private void ShowSpeaker(DialogueLine line)
        {
            if (_speakerLabel != null)
            {
                _speakerLabel.text = SpeakerLabel(line, GameManager.Instance.SelectedCharacterShortName);
            }
        }

        /// <summary>
        /// 会話中に言語を切り替えたら、表示中の行をその言語で出し直す。
        /// 文字数が変わるので送りはやり直さず全文を出す（次の行からはふつうに送る）。
        /// </summary>
        private void OnLocaleChanged()
        {
            DialogueLine line = _dialogue != null ? _dialogue.CurrentLine : null;
            if (line == null || _root == null || !_root.activeSelf)
            {
                return;
            }

            ShowSpeaker(line);
            _fullText = line.DisplayText;
            _revealed = _fullText.Length;
            _revealedFrame = Time.frameCount;
            _lastBlipAt = _fullText.Length;

            if (_bodyLabel != null)
            {
                _bodyLabel.text = _fullText;
                _bodyLabel.maxVisibleCharacters = _fullText.Length;
            }
        }

        private void OnFinished()
        {
            SetVisible(false);
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf || _bodyLabel == null)
            {
                return;
            }

            int total = _fullText.Length;
            if (_revealed < total)
            {
                _revealed += _charactersPerSecond * Time.unscaledDeltaTime;

                // 送り途中の入力は「全文表示」に使う（DialogueSystem 側の送りより先に処理する）。
                if (KCDInput.InteractPressed || KCDInput.SubmitPressed)
                {
                    _revealed = total;
                }

                if (_revealed >= total)
                {
                    _revealedFrame = Time.frameCount;
                }

                int visible = Mathf.Clamp(Mathf.FloorToInt(_revealed), 0, total);
                _bodyLabel.maxVisibleCharacters = visible;
                Blip(visible);
            }

            if (_continueMark != null)
            {
                bool done = _revealed >= total;
                if (_continueMark.activeSelf != done)
                {
                    _continueMark.SetActive(done);
                }
            }
        }

        /// <summary>2 文字ごとに話者の声を鳴らす。句読点・空白では鳴らさない。</summary>
        private void Blip(int visible)
        {
            if (visible - _lastBlipAt < 2 || visible <= 0 || visible > _fullText.Length)
            {
                return;
            }

            _lastBlipAt = visible;
            char c = _fullText[visible - 1];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || c == '…' || c == 'ー')
            {
                return;
            }

            AudioManager.Instance?.PlayBlip(_voice);
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
            {
                _root.SetActive(visible);
            }

            HUD hud = HUD.Instance;
            if (hud != null)
            {
                hud.SetGameplayUIVisible(!visible);
            }
        }
    }
}
