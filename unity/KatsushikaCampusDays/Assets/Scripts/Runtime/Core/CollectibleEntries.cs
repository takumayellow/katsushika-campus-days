using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 置き場所。屋外は campus.json と同じ (x = 東 m, z = 北 m)、屋内は Models/Interiors/&lt;building&gt;.json の poi_ 名。
    /// クエストの報酬のように置かれない物は両方とも空。
    /// </summary>
    public readonly struct CatalogPlace
    {
        private readonly string _building;
        private readonly string _poi;

        public CatalogPlace(string building, string poi, bool outdoor, Vector2 xz)
        {
            _building = building;
            _poi = poi;
            IsOutdoor = outdoor;
            XZ = xz;
        }

        /// <summary>屋内なら建物 id。屋外は空。</summary>
        public string Building => _building ?? string.Empty;

        /// <summary>屋内の置き場所（poi_ の Empty 名）。屋外は空。</summary>
        public string Poi => _poi ?? string.Empty;

        /// <summary>屋外の座標を持つか。</summary>
        public bool IsOutdoor { get; }

        /// <summary>屋外の座標 (x, z)。</summary>
        public Vector2 XZ { get; }

        /// <summary>屋内の poi に置くか。</summary>
        public bool IsIndoor => !string.IsNullOrEmpty(Building) && !string.IsNullOrEmpty(Poi);

        /// <summary>どこかに置く物か（クエストの報酬は置かない）。</summary>
        public bool IsPlaced => IsOutdoor || IsIndoor;

        /// <summary>building と position（{x, z} か poi_ 名か null）を読む。</summary>
        public static CatalogPlace Read(Dictionary<string, object> node)
        {
            string building = MiniJson.GetString(node, "building");
            object position = null;
            node?.TryGetValue("position", out position);

            if (position is Dictionary<string, object> point)
            {
                var xz = new Vector2(MiniJson.GetFloat(point, "x"), MiniJson.GetFloat(point, "z"));
                return new CatalogPlace(building, string.Empty, true, xz);
            }

            return new CatalogPlace(building, position as string, false, Vector2.zero);
        }
    }

    /// <summary>拾える物 1 件（collectibles.json の collectibles）。</summary>
    public sealed class CatalogItem
    {
        public string Id = string.Empty;
        public string NameJa = string.Empty;
        public string NameEn = string.Empty;
        public string HintJa = string.Empty;
        public string HintEn = string.Empty;
        public string Rarity = string.Empty;

        /// <summary>hidden = キャンパスに隠してある、quest = クエストの報酬でもらう。</summary>
        public string Source = string.Empty;

        public CatalogPlace Place;

        /// <summary>キャンパスに隠してある物か。結果画面と称号はこれだけを数える。</summary>
        public bool IsHidden => Source == CollectibleCatalog.SourceHidden;

        /// <summary>表示名。ローカライズの item.&lt;id&gt;.name、無ければ JSON の name_ja / name_en。</summary>
        public string DisplayName => L.Get("item." + Id + ".name", L.Pick(NameJa, NameEn));

        public static CatalogItem Read(Dictionary<string, object> node)
        {
            string id = MiniJson.GetString(node, "id");
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return new CatalogItem
            {
                Id = id,
                NameJa = MiniJson.GetString(node, "name_ja", id),
                NameEn = MiniJson.GetString(node, "name_en"),
                HintJa = MiniJson.GetString(node, "hint_ja"),
                HintEn = MiniJson.GetString(node, "hint_en"),
                Rarity = MiniJson.GetString(node, "rarity"),
                Source = MiniJson.GetString(node, "source"),
                Place = CatalogPlace.Read(node)
            };
        }
    }

    /// <summary>写真スポット 1 件（collectibles.json の photo_spots）。</summary>
    public sealed class CatalogPhotoSpot
    {
        public string Id = string.Empty;
        public string NameJa = string.Empty;
        public string NameEn = string.Empty;
        public string CaptionJa = string.Empty;
        public string CaptionEn = string.Empty;
        public CatalogPlace Place;

        /// <summary>撮る向き (x, z)。長さは 1 とは限らない。</summary>
        public Vector2 LookDir;

        /// <summary>表示名。ローカライズの photo.&lt;id&gt;.name、無ければ JSON の name_ja / name_en。</summary>
        public string DisplayName => L.Get("photo." + Id + ".name", L.Pick(NameJa, NameEn));

        public static CatalogPhotoSpot Read(Dictionary<string, object> node)
        {
            string id = MiniJson.GetString(node, "id");
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Dictionary<string, object> look = MiniJson.GetObject(node, "look_dir");
            return new CatalogPhotoSpot
            {
                Id = id,
                NameJa = MiniJson.GetString(node, "name_ja", id),
                NameEn = MiniJson.GetString(node, "name_en"),
                CaptionJa = MiniJson.GetString(node, "caption_ja"),
                CaptionEn = MiniJson.GetString(node, "caption_en"),
                Place = CatalogPlace.Read(node),
                LookDir = new Vector2(MiniJson.GetFloat(look, "x"), MiniJson.GetFloat(look, "z"))
            };
        }
    }

    /// <summary>称号 1 件（collectibles.json の achievements）。条件の読み方は AchievementBook が持つ。</summary>
    public sealed class CatalogAchievement
    {
        public string Id = string.Empty;
        public string NameJa = string.Empty;
        public string NameEn = string.Empty;
        public string DescJa = string.Empty;
        public string DescEn = string.Empty;

        /// <summary>condition.type（collect_count / rarity_count / photo_count / building_count / npc_count / quest_count / quest_complete）。</summary>
        public string ConditionType = string.Empty;

        /// <summary>condition.value が数のとき、その値。</summary>
        public int Count;

        /// <summary>condition.value が文字列のとき、その値（quest_complete のクエスト id）。</summary>
        public string TargetId = string.Empty;

        /// <summary>condition.rarity（rarity_count のとき）。</summary>
        public string Rarity = string.Empty;

        /// <summary>condition.scope（quest_count で main ならサブクエストを数えない）。</summary>
        public string Scope = string.Empty;

        /// <summary>condition.npcs（npc_count で数える NPC の id）。</summary>
        public readonly List<string> Npcs = new List<string>();

        /// <summary>表示名。ローカライズの ach.&lt;id&gt;.name、無ければ JSON の name_ja / name_en。</summary>
        public string DisplayName => L.Get("ach." + Id + ".name", L.Pick(NameJa, NameEn));

        public static CatalogAchievement Read(Dictionary<string, object> node)
        {
            string id = MiniJson.GetString(node, "id");
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Dictionary<string, object> condition = MiniJson.GetObject(node, "condition");
            object value = null;
            condition?.TryGetValue("value", out value);

            var achievement = new CatalogAchievement
            {
                Id = id,
                NameJa = MiniJson.GetString(node, "name_ja", id),
                NameEn = MiniJson.GetString(node, "name_en"),
                DescJa = MiniJson.GetString(node, "desc_ja"),
                DescEn = MiniJson.GetString(node, "desc_en"),
                ConditionType = MiniJson.GetString(condition, "type"),
                Count = MiniJson.GetInt(condition, "value"),
                TargetId = value as string ?? string.Empty,
                Rarity = MiniJson.GetString(condition, "rarity"),
                Scope = MiniJson.GetString(condition, "scope")
            };
            achievement.Npcs.AddRange(MiniJson.ToStringList(MiniJson.GetArray(condition, "npcs")));
            return achievement;
        }
    }
}
