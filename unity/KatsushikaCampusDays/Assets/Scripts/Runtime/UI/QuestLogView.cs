using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Tab で開く一覧。受注中・完了済みをまとめて 1 枚のテキストに流し込む。
    /// 指で遊んでいるときは、HUD のボタンで開き、どこを叩いても閉じる (#187)。
    /// </summary>
    public sealed class QuestLogView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _bodyLabel;
        [SerializeField] private TmpLinkTap _tap;

        /// <summary>閉じ方の案内を指のときの文面で書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _drawnForTouch;

        private readonly StringBuilder _builder = new StringBuilder(1024);
        private QuestSystem _quests;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>Esc でログを閉じたフレーム。同じ Esc でポーズメニューが開かないよう PauseMenu が見る。</summary>
        public int ClosedByMenuFrame { get; private set; } = -1;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text body)
        {
            _root = root;
            _bodyLabel = body;
        }

        /// <summary>どこを叩いても閉じる受け口。SceneBuilder から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void OnEnable()
        {
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        /// <summary>マウスのクリックは TmpLinkTap が捨て、ここでは指で遊んでいるときだけ受ける。</summary>
        private void OnTapped(string linkId)
        {
            if (IsOpen && InputMode.IsTouch)
            {
                SetOpen(false);
            }
        }

        private void Start()
        {
            _quests = GameManager.Instance.Quests;
            if (_root != null)
            {
                _root.SetActive(false);
            }

            L.LocaleChanged += OnLocaleChanged;
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        /// <summary>開いたまま言語を切り替えたら、その場で描き直す。閉じているなら次に開いたときに作る。</summary>
        private void OnLocaleChanged()
        {
            if (IsOpen)
            {
                Rebuild();
            }
        }

        /// <summary>開いたまま無効にされても、自分の封鎖を残さない。</summary>
        private void OnDisable()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }

            KCDInput.Unblock(this);
        }

        private void Update()
        {
            // DormEnding / FormalEnding.IsAnyShowing: 裏エンド (#41) とエンディング (#176) の最中は Tab でログを開かせない。
            if (ResultScreen.IsAnyOpen || DormEnding.IsAnyShowing || FormalEnding.IsAnyShowing)
            {
                return;
            }

            if (IsOpen && InputMode.IsTouch != _drawnForTouch)
            {
                Rebuild();
            }

            if (KCDInput.QuestLogPressed)
            {
                Toggle();
            }
            else if (IsOpen && KCDInput.MenuPressed)
            {
                SetOpen(false);
                ClosedByMenuFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// ログを開いてよいか。ほかの封鎖（ポーズ・会話・写真モード・建物の出入りの暗転など）があるときは開かない (#62)。
        /// 自分の封鎖は数えない（開いているログを閉じるのはいつでもできる）。
        /// </summary>
        public static bool CanOpen(bool blocked, bool blockedBySelf, bool photoMode)
        {
            return !photoMode && (!blocked || blockedBySelf);
        }

        /// <summary>開閉を切り替える。閉じるのはいつでも、開くのは <see cref="CanOpen"/> のときだけ。</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                SetOpen(false);
            }
            else if (CanOpen(KCDInput.GameplayBlocked, KCDInput.IsBlockedBy(this), KCDInput.PhotoMode))
            {
                SetOpen(true);
            }
        }

        /// <summary>
        /// 開閉する。開いている間は自分の名前で操作を封鎖する。
        /// 共有の GameplayBlocked への代入だと、ポーズ中に開いて閉じたときにポーズの封鎖まで外れていた (#62)。
        /// </summary>
        public void SetOpen(bool open)
        {
            if (_root == null)
            {
                return;
            }

            if (open)
            {
                Rebuild();
            }

            if (open != _root.activeSelf)
            {
                AudioManager.Instance?.PlayUi(open ? "ui_open" : "ui_close");
            }

            _root.SetActive(open);
            if (open)
            {
                KCDInput.Block(this);
            }
            else
            {
                KCDInput.Unblock(this);
            }
        }

        private void Rebuild()
        {
            if (_bodyLabel == null || _quests == null)
            {
                return;
            }

            _builder.Length = 0;
            Compose(_builder, _quests);
            _drawnForTouch = InputMode.IsTouch;
            if (_drawnForTouch)
            {
                _builder.Append("\n<size=70%><alpha=#99>").Append(L.Pick("タップで閉じる", "Tap to close"))
                    .Append("<alpha=#FF></size>");
            }

            _bodyLabel.text = _builder.ToString();
        }

        /// <summary>受注中・完了済みのクエストを一覧の文面にして足す。題名・あらすじ・目標はいまの言語で出す。</summary>
        public static void Compose(StringBuilder into, QuestSystem quests)
        {
            int shown = 0;

            for (int i = 0; i < quests.All.Count; i++)
            {
                QuestData quest = quests.All[i];
                bool active = quests.IsActive(quest.Id);
                bool completed = quests.IsCompleted(quest.Id);
                if (!active && !completed)
                {
                    continue;
                }

                shown++;
                into.Append(completed ? "<color=#8FD48F>" : "<color=#FFE6A8>");
                into.Append(completed
                    ? L.Get("ui.questlog.completed", "[達成] ")
                    : L.Get("ui.questlog.active", "[受注中] "));
                into.Append(quest.DisplayTitle);
                into.Append("</color>\n");

                string summary = quest.DisplaySummary;
                if (!string.IsNullOrEmpty(summary))
                {
                    into.Append("  ").Append(summary).Append('\n');
                }

                for (int s = 0; s < quest.Steps.Count; s++)
                {
                    QuestStep step = quest.Steps[s];
                    into.Append(step.Completed ? "  <s>" : "  ");
                    into.Append("・").Append(step.DisplayText);
                    into.Append(step.Completed ? "</s>\n" : "\n");
                }

                into.Append('\n');
            }

            if (shown == 0)
            {
                into.Append(L.Get("ui.questlog.empty", "まだ受けているクエストはない。\nキャンパスを歩いて、誰かに話しかけてみよう。"));
            }
        }
    }
}
