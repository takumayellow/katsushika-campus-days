using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 一日の歩き方の記録。入った建物・拾った物・撮った写真スポットを種類で数える。
    /// リザルト画面がここから達成率を作る。セーブには載せない（起動中のみ）。
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

        /// <summary>入った建物の id。</summary>
        public static IReadOnlyCollection<string> BuildingIds => _buildings;

        /// <summary>拾った物の id。牛乳や葉のようなクエストの拾い物も入る。</summary>
        public static IReadOnlyCollection<string> CollectedIds => _collected;

        /// <summary>撮影した写真スポットの id（ps_ で始まるもの）。</summary>
        public static IReadOnlyCollection<string> PhotoSpotIds => _photoSpots;

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
    }
}
