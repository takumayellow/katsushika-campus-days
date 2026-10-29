using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Tab で開く一覧。受注中・完了済みをまとめて 1 枚のテキストに流し込む。
    /// 指で遊んでいるときは、HUD のボタンで開き、題名の外を叩くと閉じる (#187)。
    /// 受注中が 2 本以上なら、↑↓ か題名を叩いて追うクエストを選び直せる (#410)。
    /// </summary>
    public sealed class QuestLogView : MonoBehaviour
    {
        /// <summary>受注中の題名に付けるリンクの接頭辞。続く数字は <see cref="QuestSystem.All"/> の番号。</summary>
        public const string TrackLinkPrefix = "track:";

        /// <summary>追っているクエストの題名の色。ミニマップの目標の輪（Minimap._objectiveColor）と同じ水色。</summary>
        private const string TrackedColor = "#4FD1D9";

        private const string ActiveColor = "#FFE6A8";

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

        /// <summary>
        /// マウスのクリックは TmpLinkTap が捨て、ここでは指で遊んでいるときだけ受ける。
        /// 受注中が 2 本以上なら、題名を叩いたクエストを追い、開いたままにする。
        /// ほかの場所と、いま選んでいる題名を叩いたら閉じる（選び直すものが無い）。
        /// </summary>
        private void OnTapped(string linkId)
        {
            if (!IsOpen || !InputMode.IsTouch)
            {
                return;
            }

            QuestData quest = TrackLinkTarget(_quests, linkId);
            if (quest == null || !Select(quest, "ui_confirm"))
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

            // 叩く板は画面全体に広げてあり、文字を知らないとリンクを探せない。題名のリンクを読めるよう本文を指す。
            if (_tap != null)
            {
                _tap.Bind(_bodyLabel);
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
            else if (IsOpen && _quests != null)
            {
                QuestData next = NextTracked(_quests, KCDInput.MenuVertical);
                if (next != null)
                {
                    Select(next, "ui_move");
                }
            }
        }

        /// <summary>
        /// 追うクエストを選び、一覧を描き直す（ミニマップの輪と追跡表示は Changed で切り替わる）。
        /// もう選んであれば（選んでいないときは、いま追っているものなら）何もせず false。
        /// </summary>
        private bool Select(QuestData quest, string sound)
        {
            if (quest == (_quests.ChosenQuest ?? _quests.TrackedQuest) || !_quests.Track(quest.Id))
            {
                return false;
            }

            AudioManager.Instance?.PlayUi(sound);
            Rebuild();
            return true;
        }

        /// <summary>
        /// ↑↓ で次に追うクエスト。一覧と同じ順に受注中のものを巡り、端からは反対の端へ戻る。
        /// 起点は選んだもの（無ければいま追っているもの）。制限時間を数えている間も、選んだものから進める。
        /// 受注中が 2 本未満か、向きが 0 なら null。
        /// </summary>
        public static QuestData NextTracked(QuestSystem quests, int direction)
        {
            if (quests == null || direction == 0)
            {
                return null;
            }

            var active = new List<QuestData>();
            for (int i = 0; i < quests.All.Count; i++)
            {
                if (quests.IsActive(quests.All[i].Id))
                {
                    active.Add(quests.All[i]);
                }
            }

            if (active.Count < 2)
            {
                return null;
            }

            int step = direction > 0 ? 1 : -1;
            int at = active.IndexOf(quests.ChosenQuest ?? quests.TrackedQuest);
            if (at < 0)
            {
                return active[step > 0 ? 0 : active.Count - 1];
            }

            return active[(at + step + active.Count) % active.Count];
        }

        /// <summary>
        /// 題名のリンク（"track:" と All の番号）が指す受注中のクエスト。形が違うか受注中でなければ null。
        /// 受注中が 1 本だけなら選ぶものが無いので null（叩けば、ほかの場所と同じく閉じる）。
        /// </summary>
        public static QuestData TrackLinkTarget(QuestSystem quests, string linkId)
        {
            if (quests == null || !TmpLinkTap.TryParse(linkId, TrackLinkPrefix, out int index) ||
                index >= quests.All.Count || ActiveCount(quests) < 2)
            {
                return null;
            }

            QuestData quest = quests.All[index];
            return quests.IsActive(quest.Id) ? quest : null;
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
            _builder.Append(TrackHint(_quests, _drawnForTouch));
            if (_drawnForTouch)
            {
                _builder.Append(CloseHint());
            }

            _bodyLabel.text = _builder.ToString();
        }

        /// <summary>
        /// 指のときに一覧の末尾へ足す「タップで閉じる」。本文と同じ大きさで、薄くだけする。
        /// 70% にすると縦 360 幅で 8.8 px まで小さくなる (#187)。
        /// </summary>
        public static string CloseHint()
        {
            return "\n<alpha=#99>" + L.Pick("タップで閉じる", "Tap to close") + "<alpha=#FF>";
        }

        /// <summary>
        /// 受注中が 2 本以上のとき、一覧の末尾へ足す追うクエストの選び方。1 本なら選ぶものが無いので出さない。
        /// 閉じ方の案内（<see cref="CloseHint"/>）と同じく、本文と同じ大きさで薄くする。
        /// </summary>
        public static string TrackHint(QuestSystem quests, bool touch)
        {
            if (quests == null || ActiveCount(quests) < 2)
            {
                return string.Empty;
            }

            string hint = touch
                ? L.Get("ui.questlog.track_hint_touch", "題名をタップで追跡するクエストを選ぶ")
                : L.Get("ui.questlog.track_hint", "↑↓ で追跡するクエストを選ぶ");
            return "\n<alpha=#99>" + hint + "<alpha=#FF>";
        }

        private static int ActiveCount(QuestSystem quests)
        {
            int count = 0;
            for (int i = 0; i < quests.All.Count; i++)
            {
                if (quests.IsActive(quests.All[i].Id))
                {
                    count++;
                }
            }

            return count;
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

        /// <summary>
        /// 受注中・完了済みのクエストを一覧の文面にして足す。題名・あらすじ・目標はいまの言語で出す。
        /// 受注中のものを目標まで書き、達成したものは題名だけを 1 段落にまとめて後ろに置く。
        /// 達成したものまで目標を書くと、全部を終えるころには 90 行近くになり枠に収まらない。
        /// 受注中の題名には、いま追っているもの（[追跡中]）と、制限時間が終われば追うもの（[次に追跡]）の印を付ける (#410)。
        /// </summary>
        public static void Compose(StringBuilder into, QuestSystem quests)
        {
            QuestData tracked = quests.TrackedQuest;
            QuestData chosen = quests.ChosenQuest;
            int shown = 0;
            int completed = 0;
            for (int i = 0; i < quests.All.Count; i++)
            {
                QuestData quest = quests.All[i];
                if (quests.IsCompleted(quest.Id))
                {
                    completed++;
                }
                else if (quests.IsActive(quest.Id))
                {
                    AppendActive(into, quest, i, tracked, chosen);
                    shown++;
                }
            }

            if (completed > 0)
            {
                AppendCompleted(into, quests, completed);
                shown += completed;
            }

            if (shown == 0)
            {
                into.Append(L.Get("ui.questlog.empty", "まだ受けているクエストはない。\nキャンパスを歩いて、誰かに話しかけてみよう。"));
            }
        }

        /// <summary>受注中の 1 本。題名は <see cref="TrackLinkPrefix"/> のリンクにして、指で叩いて選べるようにする。</summary>
        private static void AppendActive(StringBuilder into, QuestData quest, int index, QuestData tracked, QuestData chosen)
        {
            bool tracking = quest == tracked;
            string label = tracking ? L.Get("ui.questlog.tracking", "[追跡中] ")
                : quest == chosen ? L.Get("ui.questlog.track_next", "[次に追跡] ")
                : L.Get("ui.questlog.active", "[受注中] ");
            into.Append("<link=\"").Append(TrackLinkPrefix).Append(index).Append("\"><color=")
                .Append(tracking ? TrackedColor : ActiveColor).Append('>').Append(label).Append(quest.DisplayTitle)
                .Append("</color></link>\n");

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

        /// <summary>達成したクエストの数の見出しと、題名を読点でつないだ段落。</summary>
        private static void AppendCompleted(StringBuilder into, QuestSystem quests, int count)
        {
            into.Append("<color=#8FD48F>").Append(L.Format("ui.questlog.completed_heading", count))
                .Append("</color>\n<size=85%><alpha=#CC>");
            string separator = L.Pick("、", ", ");
            bool first = true;
            for (int i = 0; i < quests.All.Count; i++)
            {
                QuestData quest = quests.All[i];
                if (!quests.IsCompleted(quest.Id))
                {
                    continue;
                }

                if (!first)
                {
                    into.Append(separator);
                }

                into.Append(quest.DisplayTitle);
                first = false;
            }

            into.Append("<alpha=#FF></size>\n");
        }
    }
}
