using UnityEngine;

namespace KCD
{
    /// <summary>
    /// モブの学生に話しかけたときの返事。会話ウィンドウは開かず、時間帯に合った一言か会釈を
    /// トーストで出して、少しだけ立ち止まってこちらを向く (#22)。クエストの「会った」には数えない。
    /// 一言は初めて話しかけたときに MobScheduler が選び、見えている人どうしで同じ台詞にならない。
    /// 同じ人に続けて話しかけると会釈を返す。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MobTalker : Interactable
    {
        /// <summary>
        /// 時間帯ごとの台詞の数（ui.mob.line.&lt;band&gt;.0〜.7）。見えている人（MobScheduler.DefaultDrawnCount 人まで）が
        /// みな違う台詞を言えるだけ用意する。
        /// </summary>
        public const int LinesPerBand = 8;

        /// <summary>話しかけられて立ち止まる秒数。</summary>
        public const float HoldSeconds = 1.8f;

        /// <summary>同じ人にもう一度話しかけられるまでの秒数（トーストの連打を防ぐ）。</summary>
        public const float CooldownSeconds = 3f;

        private MobWalker _walker;
        private float _readyAt;

        public override bool CanInteract =>
            isActiveAndEnabled
            && _walker != null
            && _walker.IsRunning
            && _walker.Tier != MobTier.Hidden
            && Time.time >= _readyAt
            && (DialogueSystem.Instance == null || !DialogueSystem.Instance.IsPlaying);

        private void Awake()
        {
            _walker = GetComponent<MobWalker>();
        }

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
            RefreshPrompt();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
        }

        /// <summary>いまの言語で「話す」を出す。言語を切り替えたら出し直す。</summary>
        private void RefreshPrompt()
        {
            PromptLabel = L.Get("ui.interact.talk", "話す");
        }

        public override void Interact(GameObject interactor)
        {
            if (!CanInteract)
            {
                return;
            }

            _readyAt = Time.time + CooldownSeconds;
            Vector3 toward = interactor != null ? interactor.transform.position : transform.position + transform.forward;
            _walker.Hold(HoldSeconds, toward);

            string said;
            // 時間帯の分からない人は台詞を持たないので、番号を借りずに会釈だけ返す。
            if (_walker.LineIndex < 0 && !string.IsNullOrEmpty(_walker.BandId))
            {
                int index = _walker.ClaimLine();
                said = PickLine(_walker.BandId, index);
            }
            else
            {
                said = Nod();
            }

            HUD hud = HUD.Instance;
            if (hud != null)
            {
                hud.ShowToast(said, false);
            }
        }

        public static string LineKey(string bandId, int index)
        {
            return "ui.mob.line." + bandId + "." + index;
        }

        /// <summary>会釈の返事。</summary>
        public static string Nod()
        {
            return L.Get("ui.mob.nod", "（軽く会釈してくれた）");
        }

        /// <summary>
        /// 返事を選ぶ。時間帯が分からないか、その時間帯の台詞が辞書に無ければ会釈。
        /// そうでなければ lineIndex 番目の台詞をかぎ括弧で包む。
        /// </summary>
        public static string PickLine(string bandId, int lineIndex)
        {
            string nod = Nod();
            if (string.IsNullOrEmpty(bandId))
            {
                return nod;
            }

            string line = L.Get(LineKey(bandId, Mathf.Clamp(lineIndex, 0, LinesPerBand - 1)), string.Empty);
            return string.IsNullOrEmpty(line) ? nod : L.Format("ui.mob.say", line);
        }
    }
}
