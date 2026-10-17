using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 会話ウィンドウ。DialogueSystem の行更新を受けて、1 文字ずつ送る。
    /// 送り終わっていない状態で E を押すと全文表示、もう一度で次の行へ進む。
    /// 選択肢を出している間は、本文の欄に選択肢を並べる (#9)。
    /// </summary>
    public sealed class DialogueView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _speakerLabel;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private GameObject _continueMark;
        [SerializeField] private float _charactersPerSecond = 42f;
        [SerializeField] private TmpLinkTap _tap;

        private DialogueSystem _dialogue;

        /// <summary>表示中の行と、話者が空の行に出すプレイヤーの名前。言語を切り替えたらこれを出し直す。</summary>
        private DialogueLine _line;
        private string _playerName = string.Empty;

        private string _fullText = string.Empty;
        private float _revealed;
        private int _revealedFrame = -1;
        private string _voice = string.Empty;
        private int _lastBlipAt;

        /// <summary>選択肢を指のときの形で描いているか。入力の種類が変わったら描き直す。</summary>
        private bool _choicesDrawnForTouch;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text speaker, TMP_Text body, GameObject continueMark)
        {
            _root = root;
            _speakerLabel = speaker;
            _bodyLabel = body;
            _continueMark = continueMark;
        }

        /// <summary>選択肢を叩く受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        /// <summary>言語の切り替えは DialogueSystem の有無と関係なく、生きているあいだずっと受ける。</summary>
        private void Awake()
        {
            L.LocaleChanged += OnLocaleChanged;
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
            _dialogue.ChoicesChanged += OnChoicesChanged;
            _dialogue.AdvanceGate = IsFullyRevealed;
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
            SetVisible(false);
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
            if (_dialogue != null)
            {
                _dialogue.LineChanged -= OnLineChanged;
                _dialogue.Finished -= OnFinished;
                _dialogue.ChoicesChanged -= OnChoicesChanged;
                _dialogue.AdvanceGate = null;
            }

            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
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

            GameManager game = GameManager.Instance;
            Show(line, game.SelectedCharacterShortName, game.SelectedCharacterId);
        }

        /// <summary>
        /// 1 行をいまの言語で出し、1 文字目から送り始める。playerName と playerId は話者が空の行（プレイヤー）の名前と声。
        /// DialogueSystem の行更新から呼ぶ。テストからも呼ぶ。
        /// </summary>
        public void Show(DialogueLine line, string playerName, string playerId)
        {
            if (line == null)
            {
                return;
            }

            ApplyChoiceSpacing(_bodyLabel, false, false);
            _line = line;
            _playerName = playerName ?? string.Empty;
            SetVisible(true);
            ShowSpeaker(line);

            _fullText = line.DisplayText;
            _revealed = 0f;
            _revealedFrame = -1;
            _lastBlipAt = 0;
            // 紙に書かれた文（メモ・手紙）は読むだけなので声を鳴らさない (#175)。
            _voice = line.Written ? string.Empty : DialogueVoice.ForSpeaker(line.Speaker, playerId);

            if (_bodyLabel != null)
            {
                _bodyLabel.text = _fullText;
                _bodyLabel.maxVisibleCharacters = 0;
            }
        }

        /// <summary>マウスのクリックは TmpLinkTap が捨てる。叩いたのが選択肢なら選ぶ。</summary>
        private void OnTapped(string linkId)
        {
            if (_dialogue == null || !_dialogue.IsChoosing || !InputMode.IsTouch)
            {
                return;
            }

            int index = TappedChoice(linkId, _dialogue.Choices.Count);
            if (index != NoTap)
            {
                _dialogue.TapChoice(index);
            }
        }

        /// <summary>叩いたリンクが選択肢でも「やめる」でもないときの値。</summary>
        public const int NoTap = int.MinValue;

        /// <summary>「やめる」のリンクの id。選ばずに会話を閉じる（DialogueSystem.CloseChoice）。</summary>
        public const string CloseLinkId = "choice:-1";

        /// <summary>
        /// 叩いたリンクが指す選択肢の番号。「やめる」なら DialogueSystem.CloseChoice、
        /// 選択肢でも「やめる」でもなければ <see cref="NoTap"/>。
        /// </summary>
        public static int TappedChoice(string linkId, int count)
        {
            if (linkId == CloseLinkId)
            {
                return DialogueSystem.CloseChoice;
            }

            int index = TmpLinkTap.ChoiceIn(linkId, count);
            return index >= 0 ? index : NoTap;
        }

        /// <summary>指のときの選択肢の文字の大きさ（本文の欄の文字に対する割合）。「やめる」を足しても縦の欄 300 に収める。</summary>
        public const int TouchChoiceSizePercent = 85;

        /// <summary>指のときの選択肢どうしの間（本文の欄の単位）。縦持ちは叩く高さ 44 px のために広く、横持ちは枠の高さに収める。</summary>
        public const float PortraitChoiceGap = 58f;

        public const float LandscapeChoiceGap = 44f;

        /// <summary>
        /// 選択肢を出している間の本文の欄の整え方。指なら選択肢どうしに間を空け、それ以外は間を空けない。
        /// TMP の paragraphSpacing は改行の後にだけ付き、文字の大きさ（欄の大きさ）の 1/100 を 1 とするので、
        /// 折り返した選択肢の 2 行目は詰まったまま、別の選択肢との間だけ空く。
        /// </summary>
        public static void ApplyChoiceSpacing(TMP_Text label, bool touch, bool portrait)
        {
            if (label != null)
            {
                float gap = portrait ? PortraitChoiceGap : LandscapeChoiceGap;
                label.paragraphSpacing = touch ? gap * 100f / label.fontSize : 0f;
            }
        }

        /// <summary>選択肢を出したときと、選んでいる選択肢が変わったとき。本文の欄に選択肢を並べ、すぐに全部見せる。</summary>
        private void OnChoicesChanged()
        {
            if (_dialogue == null || !_dialogue.IsChoosing)
            {
                return;
            }

            _line = null;
            _voice = string.Empty;
            _choicesDrawnForTouch = InputMode.IsTouch;
            if (_speakerLabel != null)
            {
                _speakerLabel.text = GameManager.Instance.SelectedCharacterShortName;
            }

            _fullText = ComposeChoices(_dialogue.Choices, _dialogue.ChoiceIndex, _choicesDrawnForTouch);
            _revealed = _fullText.Length;
            _revealedFrame = Time.frameCount;
            _lastBlipAt = _fullText.Length;
            ApplyChoiceSpacing(_bodyLabel, _choicesDrawnForTouch, Screen.height > Screen.width);
            if (_bodyLabel != null)
            {
                _bodyLabel.text = _fullText;
                _bodyLabel.maxVisibleCharacters = _fullText.Length;
            }

            if (_continueMark != null)
            {
                _continueMark.SetActive(false);
            }
        }

        /// <summary>
        /// 選択肢を 1 行ずつ並べる。selected の行にだけ ▶ と色を付け、各行を <c>&lt;link="choice:N"&gt;</c> で囲む。
        /// 指のときは行の間を広げて叩きやすくし、指でないときはキーの案内を添える。
        /// </summary>
        public static string ComposeChoices(IReadOnlyList<DialogueChoice> choices, int selected, bool touch)
        {
            var builder = new StringBuilder(256);
            if (touch)
            {
                builder.Append("<size=").Append(TouchChoiceSizePercent).Append("%>");
            }

            for (int i = 0; i < choices.Count; i++)
            {
                builder.Append("<link=\"choice:").Append(i).Append("\">");
                builder.Append(i == selected ? "<color=#FFD98A>▶ " : "   ");
                builder.Append(choices[i].DisplayText);
                builder.Append(i == selected ? "</color>" : string.Empty);
                builder.Append("</link>");

                // 指では最後の選択肢と「やめる」の間に間を空けない（垂直タブは段落の間を足さない改行）。
                builder.Append(touch && i == choices.Count - 1 ? "\u000B" : "\n");
            }

            if (touch)
            {
                // 指には Esc が無いので、選ばずに閉じるリンクを最後に置く。
                builder.Append("<link=\"").Append(CloseLinkId).Append("\">   ")
                    .Append(L.Pick("やめる", "Cancel")).Append("</link></size>");
            }
            else
            {
                builder.Append("<size=75%><alpha=#99>")
                    .Append(L.Pick("↑↓ えらぶ　Enter 決める", "Up/Down choose   Enter pick"))
                    .Append("<alpha=#FF></size>");
            }

            return builder.ToString();
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
                _speakerLabel.text = SpeakerLabel(line, _playerName);
            }
        }

        /// <summary>
        /// 会話中に言語を切り替えたら、表示中の行をその言語で出し直す。
        /// 文字数が変わるので送りはやり直さず全文を出す（次の行からはふつうに送る）。
        /// </summary>
        private void OnLocaleChanged()
        {
            if (_dialogue != null && _dialogue.IsChoosing)
            {
                OnChoicesChanged();
                return;
            }

            DialogueLine line = _line;
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
            _line = null;
            SetVisible(false);
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf || _bodyLabel == null)
            {
                return;
            }

            if (_dialogue != null && _dialogue.IsChoosing)
            {
                if (InputMode.IsTouch != _choicesDrawnForTouch)
                {
                    OnChoicesChanged();
                }

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
            if (string.IsNullOrEmpty(_voice) || visible - _lastBlipAt < 2 || visible <= 0 || visible > _fullText.Length)
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
