using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 拾った物の数と、collect ステップの追いつき (#65)。
    ///
    /// 以前は拾った瞬間に受注中のクエストへ報告するだけだったので、受注する前に拾った物は数えられなかった。
    /// 拾った物はその場から消え、隠しアイテムは拾い直せない（CollectableItem が出し直さない）ので、
    /// 先に拾ってしまうと実験ノート（c_lab_goggles）や牛乳・理科大グリーンの葉のクエストは達成できなくなっていた。
    /// いまは拾った数を覚えておき、collect ステップが今のステップになったとき（受注・前のステップの達成・
    /// セーブの読み込み）に、持っている数まで進める。
    /// </summary>
    public sealed partial class QuestSystem
    {
        /// <summary>このセッションで拾った数（アイテム id ごと）。「はじめから」（Load）でだけ消す。</summary>
        private readonly Dictionary<string, int> _held = new Dictionary<string, int>();

        /// <summary>報告を処理している入れ子の深さ。0 より大きいあいだに受注したクエストを覚える。</summary>
        private int _reportDepth;

        /// <summary>
        /// 報告の最中に受注したクエスト。受注のときの追いつきで今回拾った 1 個はもう数えているので、
        /// 同じ報告のループでもう 1 度数えない。
        /// </summary>
        private readonly HashSet<string> _activatedDuringReport = new HashSet<string>();

        /// <summary>このセッションで拾った数。受注前に拾った分も含む。</summary>
        public int HeldCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            return _held.TryGetValue(itemId, out int count) ? count : 0;
        }

        private void NoteHeld(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            _held.TryGetValue(itemId, out int count);
            _held[itemId] = count + 1;
        }

        /// <summary>
        /// 今のステップが collect なら、持っている数まで進める。数がそろえば達成にして次のステップへ進める。
        /// 制限時間つきのステップは、計時中に拾った分だけを数える（前もって集めておけば済む挑戦にしない）。
        /// </summary>
        private void CatchUpHeld(QuestData quest)
        {
            if (quest == null || !_active.Contains(quest.Id))
            {
                return;
            }

            QuestStep step = quest.CurrentStep;
            if (step == null || step.Kind != QuestStepKind.Collect || step.IsTimed)
            {
                return;
            }

            int held = Mathf.Min(step.Count, HeldCount(step.Target));
            if (held <= step.Progress)
            {
                return;
            }

            if (step.MinHour > 0f && CurrentHour() < step.MinHour)
            {
                return;
            }

            step.Progress = held;
            if (step.Progress >= step.Count)
            {
                CompleteStep(quest, step);
            }
        }

        /// <summary>受注中のすべてのクエストを追いつかせる。セーブの読み込みのあとに呼ぶ。</summary>
        private void CatchUpAllHeld()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                CatchUpHeld(_all[i]);
            }
        }
    }
}
