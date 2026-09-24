using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 隠しアイテム・写真スポット・称号の一覧（Assets/Data/Collectibles/collectibles.json を
    /// DataBundler が Resources/KCD/Collectibles へ写したもの）。
    ///
    /// 結果画面の「隠しアイテム」「写真」と称号は、ここに載っている id だけを数える (#65)。
    /// 牛乳や葉のようなクエストの拾い物は載っていないので数えない（以前は数えていて 2/20 で頭打ちになった）。
    /// クエストの報酬（source = quest）は載っているが、隠しアイテムの 20 個には入らない。
    /// </summary>
    public sealed class CollectibleCatalog
    {
        public const string ResourcePath = "KCD/Collectibles/collectibles";

        /// <summary>キャンパスに隠してある物。</summary>
        public const string SourceHidden = "hidden";

        /// <summary>クエストの報酬でもらう物。</summary>
        public const string SourceQuest = "quest";

        private static CollectibleCatalog _instance;

        private readonly List<CatalogItem> _items = new List<CatalogItem>();
        private readonly List<CatalogPhotoSpot> _photoSpots = new List<CatalogPhotoSpot>();
        private readonly List<CatalogAchievement> _achievements = new List<CatalogAchievement>();
        private readonly Dictionary<string, CatalogItem> _itemById = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogPhotoSpot> _spotById = new Dictionary<string, CatalogPhotoSpot>(StringComparer.Ordinal);

        /// <summary>Resources から読んだ一覧。初めて使うときに読む。読めなければ空の一覧。</summary>
        public static CollectibleCatalog Instance => _instance ??= LoadFromResources();

        public IReadOnlyList<CatalogItem> Items => _items;
        public IReadOnlyList<CatalogPhotoSpot> PhotoSpots => _photoSpots;
        public IReadOnlyList<CatalogAchievement> Achievements => _achievements;

        /// <summary>隠しアイテムの数（source = hidden）。</summary>
        public int HiddenCount { get; private set; }

        /// <summary>写真スポットの数。</summary>
        public int PhotoSpotCount => _photoSpots.Count;

        /// <summary>JSON 文字列から作る（テストから呼ぶ）。壊れていれば空の一覧。</summary>
        public static CollectibleCatalog Parse(string json)
        {
            var catalog = new CollectibleCatalog();
            if (string.IsNullOrEmpty(json))
            {
                return catalog;
            }

            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
            {
                return catalog;
            }

            foreach (object node in MiniJson.GetArray(root, "collectibles"))
            {
                catalog.AddItem(CatalogItem.Read(node as Dictionary<string, object>));
            }

            foreach (object node in MiniJson.GetArray(root, "photo_spots"))
            {
                catalog.AddPhotoSpot(CatalogPhotoSpot.Read(node as Dictionary<string, object>));
            }

            foreach (object node in MiniJson.GetArray(root, "achievements"))
            {
                CatalogAchievement achievement = CatalogAchievement.Read(node as Dictionary<string, object>);
                if (achievement != null)
                {
                    catalog._achievements.Add(achievement);
                }
            }

            return catalog;
        }

        private static CollectibleCatalog LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[KCD] 収集物の一覧が見つかりません: Resources/" + ResourcePath);
                return new CollectibleCatalog();
            }

            try
            {
                return Parse(asset.text);
            }
            catch (FormatException error)
            {
                Debug.LogError("[KCD] 収集物の一覧を解釈できません: " + error.Message);
                return new CollectibleCatalog();
            }
        }

        private void AddItem(CatalogItem item)
        {
            if (item == null || _itemById.ContainsKey(item.Id))
            {
                return;
            }

            _items.Add(item);
            _itemById[item.Id] = item;
            if (item.IsHidden)
            {
                HiddenCount++;
            }
        }

        private void AddPhotoSpot(CatalogPhotoSpot spot)
        {
            if (spot == null || _spotById.ContainsKey(spot.Id))
            {
                return;
            }

            _photoSpots.Add(spot);
            _spotById[spot.Id] = spot;
        }

        /// <summary>id の物。一覧に無ければ null（牛乳・葉など）。</summary>
        public CatalogItem FindItem(string id)
        {
            return !string.IsNullOrEmpty(id) && _itemById.TryGetValue(id, out CatalogItem item) ? item : null;
        }

        /// <summary>id の写真スポット。一覧に無ければ null。</summary>
        public CatalogPhotoSpot FindPhotoSpot(string id)
        {
            return !string.IsNullOrEmpty(id) && _spotById.TryGetValue(id, out CatalogPhotoSpot spot) ? spot : null;
        }

        /// <summary>id がキャンパスに隠してある物か。</summary>
        public bool IsHidden(string id)
        {
            CatalogItem item = FindItem(id);
            return item != null && item.IsHidden;
        }

        /// <summary>ids のうち隠しアイテムの種類数。一覧に無い id とクエストの報酬は数えない。</summary>
        public int CountHidden(IEnumerable<string> ids)
        {
            return CountDistinct(ids, IsHidden);
        }

        /// <summary>ids のうち写真スポットの種類数。自由撮影のファイル名や一覧に無い id は数えない。</summary>
        public int CountPhotoSpots(IEnumerable<string> ids)
        {
            return CountDistinct(ids, id => FindPhotoSpot(id) != null);
        }

        /// <summary>ids のうち、そのレア度の物の種類数（隠しアイテムもクエストの報酬も数える）。</summary>
        public int CountRarity(IEnumerable<string> ids, string rarity)
        {
            return CountDistinct(ids, id =>
            {
                CatalogItem item = FindItem(id);
                return item != null && item.Rarity == rarity;
            });
        }

        private static int CountDistinct(IEnumerable<string> ids, Func<string, bool> counts)
        {
            if (ids == null)
            {
                return 0;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id) && counts(id))
                {
                    seen.Add(id);
                }
            }

            return seen.Count;
        }
    }
}
