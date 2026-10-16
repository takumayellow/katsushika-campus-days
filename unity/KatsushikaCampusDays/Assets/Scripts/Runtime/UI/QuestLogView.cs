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
            AppendProgress(_builder);
            Compose(_builder, _quests);
            _drawnForTouch = InputMode.IsTouch;
            if (_drawnForTouch)
            {
                _builder.Append("\n<size=70%><alpha=#99>").Append(L.Pick("タップで閉じる", "Tap to close"))
                    .Append("<alpha=#FF></size>");
            }

            _bodyLabel.text = _builder.ToString();
        }

        /// <summary>この周回で集めた数を、一覧の先頭に足す (#10)。</summary>
        private static void AppendProgress(StringBuilder into)
        {
            CollectibleCatalog catalog = CollectibleCatalog.Instance;
            if (catalog == null)
            {
                return;
            }

            AchievementBook book = GameManager.Instance != null ? GameManager.Instance.Achievements : null;
            ComposeProgress(into,
                DayStats.HiddenCollectedCount(catalog), catalog.HiddenCount,
                DayStats.CatalogPhotoCount(catalog), catalog.PhotoSpotCount,
                book != null ? book.Unlocked.Count : 0, catalog.Achievements.Count);
        }

        /// <summary>
        /// 隠しアイテム・写真スポット・称号の「集めた数 / 全部の数」を 1 行で足す。
        /// 全部の数が 0 の項目は出さず、集めた数は全部の数で頭打ちにする。
        /// </summary>
        public static void ComposeProgress(StringBuilder into, int hidden, int hiddenTotal, int photos, int photoTotal,
            int titles, int titleTotal)
        {
            string separator = L.Pick("　", "   ");
            int start = into.Length;
            if (hiddenTotal > 0)
            {
                AppendPart(into, L.Format("ui.result.collectibles", Mathf.Clamp(hidden, 0, hiddenTotal), hiddenTotal),
                    separator, start);
            }

            if (photoTotal > 0)
            {
                AppendPart(into, L.Format("ui.result.photos", Mathf.Clamp(photos, 0, photoTotal), photoTotal),
                    separator, start);
            }

            if (titleTotal > 0)
            {
                AppendPart(into, L.Format("ui.collection.achievements", Mathf.Clamp(titles, 0, titleTotal), titleTotal),
                    separator, start);
            }

            if (into.Length == start)
            {
                return;
            }

            into.Insert(start, "<size=85%><color=#A8D8FF>");
            into.Append("</color></size>\n\n");
        }

        private static void AppendPart(StringBuilder into, string part, string separator, int start)
        {
            if (into.Length > start)
            {
                into.Append(separator);
            }

            into.Append(part);
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
