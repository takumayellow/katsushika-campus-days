using UnityEngine;

namespace KCD
{
    /// <summary>
    /// クエストの現在ステップが「どこへ行けばいいか」をシーン内の物から引く。
    /// ミニマップの目的地マーカーが使う。探索結果は 1 秒だけ覚えて、毎フレーム探さない。
    /// </summary>
    public static class QuestObjectiveLocator
    {
        private const float RefreshInterval = 1f;

        private static float _refreshedAt = -10f;
        private static NPCTalker[] _npcs = new NPCTalker[0];
        private static EntranceTrigger[] _entrances = new EntranceTrigger[0];
        private static VisitZone[] _zones = new VisitZone[0];
        private static CollectableItem[] _items = new CollectableItem[0];
        private static PropInteractable[] _props = new PropInteractable[0];

        /// <summary>ステップの目的地を返す。見つからなければ false。</summary>
        public static bool TryLocate(QuestStep step, Vector3 from, out Vector3 position)
        {
            position = Vector3.zero;
            if (step == null || string.IsNullOrEmpty(step.Target))
            {
                return false;
            }

            Refresh();

            // 制限時間つきで時間切れ（またはロード直後）なら、目的地ではなく再挑戦を受け付ける依頼主を指す。
            QuestStepKind kind = step.Kind;
            string target = step.Target;
            if (step.AwaitingGiver)
            {
                kind = QuestStepKind.Talk;
                target = step.Giver;
            }

            switch (kind)
            {
                case QuestStepKind.Talk:
                    return Nearest(_npcs, from, n => n.NpcId == target, out position);
                case QuestStepKind.Enter:
                    return Nearest(_entrances, from, e => e.BuildingId == target, out position);
                case QuestStepKind.Visit:
                    return Nearest(_zones, from, z => z.PlaceId == target, out position);
                case QuestStepKind.Collect:
                    return Nearest(_items, from, i => i.ItemId == target && i.CanInteract, out position);
                case QuestStepKind.Flag:
                    return Nearest(_props, from, p => p.FlagId == target, out position);
                default:
                    return false;
            }
        }

        /// <summary>シーンが変わったときなどに、次回の検索でリストを取り直させる。</summary>
        public static void Invalidate()
        {
            _refreshedAt = -10f;
        }

        private static void Refresh()
        {
            if (Time.unscaledTime - _refreshedAt < RefreshInterval)
            {
                return;
            }

            _refreshedAt = Time.unscaledTime;
            _npcs = Object.FindObjectsByType<NPCTalker>(FindObjectsSortMode.None);
            _entrances = Object.FindObjectsByType<EntranceTrigger>(FindObjectsSortMode.None);
            _zones = Object.FindObjectsByType<VisitZone>(FindObjectsSortMode.None);
            _items = Object.FindObjectsByType<CollectableItem>(FindObjectsSortMode.None);
            _props = Object.FindObjectsByType<PropInteractable>(FindObjectsSortMode.None);
        }

        private static bool Nearest<T>(
            T[] candidates, Vector3 from, System.Func<T, bool> match, out Vector3 position) where T : Component
        {
            position = Vector3.zero;
            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                T candidate = candidates[i];
                if (candidate == null || !match(candidate))
                {
                    continue;
                }

                float distance = (candidate.transform.position - from).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    position = candidate.transform.position;
                    found = true;
                }
            }

            return found;
        }
    }
}
