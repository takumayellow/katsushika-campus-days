using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 裏エンド「寮でぐーたら」(#41)。寮長との会話が <see cref="DormRoute.FlagId"/> を立てたら、
    /// 暗転して結果を出し、Enter でタイトルへ戻る。
    ///
    /// 本編のリザルト（<see cref="ResultScreen"/>）とは別物で、ランクも達成率も出さない。
    /// 出すのは時刻だけ。日付（<c>GameManager.DayNumber</c>）も進めない。サボった一日は数えない。
    ///
    /// 時間を止める順番に気をつけること。<c>KCDInput.Block(this)</c> を掛けると
    /// <c>KCDInput.GameplayBlocked</c> が true になり、<see cref="DayEndEvaluator"/> の Update が
    /// 先頭で戻るので、裏エンドの最中に一日の終わりのリザルトが割り込むことはない。
    /// </summary>
    public sealed class DormEnding : MonoBehaviour
    {
        /// <summary>暗転にかける秒数。InteriorLoader の 0.35 秒より長く、終わりらしく落とす。</summary>
        public const float FadeSeconds = 0.9f;

        /// <summary>出した直後に入力を捨てる秒数。会話を送った Enter でそのまま閉じないため。</summary>
        public const float InputGuardSeconds = 0.6f;

        /// <summary>暗転の Canvas の重ね順。InteriorLoader の暗転（100）より手前。</summary>
        public const int SortingOrder = 101;

        /// <summary>結果の Canvas の重ね順。自分の暗転よりさらに手前（DormEndingFactory が使う）。</summary>
        public const int PanelSortingOrder = 102;

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private TMP_Text _choice;

        private CanvasGroup _fade;
        private bool _played;
        private bool _subscribed;
        private bool _showing;
        private bool _active;
        private bool _frozen;
        private float _openedAt;

        /// <summary>結果を出している最中か。</summary>
        public bool IsShowing => _showing;

        /// <summary>
        /// どこかで裏エンドが動いている最中か。<see cref="IsShowing"/> と違い、結果パネルが出る前の
        /// 暗転（<see cref="FadeSeconds"/> 秒）も含む。
        ///
        /// <see cref="ResultScreen.IsAnyOpen"/> と同じ役目。<c>KCDInput.Block</c> が止めるのは
        /// <c>GameplayBlocked</c> を見ている側だけで、メニュー（Esc）とクエストログ（Tab）は見ていない。
        /// 見張らないと、暗転の裏でポーズが開き、閉じたときに <c>PauseMenu.SetOpen(false)</c> が
        /// <c>Time.timeScale = 1</c> に戻してしまう（＝裏エンドの最中に時間が動き出す）。
        /// </summary>
        public static bool IsAnyShowing { get; private set; }

        /// <summary>もう出したか。一度出したらこのシーンでは二度と出さない。</summary>
        public bool HasPlayed => _played;

        // ---- 純関数（Unity を起動せずにテストする）----

        /// <summary>
        /// 裏エンドを出すか。フラグは一度立つと会話のたびに true のままなので、
        /// 「まだ出していない」を併せて見ないと会話を終えるたびに出てしまう。
        /// </summary>
        public static bool ShouldPlay(bool alreadyPlayed, bool flagSet)
        {
            return !alreadyPlayed && flagSet;
        }

        /// <summary>
        /// ゲーム内時刻（時間単位の実数）を 24 時間の時計にする。
        /// 0 未満や 24 以上は一日ぶん回して丸める（23:59:40 は 24:00 に丸まって 00:00 になる）。
        /// </summary>
        public static string FormatClock(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours))
            {
                hours = 0f;
            }

            int minutes = Mathf.RoundToInt(hours * 60f);
            minutes = ((minutes % 1440) + 1440) % 1440;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        /// <summary>見出し。</summary>
        public static string HeadingText(bool english)
        {
            return english ? "Slacked Off at the Dorm" : "寮でぐーたら";
        }

        /// <summary>
        /// 本文。数字は時刻だけ。探索率も入った建物の数も出さない（この一日は集計しない）。
        /// </summary>
        public static string BodyText(float hours, bool english)
        {
            string clock = FormatClock(hours);
            return english
                ? "You crawled under the covers at the dorm and never went to class.\n\n"
                  + "<size=200%>" + clock + "</size>\n\n"
                  + "<size=85%>Today does not get counted.</size>"
                : "寮の布団にもぐりこんで、そのまま授業をサボった。\n\n"
                  + "<size=200%>" + clock + "</size>\n\n"
                  + "<size=85%>この一日は数えない。</size>";
        }

        /// <summary>下段の案内。</summary>
        public static string ChoiceText(bool english)
        {
            return english ? "Enter — back to the title" : "Enter でタイトルへ";
        }

        // ---- MonoBehaviour ----

        /// <summary>SceneBuilder（DormEndingFactory）から差し込む。</summary>
        public void Bind(GameObject root, TMP_Text heading, TMP_Text body, TMP_Text choice)
        {
            _root = root;
            _heading = heading;
            _body = body;
            _choice = choice;
        }

        private void Awake()
        {
            _fade = BuildFadeOverlay();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_subscribed && DialogueSystem.Instance != null)
            {
                DialogueSystem.Instance.Finished -= OnDialogueFinished;
            }

            _subscribed = false;
            KCDInput.Unblock(this);

            if (_fade != null)
            {
                _fade.alpha = 0f;
            }

            _showing = false;
            if (_active)
            {
                _active = false;
                IsAnyShowing = false;
            }

            // 出している最中に消されても、止めた時間は必ず戻す。
            // ここで見るのは _showing ではなく _frozen。_showing が立つのは暗転が終わって
            // 結果パネルを出したあとなので、_showing で判定すると暗転の 0.9 秒のあいだに
            // 消された場合に timeScale = 0 のまま取り残される（シーンを跨いで固まる）。
            if (_frozen)
            {
                _frozen = false;
                Time.timeScale = 1f;
            }
        }

        private void Update()
        {
            if (!_subscribed)
            {
                // DialogueSystem のほうが後に Awake することがあるので、掴めるまで試し続ける。
                TrySubscribe();
            }

            if (!_showing || Time.unscaledTime - _openedAt < InputGuardSeconds)
            {
                return;
            }

            if (KCDInput.SubmitPressed || KCDInput.InteractPressed)
            {
                Close();
            }
        }

        private void TrySubscribe()
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null)
            {
                return;
            }

            dialogue.Finished += OnDialogueFinished;
            _subscribed = true;
        }

        private void OnDialogueFinished()
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null)
            {
                return;
            }

            if (ShouldPlay(_played, dialogue.HasFlag(DormRoute.FlagId)))
            {
                Play();
            }
        }

        /// <summary>フラグを見ずに裏エンドを出す（デバッグ用）。二度目は何もしない。</summary>
        public void Play()
        {
            if (_played || !isActiveAndEnabled)
            {
                return;
            }

            _played = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // 会話の封鎖（DialogueSystem）はもう外れているので、自分の封鎖を掛け直す (#40)。
            // GameplayBlocked が立つので DayEndEvaluator は動かない。
            KCDInput.Block(this);
            _active = true;
            IsAnyShowing = true;
            HUD.Instance?.SetGameplayUIVisible(false);

            // 暗転の前に時間を止める。止めないと暗転の 0.9 秒ぶん時計が進み、
            // 画面に出す時刻が「寮長と話し終えた時刻」からずれる。
            float hours = GameManager.Instance.GameTimeHours;
            Time.timeScale = 0f;
            _frozen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            AudioManager.Instance?.PlaySe("door_close", 0.8f);

            yield return Fade(1f);

            Paint(hours);
            if (_root != null)
            {
                _root.SetActive(true);
            }

            _showing = true;
            _openedAt = Time.unscaledTime;
            AudioManager.Instance?.PlayJingle("jingle_day_end");
        }

        private void Close()
        {
            _showing = false;
            _active = false;
            IsAnyShowing = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }

            if (_fade != null)
            {
                _fade.alpha = 0f;
            }

            AudioManager.Instance?.PlayUi("ui_confirm");
            KCDInput.Unblock(this);
            KCDInput.MarkModalClosed();
            // 座っている途中で裏エンドに入った場合に備えて、シーンをまたぐ移動ロックも外しておく。
            KCDInput.MovementLocked = false;

            // 日付は進めない。ReturnToTitle が timeScale と封鎖を戻す。
            // _frozen を落とすのはそのあと。ここへ来る前に落ちた場合は OnDisable が戻す。
            GameManager.Instance.ReturnToTitle();
            _frozen = false;
        }

        private void Paint(float hours)
        {
            bool english = L.IsEnglish;

            if (_heading != null)
            {
                _heading.text = HeadingText(english);
            }

            if (_body != null)
            {
                _body.text = BodyText(hours, english);
            }

            if (_choice != null)
            {
                _choice.text = ChoiceText(english);
            }
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

        /// <summary>暗転用の真っ黒な板。InteriorLoader のものと同じ作りで、重ね順だけ 1 つ手前。</summary>
        private CanvasGroup BuildFadeOverlay()
        {
            var go = new GameObject("DormEndingFade");
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
