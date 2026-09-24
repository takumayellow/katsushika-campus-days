using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 一日の歩き方の記録。入った建物・拾った物・撮った写真スポット・話した NPC を種類で数える。
    /// リザルト画面がここから達成率を作り、称号（AchievementBook）もここを見る。セーブにも載せ、つづきからで戻す (#53, #61)。
    /// </summary>
    public static class DayStats
    {
        private static readonly HashSet<string> _buildings = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _collected = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _photoSpots = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _talked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 記録が増えるたびに 1 つ進む数。称号の判定（GameManager）は、これが変わったフレームだけ数え直す。
        /// static のイベントにしないのは、シーンをまたいで購読が残る心配をしないため。
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>入った建物の種類数。</summary>
        public static int BuildingCount => _buildings.Count;

        /// <summary>拾った物の種類数。</summary>
        public static int CollectedCount => _collected.Count;

        /// <summary>撮影した写真スポットの種類数。</summary>
        public static int PhotoSpotCount => _photoSpots.Count;

        /// <summary>入った建物の id。</summary>
        public static IReadOnlyCollection<string> BuildingIds => _buildings;

        /// <summary>拾った物の id。牛乳や葉のようなクエストの拾い物も入る。</summary>
        public static IReadOnlyCollection<string> CollectedIds => _collected;

        /// <summary>撮影した写真スポットの id（ps_ で始まるもの）。</summary>
        public static IReadOnlyCollection<string> PhotoSpotIds => _photoSpots;

        /// <summary>話しかけた NPC の id。</summary>
        public static IReadOnlyCollection<string> TalkedIds => _talked;

        /// <summary>
        /// 拾った隠しアイテムの種類数。結果画面と称号はこれを使う (#65)。
        /// 一覧に無い牛乳・葉と、クエストの報酬は数えない。
        /// </summary>
        public static int HiddenCollectedCount(CollectibleCatalog catalog)
        {
            return catalog != null ? catalog.CountHidden(_collected) : 0;
        }

        /// <summary>撮影した写真スポットのうち、一覧に載っているものの種類数。</summary>
        public static int CatalogPhotoCount(CollectibleCatalog catalog)
        {
            return catalog != null ? catalog.CountPhotoSpots(_photoSpots) : 0;
        }

        /// <summary>建物に入った。同じ建物は 1 回だけ数える。</summary>
        public static void NoteEnter(string buildingId)
        {
            if (!string.IsNullOrEmpty(buildingId) && _buildings.Add(buildingId))
            {
                Version++;
            }
        }

        /// <summary>物を拾った。同じ id は 1 回だけ数える。</summary>
        public static void NoteCollect(string itemId)
        {
            if (!string.IsNullOrEmpty(itemId) && _collected.Add(itemId))
            {
                Version++;
            }
        }

        /// <summary>写真スポット id は ps_ で始まる。自由撮影のファイル名は数えない。</summary>
        public static void NotePhoto(string spotOrFile)
        {
            if (!string.IsNullOrEmpty(spotOrFile) && spotOrFile.StartsWith("ps_", StringComparison.Ordinal) &&
                _photoSpots.Add(spotOrFile))
            {
                Version++;
            }
        }

        /// <summary>NPC に話しかけた。同じ NPC は 1 回だけ数える。称号「顔なじみ」(ach_friends) が使う。</summary>
        public static void NoteTalk(string npcId)
        {
            if (!string.IsNullOrEmpty(npcId) && _talked.Add(npcId))
            {
                Version++;
            }
        }

        /// <summary>その建物に入ったことがあるか。</summary>
        public static bool HasEntered(string buildingId) => _buildings.Contains(buildingId);

        /// <summary>その物を拾ったことがあるか。</summary>
        public static bool HasCollected(string itemId) => _collected.Contains(itemId);

        /// <summary>その NPC に話しかけたことがあるか。</summary>
        public static bool HasTalked(string npcId) => _talked.Contains(npcId);

        /// <summary>タイトルから新規に始めたときに呼ぶ。</summary>
        public static void Reset()
        {
            _buildings.Clear();
            _collected.Clear();
            _photoSpots.Clear();
            _talked.Clear();
            Version++;
        }

        /// <summary>セーブ用。入った建物の id（並びは安定させる）。</summary>
        public static List<string> SortedBuildingIds() => Sorted(_buildings);

        /// <summary>セーブ用。拾った物の id。</summary>
        public static List<string> SortedCollectedIds() => Sorted(_collected);

        /// <summary>セーブ用。撮った写真スポットの id。</summary>
        public static List<string> SortedPhotoSpotIds() => Sorted(_photoSpots);

        /// <summary>セーブ用。話しかけた NPC の id。</summary>
        public static List<string> SortedTalkedIds() => Sorted(_talked);

        /// <summary>
        /// セーブから戻す。今の記録は捨てて置き換える。null のリストは空、空の id は数えず、
        /// 写真は記録するときと同じく ps_ で始まるものだけ数える。
        /// </summary>
        public static void Restore(IEnumerable<string> buildings, IEnumerable<string> collected,
            IEnumerable<string> photoSpots, IEnumerable<string> talked)
        {
            Reset();
            AddAll(buildings, NoteEnter);
            AddAll(collected, NoteCollect);
            AddAll(photoSpots, NotePhoto);
            AddAll(talked, NoteTalk);
        }

        private static List<string> Sorted(HashSet<string> ids)
        {
            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void AddAll(IEnumerable<string> ids, Action<string> note)
        {
            if (ids == null)
            {
                return;
            }

            foreach (string id in ids)
            {
                note(id);
            }
        }
    }
}
