using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>ワープ先の種類。メニューの見出し（タブ）に対応する。</summary>
    public enum DevWarpKind
    {
        /// <summary>キャンパスの場所（訪問ゾーン）と各棟の入口前。</summary>
        Place,

        /// <summary>建物の中。</summary>
        Interior,

        /// <summary>NPC の前。</summary>
        Npc,

        /// <summary>時刻。</summary>
        Time
    }

    /// <summary>ワープ先 1 件。位置は屋外のワールド座標。屋内と時刻は種類ごとに必要なものだけを持つ。</summary>
    public readonly struct DevWarpTarget
    {
        public DevWarpTarget(DevWarpKind kind, string id, string label, Vector3 position, float yaw,
            bool hasPosition, float hours = 0f)
        {
            Kind = kind;
            Id = id;
            Label = label;
            Position = position;
            Yaw = yaw;
            HasPosition = hasPosition;
            Hours = hours;
        }

        public DevWarpKind Kind { get; }

        /// <summary>ゾーンの placeId / 建物の id / NPC の id / 時刻の名前。</summary>
        public string Id { get; }

        public string Label { get; }

        /// <summary>立つ位置。<see cref="HasPosition"/> が false なら使わない。</summary>
        public Vector3 Position { get; }

        public float Yaw { get; }

        /// <summary>
        /// 位置を持つか。屋内で入口の前が分かっていればその前に立ってから入る。分からなければ今いる場所から入る。
        /// </summary>
        public bool HasPosition { get; }

        /// <summary>時刻の項目で合わせる時刻（時）。</summary>
        public float Hours { get; }
    }

    /// <summary>シーンから拾った訪問ゾーン。Stand は箱の中心の真下（箱の下端）。</summary>
    public readonly struct DevZoneSource
    {
        public DevZoneSource(string id, string name, Vector3 stand)
        {
            Id = id;
            Name = name;
            Stand = stand;
        }

        public string Id { get; }

        public string Name { get; }

        public Vector3 Stand { get; }
    }

    /// <summary>シーンから拾った入口。Door は扉の位置、Outward は建物の外を指す水平な向き。</summary>
    public readonly struct DevEntranceSource
    {
        public DevEntranceSource(string buildingId, string name, Vector3 door, Vector3 outward)
        {
            BuildingId = buildingId;
            Name = name;
            Door = door;
            Outward = outward;
        }

        public string BuildingId { get; }

        public string Name { get; }

        public Vector3 Door { get; }

        public Vector3 Outward { get; }
    }

    /// <summary>InteriorLoader に登録されている屋内。</summary>
    public readonly struct DevInteriorSource
    {
        public DevInteriorSource(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }
    }

    /// <summary>シーンにいる NPC。Forward はその NPC の正面。</summary>
    public readonly struct DevNpcSource
    {
        public DevNpcSource(string id, string name, Vector3 position, Vector3 forward)
        {
            Id = id;
            Name = name;
            Position = position;
            Forward = forward;
        }

        public string Id { get; }

        public string Name { get; }

        public Vector3 Position { get; }

        public Vector3 Forward { get; }
    }

    /// <summary>
    /// 開発者用ワープメニューの行き先の一覧。シーンにあるゾーン・入口・屋内・NPC から作るので、座標を手で書かない。
    /// 並びは id の順で固定する（FindObjectsByType の順は実行ごとに変わる）。
    /// </summary>
    public sealed class DevWarpCatalog
    {
        /// <summary>入口の前に立つ距離（m）。歩き入りの範囲（EntranceTrigger.WalkInDepth）の外。</summary>
        public const float EntranceStandOff = 1.6f;

        /// <summary>NPC の前に立つ距離（m）。NPCTalker に話しかけるときと同じ。</summary>
        public const float NpcStandOff = 1.8f;

        /// <summary>屋内の見どころの訪問ゾーンの id の接頭辞。</summary>
        public const string InteriorPlacePrefix = "poi_";

        /// <summary>時刻の項目（辞書キー, 控えの表示, 時）。朝・昼・夕・夜。</summary>
        public static readonly (string Key, string Fallback, float Hours)[] Times =
        {
            ("morning", "朝 7:00", 7f),
            ("noon", "昼 12:00", 12f),
            ("evening", "夕 17:30", 17.5f),
            ("night", "夜 21:00", 21f)
        };

        private readonly List<DevWarpTarget>[] _tabs;

        private DevWarpCatalog(List<DevWarpTarget>[] tabs)
        {
            _tabs = tabs;
        }

        /// <summary>タブの数（場所 / 建物の中 / 人 / 時刻）。</summary>
        public static int TabCount => Enum.GetValues(typeof(DevWarpKind)).Length;

        /// <summary>タブの項目。</summary>
        public IReadOnlyList<DevWarpTarget> Tab(int index) => _tabs[index];

        /// <summary>全項目の数。</summary>
        public int Count
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _tabs.Length; i++)
                {
                    total += _tabs[i].Count;
                }

                return total;
            }
        }

        /// <summary>
        /// 一覧を作る。場所は「ゾーン → 各棟の入口前」の順。屋内は、同じ建物の入口が見つかっていればその前に立ってから入る。
        /// 同じ id は 1 件にまとめる（id の順で最初の 1 件を残す）。id の空のものは捨てる。
        /// </summary>
        public static DevWarpCatalog Build(IEnumerable<DevZoneSource> zones, IEnumerable<DevEntranceSource> entrances,
            IEnumerable<DevInteriorSource> interiors, IEnumerable<DevNpcSource> npcs)
        {
            var places = new List<DevWarpTarget>();
            var interiorList = new List<DevWarpTarget>();
            var npcList = new List<DevWarpTarget>();
            var timeList = new List<DevWarpTarget>();

            var zoneSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DevZoneSource zone in Sorted(zones, z => z.Id))
            {
                if (string.IsNullOrEmpty(zone.Id) || !zoneSeen.Add(zone.Id))
                {
                    continue;
                }

                string name = L.Get("ui.place." + zone.Id, zone.Name);
                places.Add(new DevWarpTarget(DevWarpKind.Place, zone.Id,
                    string.IsNullOrEmpty(name) ? zone.Id : name, zone.Stand, 0f, true));
            }

            var fronts = new Dictionary<string, DevWarpTarget>(StringComparer.Ordinal);
            foreach (DevEntranceSource entrance in Sorted(entrances, e => e.BuildingId))
            {
                if (string.IsNullOrEmpty(entrance.BuildingId) || fronts.ContainsKey(entrance.BuildingId))
                {
                    continue;
                }

                Vector3 outward = entrance.Outward;
                outward.y = 0f;
                outward = outward.sqrMagnitude < 1e-4f ? Vector3.forward : outward.normalized;
                string building = L.Get("ui.building." + entrance.BuildingId, entrance.Name);
                if (string.IsNullOrEmpty(building))
                {
                    building = entrance.BuildingId;
                }

                var front = new DevWarpTarget(DevWarpKind.Place, "door_" + entrance.BuildingId,
                    L.Pick(building + " の入口前", building + " entrance"),
                    entrance.Door + outward * EntranceStandOff, FacingYaw(-outward), true);
                fronts.Add(entrance.BuildingId, front);
                places.Add(front);
            }

            var interiorSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DevInteriorSource interior in Sorted(interiors, i => i.Id))
            {
                if (string.IsNullOrEmpty(interior.Id) || !interiorSeen.Add(interior.Id))
                {
                    continue;
                }

                string name = L.Get("ui.building." + interior.Id, interior.Name);
                if (string.IsNullOrEmpty(name))
                {
                    name = interior.Id;
                }

                string label = L.Pick(name + " の中", "Inside " + name);
                interiorList.Add(fronts.TryGetValue(interior.Id, out DevWarpTarget front)
                    ? new DevWarpTarget(DevWarpKind.Interior, interior.Id, label, front.Position, front.Yaw, true)
                    : new DevWarpTarget(DevWarpKind.Interior, interior.Id, label, Vector3.zero, 0f, false));
            }

            var npcSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DevNpcSource npc in Sorted(npcs, n => n.Id))
            {
                if (string.IsNullOrEmpty(npc.Id) || !npcSeen.Add(npc.Id))
                {
                    continue;
                }

                Vector3 toPlayer = -npc.Forward;
                toPlayer.y = 0f;
                toPlayer = toPlayer.sqrMagnitude < 0.01f ? Vector3.back : toPlayer.normalized;
                npcList.Add(new DevWarpTarget(DevWarpKind.Npc, npc.Id,
                    string.IsNullOrEmpty(npc.Name) ? npc.Id : npc.Name,
                    npc.Position + toPlayer * NpcStandOff, FacingYaw(-toPlayer), true));
            }

            foreach ((string key, string fallback, float hours) in Times)
            {
                timeList.Add(new DevWarpTarget(DevWarpKind.Time, key, L.Get("ui.dev.time." + key, fallback),
                    Vector3.zero, 0f, false, hours));
            }

            return new DevWarpCatalog(new[] { places, interiorList, npcList, timeList });
        }

        /// <summary>ワールドの向き direction（水平）を向くときのヨー（度）。0 が +z、90 が +x。</summary>
        public static float FacingYaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>いまのシーンから一覧を作る。ゾーンの立ち位置は箱の下端の中心。</summary>
        public static DevWarpCatalog CollectFromScene()
        {
            var zones = new List<DevZoneSource>();
            foreach (VisitZone zone in UnityEngine.Object.FindObjectsByType<VisitZone>(FindObjectsInactive.Exclude))
            {
                // 屋内の見どころ（poi_*）は屋内の空間にあるので、建物の中の項目から入る。
                if (zone.PlaceId != null && zone.PlaceId.StartsWith(InteriorPlacePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var box = zone.GetComponent<BoxCollider>();
                Vector3 center = box != null ? zone.transform.TransformPoint(box.center) : zone.transform.position;
                float half = box != null ? box.size.y * 0.5f * zone.transform.lossyScale.y : 0f;
                zones.Add(new DevZoneSource(zone.PlaceId, zone.DisplayName,
                    new Vector3(center.x, center.y - half + 0.1f, center.z)));
            }

            var entrances = new List<DevEntranceSource>();
            foreach (EntranceTrigger door in UnityEngine.Object.FindObjectsByType<EntranceTrigger>(FindObjectsInactive.Exclude))
            {
                // 入口前の項目は 1 棟に 1 件で、玄関の前に立つ。2 つ目からの扉（講義棟の南東の玄関）は入れない。
                // 入れると同じ建物の id の入口が 2 つになり、どちらが残るかが FindObjectsByType の順で変わる。
                if (door.DoorId != door.BuildingId)
                {
                    continue;
                }

                entrances.Add(new DevEntranceSource(door.BuildingId, door.DisplayName, door.transform.position, door.Outward));
            }

            var interiors = new List<DevInteriorSource>();
            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null)
            {
                foreach (InteriorLoader.Entry entry in loader.Entries)
                {
                    interiors.Add(new DevInteriorSource(entry.Id, entry.DisplayName));
                }
            }

            var npcs = new List<DevNpcSource>();
            foreach (NPCTalker talker in UnityEngine.Object.FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude))
            {
                // 扉や掲示板のような「調べる」物は人ではないので入れない。
                if (talker.PromptKey == NPCTalker.TalkPromptKey)
                {
                    npcs.Add(new DevNpcSource(talker.NpcId, talker.DisplayName, talker.transform.position,
                        talker.transform.forward));
                }
            }

            return Build(zones, entrances, interiors, npcs);
        }

        private static List<T> Sorted<T>(IEnumerable<T> source, Func<T, string> key)
        {
            var list = new List<T>(source ?? Array.Empty<T>());
            list.Sort((a, b) => string.CompareOrdinal(key(a) ?? string.Empty, key(b) ?? string.Empty));
            return list;
        }
    }
}
