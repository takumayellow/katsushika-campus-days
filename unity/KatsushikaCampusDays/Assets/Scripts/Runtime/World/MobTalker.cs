using UnityEngine;

namespace KCD
{
    /// <summary>
    /// モブの学生に話しかけたときの返事。会話ウィンドウは開かず、時間帯に合った一言か会釈を
    /// トーストで出して、少しだけ立ち止まってこちらを向く (#22)。クエストの「会った」には数えない。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class MobTalker : Interactable
    {
        /// <summary>時間帯ごとの台詞の数（ui.mob.line.&lt;band&gt;.0 と .1）。</summary>
        public const int LinesPerBand = 2;

        /// <summary>台詞ではなく会釈だけ返す割合。</summary>
        public const float NodChance = 0.34f;

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

            HUD hud = HUD.Instance;
            if (hud != null)
            {
                hud.ShowToast(PickLine(_walker.BandId, Random.value, Random.Range(0, LinesPerBand)), false);
            }
        }

        public static string LineKey(string bandId, int index)
        {
            return "ui.mob.line." + bandId + "." + index;
        }

        /// <summary>
        /// 返事を選ぶ。nodRoll（0〜1）が <see cref="NodChance"/> 未満か、時間帯が分からないか、
        /// その時間帯の台詞が辞書に無ければ会釈。そうでなければ lineIndex 番目の台詞をかぎ括弧で包む。
        /// </summary>
        public static string PickLine(string bandId, float nodRoll, int lineIndex)
        {
            string nod = L.Get("ui.mob.nod", "（軽く会釈してくれた）");
            if (string.IsNullOrEmpty(bandId) || nodRoll < NodChance)
            {
                return nod;
            }

            string line = L.Get(LineKey(bandId, Mathf.Clamp(lineIndex, 0, LinesPerBand - 1)), string.Empty);
            return string.IsNullOrEmpty(line) ? nod : L.Format("ui.mob.say", line);
        }
    }
}
