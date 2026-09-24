using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 一日の歩き方の記録。入った建物・拾った物・撮った写真スポットを種類で数える。
    /// リザルト画面がここから達成率を作る。セーブにも載せ、つづきからで戻す (#53, #61)。
    /// </summary>
    public static class DayStats
    {
        private static readonly HashSet<string> _buildings = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _collected = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _photoSpots = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>入った建物の種類数。</summary>
        public static int BuildingCount => _buildings.Count;

        /// <summary>拾った物の種類数。</summary>
        public static int CollectedCount => _collected.Count;

        /// <summary>撮影した写真スポットの種類数。</summary>
        public static int PhotoSpotCount => _photoSpots.Count;

        /// <summary>建物に入った。同じ建物は 1 回だけ数える。</summary>
        public static void NoteEnter(string buildingId)
        {
            if (!string.IsNullOrEmpty(buildingId))
            {
                _buildings.Add(buildingId);
            }
        }

        /// <summary>物を拾った。同じ id は 1 回だけ数える。</summary>
        public static void NoteCollect(string itemId)
        {
            if (!string.IsNullOrEmpty(itemId))
            {
                _collected.Add(itemId);
            }
        }

        /// <summary>写真スポット id は ps_ で始まる。自由撮影のファイル名は数えない。</summary>
        public static void NotePhoto(string spotOrFile)
        {
            if (!string.IsNullOrEmpty(spotOrFile) && spotOrFile.StartsWith("ps_", StringComparison.Ordinal))
            {
                _photoSpots.Add(spotOrFile);
            }
        }

        /// <summary>その建物に入ったことがあるか。</summary>
        public static bool HasEntered(string buildingId) => _buildings.Contains(buildingId);

        /// <summary>その物を拾ったことがあるか。</summary>
        public static bool HasCollected(string itemId) => _collected.Contains(itemId);

        /// <summary>タイトルから新規に始めたときに呼ぶ。</summary>
        public static void Reset()
        {
            _buildings.Clear();
            _collected.Clear();
            _photoSpots.Clear();
        }

        /// <summary>セーブ用。入った建物の id（並びは安定させる）。</summary>
        public static List<string> BuildingIds() => Sorted(_buildings);

        /// <summary>セーブ用。拾った物の id。</summary>
        public static List<string> CollectedIds() => Sorted(_collected);

        /// <summary>セーブ用。撮った写真スポットの id。</summary>
        public static List<string> PhotoSpotIds() => Sorted(_photoSpots);

        /// <summary>
        /// セーブから戻す。今の記録は捨てて置き換える。null のリストは空、空の id は数えず、
        /// 写真は記録するときと同じく ps_ で始まるものだけ数える。
        /// </summary>
        public static void Restore(IEnumerable<string> buildings, IEnumerable<string> collected, IEnumerable<string> photoSpots)
        {
            Reset();
            AddAll(buildings, NoteEnter);
            AddAll(collected, NoteCollect);
            AddAll(photoSpots, NotePhoto);
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
