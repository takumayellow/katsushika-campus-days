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
                    return LocateVisit(target, from, out position);
                case QuestStepKind.Collect:
                    return LocateCollect(target, from, out position);
                case QuestStepKind.Flag:
                    return Nearest(_props, from, p => p.FlagId == target, out position);
                default:
                    return false;
            }
        }

        /// <summary>屋内を丸ごと入れてある入れ物の名前の接頭辞（InteriorStage が付ける）。</summary>
        public const string InteriorPrefix = "Interior_";

        /// <summary>"Interior_library" → "library"。屋内の入れ物でなければ null。</summary>
        public static string InteriorIdFromName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith(InteriorPrefix)
                ? name.Substring(InteriorPrefix.Length)
                : null;
        }

        /// <summary>
        /// visit ステップの行き先。屋内のクエスト地点（poi_&lt;建物&gt;_…）は、外にいるあいだは
        /// その建物の入口を指す。
        ///
        /// 屋内はキャンパスから遠く離れた場所に建ててあって、入口を踏むとワープで運ばれる。
        /// 地点をそのまま指すとミニマップの矢印が誰も歩けない方角を向くので、
        /// 「図書館で探すミッションの目的地がどこか全く分からない」ことになっていた（#54）。
        /// </summary>
        private static bool LocateVisit(string target, Vector3 from, out Vector3 position)
        {
            string building = InteriorOwnerOf(target);
            bool inside = InteriorLoader.Instance != null && InteriorLoader.Instance.CurrentId == building;
            if (building != null && !inside)
            {
                return Nearest(_entrances, from, e => e.BuildingId == building, out position);
            }

            return Nearest(_zones, from, z => z.PlaceId == target, out position);
        }

        /// <summary>
        /// collect ステップの行き先。いまいる所（屋内ならその建物、外ならキャンパス）の物を先に指す。
        /// そこに無ければ、屋内の物はその建物の入口を、外の物はその物を指す。
        /// 第1実験棟のゴーグル（q_sq_lab_notebook）のように屋内に置いた物を、外から指せるようにする (#65)。
        /// </summary>
        private static bool LocateCollect(string target, Vector3 from, out Vector3 position)
        {
            string current = InteriorLoader.Instance != null ? InteriorLoader.Instance.CurrentId : null;
            string here = string.IsNullOrEmpty(current) ? null : current;
            if (Nearest(_items, from, i => i.ItemId == target && i.CanInteract && InteriorOwnerOf(i.transform) == here,
                    out position))
            {
                return true;
            }

            position = Vector3.zero;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _items.Length; i++)
            {
                CollectableItem item = _items[i];
                if (item == null || item.ItemId != target || !item.CanInteract)
                {
                    continue;
                }

                string owner = InteriorOwnerOf(item.transform);
                Vector3 goal = item.transform.position;
                if (owner != null && !Nearest(_entrances, from, e => e.BuildingId == owner, out goal))
                {
                    continue;
                }

                float distance = (goal - from).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    position = goal;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>その地点がどの屋内に属しているか。屋外のゾーンなら null。</summary>
        private static string InteriorOwnerOf(string placeId)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (_zones[i] == null || _zones[i].PlaceId != placeId)
                {
                    continue;
                }

                return InteriorOwnerOf(_zones[i].transform);
            }

            return null;
        }

        /// <summary>その物がどの屋内に入っているか（親をたどって Interior_ を探す）。外なら null。</summary>
        public static string InteriorOwnerOf(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                string id = InteriorIdFromName(t.name);
                if (id != null)
                {
                    return id;
                }
            }

            return null;
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
