using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 本編のエンディング (#176)。最後のメインクエスト「水盤に落ちる夕日」（<see cref="SunsetQuestId"/>）を
    /// 達成したら、少し間をおいて稲荷の夕日の話（inari / t_sunset）を流し、話し終えたら暗転して
    /// 今日の振り返り → 会った人 → クレジットの 3 枚を Enter で送る。最後の 1 枚を閉じると暗転が明けて、
    /// その場でキャンパスを歩き続けられる。タイトルへは戻さず、日付も進めない。
    ///
    /// 夕日のクエストは一度しか達成されない（クエストは日をまたいでも戻らない）ので、
    /// 出すのは達成した瞬間の 1 回だけ。セーブには何も足さない。エンディングの途中でゲームを閉じると、
    /// 読み直したあとには出ない。
    ///
    /// 達成から閉じるまで <see cref="IsAnyShowing"/> を立て、ポーズ・クエストログ・オートセーブ・
    /// 一日の終わりのリザルトを止める（<see cref="DormEnding.IsAnyShowing"/> と同じ門）。
    /// 会話の封鎖とは別に自分でも <c>KCDInput.Block(this)</c> を掛け、待っている間に別の NPC に
    /// 話しかけられないようにする。
    /// </summary>
    public sealed class FormalEnding : MonoBehaviour
    {
        /// <summary>このクエストを達成したら出す。</summary>
        public const string SunsetQuestId = "q_sunset";

        /// <summary>流す会話の NPC と話題。</summary>
        public const string DialogueId = "inari";
        public const string TopicId = "t_sunset";

        /// <summary>達成してから会話を始めるまでの実時間の秒数。達成のトーストとジングルを先に聞かせる。</summary>
        public const float DialogueDelaySeconds = 2f;

        /// <summary>暗転にかける秒数。裏エンドと揃える。</summary>
        public const float FadeSeconds = DormEnding.FadeSeconds;

        /// <summary>ページを出した直後に入力を捨てる秒数。会話を送った Enter でそのまま送らないため。</summary>
        public const float InputGuardSeconds = DormEnding.InputGuardSeconds;

        /// <summary>暗転の Canvas の重ね順。裏エンドと同じ（同時には出ない）。</summary>
        public const int SortingOrder = DormEnding.SortingOrder;

        /// <summary>ページの Canvas の重ね順。</summary>
        public const int PanelSortingOrder = DormEnding.PanelSortingOrder;

        /// <summary>ページの数（振り返り・会った人・クレジット）。</summary>
        public const int PageCount = 3;

        /// <summary>会った人のページに並べる順。講義の教授（lecture）は外の教授と同じ人なので prof にまとめる。</summary>
        private static readonly string[] PeopleOrder = { "inari", "kaname", "sora", "prof", "dorm_head" };

        /// <summary>会った人のページに出しうる NPC の id。名前は辞書の ui.npc.&lt;id&gt; で引く。</summary>
        public static IReadOnlyList<string> PeopleIds => PeopleOrder;

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TmpLinkTap _tap;
        [SerializeField] private TMP_Text _choice;

        private CanvasGroup _fade;
        private QuestSystem _quests;
        private bool _subscribedDialogue;
        private bool _played;
        private bool _awaitingDialogue;
        private bool _showing;
        private bool _active;
        private bool _frozen;
        private int _page;
        private float _openedAt;
        private string[] _pages;

        /// <summary>下段の案内を指のときの文面で書いているか。入力の種類が変わったら書き直す。</summary>
        private bool _choiceForTouch;

        /// <summary>本文の折り返しを縦長の画面に合わせて決めたか。画面の向きが変わったら決め直す。</summary>
        private bool _wrapForPortrait;

        /// <summary>ページを出している最中か。</summary>
        public bool IsShowing => _showing;

        /// <summary>もう出したか。</summary>
        public bool HasPlayed => _played;

        /// <summary>いま出しているページ（0 始まり）。ページを出していなければ -1。</summary>
        public int CurrentPage => _showing ? _page : -1;

        /// <summary>
        /// どこかでエンディングが動いている最中か。達成して会話を待っている間・会話中・暗転中も含む。
        /// </summary>
        public static bool IsAnyShowing { get; private set; }

        // ---- 純関数（Unity を起動せずにテストする）----

        /// <summary>達成したクエストでエンディングを出すか。夕日のクエストで、まだ出していないときだけ。</summary>
        public static bool ShouldPlay(bool alreadyPlayed, string completedQuestId)
        {
            return !alreadyPlayed && completedQuestId == SunsetQuestId;
        }

        /// <summary>会話を始めてよいか。間をおき終え、ほかの会話が終わっているとき。</summary>
        public static bool ShouldStartDialogue(float waitedSeconds, bool dialoguePlaying)
        {
            return waitedSeconds >= DialogueDelaySeconds && !dialoguePlaying;
        }

        /// <summary>いまの入力でページを送るか。出した直後の <see cref="InputGuardSeconds"/> 秒は捨てる。</summary>
        public static bool ShouldAdvance(bool showing, float shownSeconds, bool submitPressed)
        {
            return showing && submitPressed && shownSeconds >= InputGuardSeconds;
        }

        /// <summary>ページの見出し。範囲の外は最後のページに寄せる。</summary>
        public static string HeadingText(int page, bool english)
        {
            switch (Mathf.Clamp(page, 0, PageCount - 1))
            {
                case 0:
                    return english ? "One Day at Katsushika" : "葛飾キャンパスの一日";
                case 1:
                    return english ? "People You Met" : "今日会った人";
                default:
                    return english ? "Credits" : "クレジット";
            }
        }

        /// <summary>下段の案内。最後のページだけ「キャンパスに戻る」。指で遊んでいるときは「タップで」と書く (#187)。</summary>
        public static string ChoiceText(int page, bool english, bool touch = false)
        {
            string counter = "  (" + (Mathf.Clamp(page, 0, PageCount - 1) + 1) + "/" + PageCount + ")";
            // 日本語は「Enter で」と「タップで」で空白の有無が違うので、言い方ごと書き分ける。
            string key = touch ? (english ? "Tap — " : "タップで") : (english ? "Enter — " : "Enter で");
            if (page >= PageCount - 1)
            {
                return key + (english ? "back to campus" : "キャンパスに戻る") + counter;
            }

            return key + (english ? "next" : "次へ") + counter;
        }

        /// <summary>
        /// 振り返りの本文。数は一日の終わりのリザルト（<see cref="DayEndEvaluator.Evaluate"/>）と同じものを並べる。
        /// 負の数は 0 に、総数を超える数は総数に寄せる。
        /// </summary>
        public static string RecapText(int day, ResultData result, bool english)
        {
            result = result ?? new ResultData();
            var builder = new StringBuilder(256);
            builder.Append(english ? "The sun has set on day " + Mathf.Max(1, day) + "."
                : Mathf.Max(1, day) + " 日目の夕日が沈んだ。").Append("\n\n");
            AppendCount(builder, english ? "Quests" : "クエスト", result.Quests, result.QuestTotal);
            AppendCount(builder, english ? "Hidden items" : "隠しアイテム", result.Collectibles, result.CollectibleTotal);
            AppendCount(builder, english ? "Photos" : "写真", result.Photos, result.PhotoTotal);
            AppendCount(builder, english ? "Buildings entered" : "入った建物", result.Buildings, result.BuildingTotal);
            builder.Append('\n');
            builder.Append(english ? "<size=85%>The campus is still here. Walk on as long as you like.</size>"
                : "<size=85%>キャンパスはこのまま。好きなだけ歩いていける。</size>");
            return builder.ToString();
        }

        private static void AppendCount(StringBuilder builder, string label, int count, int total)
        {
            int shownTotal = Mathf.Max(0, total);
            builder.Append(label).Append("  ").Append(Mathf.Clamp(count, 0, shownTotal))
                .Append(" / ").Append(shownTotal).Append('\n');
        }

        /// <summary>
        /// 会った人の本文。話しかけた NPC の id から、決まった順で名前と一言を並べる。
        /// 講義の教授（lecture）と外の教授（prof）は 1 人として数える。人でない相手（メモ・手紙）は出さない。
        /// 名前は nameOf（id → 表示名。実行時は辞書の ui.npc.*）で引く。
        /// </summary>
        public static string PeopleText(ICollection<string> talkedIds, bool english, System.Func<string, string> nameOf)
        {
            var builder = new StringBuilder(512);
            foreach (string id in PeopleOrder)
            {
                bool met = talkedIds != null &&
                           (talkedIds.Contains(id) || (id == "prof" && talkedIds.Contains("lecture")));
                if (!met)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("<b>").Append(nameOf != null ? nameOf(id) : id).Append("</b>\n");
                builder.Append("<size=85%>").Append(PersonLine(id, english)).Append("</size>");
            }

            if (builder.Length == 0)
            {
                return english ? "You did not talk to anyone today." : "今日は誰とも話さなかった。";
            }

            return builder.ToString();
        }

        /// <summary>会った人のページの一言。<see cref="PeopleIds"/> に無い id は空。</summary>
        public static string PersonLine(string id, bool english)
        {
            switch (id)
            {
                case "inari":
                    return english ? "\"See you at the library tomorrow. The pool will still be here.\""
                        : "「明日も図書館にいるよ。水盤も、たぶんそのまま」";
                case "kaname":
                    return english ? "\"If the milk runs out again, you're my first call.\""
                        : "「また牛乳が切れたら、真っ先にあなたを呼ぶからね」";
                case "sora":
                    return english ? "\"The bench in the sun is yours too now. Half of it.\""
                        : "「日当たりのいいベンチ、半分はあなたのにしてあげる」";
                case "prof":
                    return english ? "\"Keep asking questions. That is the whole trick.\""
                        : "「質問をやめないこと。こつはそれだけだ」";
                case "dorm_head":
                    return english ? "\"Curfew is 23:00. Remember that much.\""
                        : "「門限は 23 時だ。それだけは覚えておきなさい」";
                default:
                    return string.Empty;
            }
        }

        // ---- MonoBehaviour ----

        /// <summary>SceneBuilder（FormalEndingFactory）から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body, TMP_Text choice)
        {
            _root = root;
            _heading = heading;
            _body = body;
            _choice = choice;
        }

        /// <summary>どこを叩いても次へ送る受け口。FormalEndingFactory から差し込む。</summary>
        public void BindTap(TmpLinkTap tap)
        {
            _tap = tap;
        }

        private void Awake()
        {
            _fade = BuildFadeOverlay();
        }

        private void OnEnable()
        {
            TrySubscribe();
            if (_tap != null)
            {
                _tap.Tapped += OnTapped;
            }
        }

        private void OnDisable()
        {
            if (_tap != null)
            {
                _tap.Tapped -= OnTapped;
            }

            SubscribeQuests(null);
            if (_subscribedDialogue && DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Finished -= OnDialogueFinished;
            }

            _subscribedDialogue = false;
            _awaitingDialogue = false;
            _showing = false;
            KCDInput.Unblock(this);

            if (_fade != null)
            {
                _fade.alpha = 0f;
            }

            if (_active)
            {
                _active = false;
                IsAnyShowing = false;
            }

            // 暗転の途中で消されても、止めた時間は必ず戻す（DormEnding と同じ理由で _frozen を見る）。
            if (_frozen)
            {
                _frozen = false;
                Time.timeScale = 1f;
            }
        }

        private void Update()
        {
            TrySubscribe();

            if (_showing && InputMode.IsTouch != _choiceForTouch)
            {
                PaintChoice(L.IsEnglish);
            }

            if (_showing && ScreenShape.IsPortrait != _wrapForPortrait)
            {
                ApplyWrap();
            }

            if (ShouldAdvance(_showing, Time.unscaledTime - _openedAt,
                    KCDInput.SubmitPressed || KCDInput.InteractPressed))
            {
                NextPage();
            }
        }

        /// <summary>指で遊んでいるときに叩いたら、Enter と同じ条件で次へ送る。</summary>
        private void OnTapped(string linkId)
        {
            if (InputMode.IsTouch && ShouldAdvance(_showing, Time.unscaledTime - _openedAt, true))
            {
                NextPage();
            }
        }

        /// <summary>
        /// クエストの進行と会話を掴む。進行は「はじめから」で作り直されることがあり、
        /// DialogueSystem はこちらより後に Awake することがあるので、毎フレーム確かめる。
        /// </summary>
        private void TrySubscribe()
        {
            GameManager manager = GameManager.Instance;
            SubscribeQuests(manager != null ? manager.Quests : null);

            DialogueSystem dialogue = DialogueSystem.Instance;
            if (!_subscribedDialogue && dialogue != null)
            {
                dialogue.Finished += OnDialogueFinished;
                _subscribedDialogue = true;
            }
        }

        private void SubscribeQuests(QuestSystem quests)
        {
            if (_quests == quests)
            {
                return;
            }

            if (_quests != null)
            {
                _quests.QuestCompleted -= OnQuestCompleted;
            }

            _quests = quests;
            if (_quests != null)
            {
                _quests.QuestCompleted += OnQuestCompleted;
            }
        }

        private void OnQuestCompleted(QuestData quest)
        {
            if (quest != null && ShouldPlay(_played, quest.Id))
            {
                Play();
            }
        }

        /// <summary>クエストを見ずにエンディングを出す（デバッグ・スモーク用）。二度目は何もしない。</summary>
        public void Play()
        {
            if (_played || !isActiveAndEnabled)
            {
                return;
            }

            _played = true;
            _active = true;
            IsAnyShowing = true;
            // 待っている間に別の NPC に話しかけたり建物に入ったりさせない。
            KCDInput.Block(this);
            StartCoroutine(WaitAndTalk());
        }

        private IEnumerator WaitAndTalk()
        {
            float startedAt = Time.unscaledTime;
            DialogueSystem dialogue = DialogueSystem.Instance;
            while (!ShouldStartDialogue(Time.unscaledTime - startedAt, dialogue != null && dialogue.IsPlaying))
            {
                yield return null;
                dialogue = DialogueSystem.Instance;
            }

            // 会話が終わったら OnDialogueFinished が暗転に進む。話題が見つからなければ会話を飛ばす。
            _awaitingDialogue = true;
            if (dialogue == null || !dialogue.PlayTopic(DialogueId, TopicId))
            {
                _awaitingDialogue = false;
                StartCoroutine(Run());
            }
        }

        private void OnDialogueFinished()
        {
            if (!_awaitingDialogue)
            {
                return;
            }

            _awaitingDialogue = false;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            HUD.Instance?.SetGameplayUIVisible(false);
            Time.timeScale = 0f;
            _frozen = true;

            _pages = BuildPages();
            yield return Fade(1f);

            _page = 0;
            ShowPage();
            if (_root != null)
            {
                _root.SetActive(true);
            }

            AudioManager.Instance?.PlayJingle("jingle_day_end");
        }

        private string[] BuildPages()
        {
            bool english = L.IsEnglish;
            GameManager manager = GameManager.Instance;
            DayEndEvaluator evaluator = FindAnyObjectByType<DayEndEvaluator>();
            return new[]
            {
                RecapText(manager != null ? manager.DayNumber : DayRestart.FirstDay,
                    evaluator != null ? evaluator.Evaluate() : null, english),
                PeopleText(new List<string>(DayStats.TalkedIds), english,
                    id => L.Get(DialogueData.SpeakerKeyPrefix + id, id)),
                CreditsView.BuildBody()
            };
        }

        private void ShowPage()
        {
            bool english = L.IsEnglish;
            if (_heading != null)
            {
                _heading.text = HeadingText(_page, english);
            }

            if (_body != null)
            {
                _body.text = _pages != null && _page < _pages.Length ? _pages[_page] : string.Empty;
                ApplyWrap();
            }

            PaintChoice(english);
            _showing = true;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>次のページへ送る（テスト・スモーク用）。入力の待ち時間は見ない。ページを出していなければ何もしない。</summary>
        public void AdvancePage()
        {
            if (_showing)
            {
                NextPage();
            }
        }

        /// <summary>
        /// page の本文を折り返すか。クレジットは長い 1 行が多く、折り返すと 1 文字だけ次の行に落ちるので、
        /// 横長では折り返さずに文字を縮める。縦長の画面は幅が狭く、折り返さないと文字が 8 px ほどまで縮む (#187)。
        /// </summary>
        public static bool WrapsBody(int page, bool portrait)
        {
            return portrait || page < PageCount - 1;
        }

        private void ApplyWrap()
        {
            _wrapForPortrait = ScreenShape.IsPortrait;
            if (_body != null)
            {
                _body.textWrappingMode = WrapsBody(_page, _wrapForPortrait)
                    ? TextWrappingModes.Normal
                    : TextWrappingModes.NoWrap;
            }
        }

        private void PaintChoice(bool english)
        {
            _choiceForTouch = InputMode.IsTouch;
            if (_choice != null)
            {
                _choice.text = ChoiceText(_page, english, _choiceForTouch);
            }
        }

        private void NextPage()
        {
            AudioManager.Instance?.PlayUi("ui_confirm");
            if (_page < PageCount - 1)
            {
                _page++;
                ShowPage();
                return;
            }

            _showing = false;
            StartCoroutine(Close());
        }

        /// <summary>
        /// 最後のページを閉じてキャンパスに戻す。パネルを消してから暗転を明け、明け切ってから時間と操作を返す。
        /// 掛け金（<see cref="IsAnyShowing"/>）を落とすのは最後。先に落とすと、明けている途中で
        /// ポーズや一日の終わりのリザルトが開く。
        /// </summary>
        private IEnumerator Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            yield return Fade(0f);

            Time.timeScale = 1f;
            _frozen = false;
            KCDInput.Unblock(this);
            _active = false;
            IsAnyShowing = false;
            HUD.Instance?.SetGameplayUIVisible(true);
            // ページを閉じた Enter / E で、そのまま近くの NPC に話しかけないようにする。
            KCDInput.MarkModalClosed();
            AutoSave.Request();
        }

        private IEnumerator Fade(float target)
        {
            if (_fade == null)
            {
                yield break;
            }

            float start = _fade.alpha;
            float elapsed = 0f;
            while (elapsed < FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _fade.alpha = Mathf.Lerp(start, target, elapsed / FadeSeconds);
                yield return null;
            }

            _fade.alpha = target;
        }

        /// <summary>暗転用の真っ黒な板。DormEnding のものと同じ作り。</summary>
        private CanvasGroup BuildFadeOverlay()
        {
            var go = new GameObject("FormalEndingFade");
            go.transform.SetParent(transform, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var panel = new GameObject("Black", typeof(RectTransform));
            panel.transform.SetParent(go.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = panel.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return group;
        }
    }
}
