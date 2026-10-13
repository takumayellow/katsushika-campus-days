using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// モブの学生の時間割（Assets/Data/Mobs/schedule.json、DataBundler が Resources/KCD/Mobs へ複製）。
    /// 時間帯ごとの人数・歩く速さ・通り道と、見た目の組み合わせ（どの体を何色で塗るか）を持つ。
    /// 読むだけで Unity のシーンに触らないので、EditMode のテストからそのまま確かめられる。
    /// </summary>
    public sealed class MobSchedule
    {
        public const string ResourcePath = "KCD/Mobs/schedule";

        /// <summary>通り道の 1 点。建物の入口・VisitZone・座標のどれか 1 つ。</summary>
        public sealed class Waypoint
        {
            public Waypoint(string kind, string id, float x, float z, float wait)
            {
                Kind = kind;
                Id = id ?? string.Empty;
                X = x;
                Z = z;
                Wait = Mathf.Max(0f, wait);
                Key = KeyOf(kind, Id, x, z);
            }

            /// <summary>"entrance" / "place" / "xz"。</summary>
            public string Kind { get; }

            /// <summary>建物 id か VisitZone の id。座標の点では空。</summary>
            public string Id { get; }

            public float X { get; }
            public float Z { get; }

            /// <summary>着いてから立ち止まる秒数（ベンチで一休み など）。</summary>
            public float Wait { get; }

            /// <summary>SceneBuilder が解決した位置を引くためのキー（entrance:lecture / place:gate_main / xz:81:-63.4）。</summary>
            public string Key { get; }

            public bool IsXz => Kind == KindXz;
        }

        /// <summary>1 人ぶんの道のり。最後の点に着いたら消える（建物に入った / 門から出た）。</summary>
        public sealed class Route
        {
            public Route(float weight, IReadOnlyList<Waypoint> waypoints, string end)
            {
                Weight = Mathf.Max(0f, weight);
                Waypoints = waypoints;
                End = string.IsNullOrEmpty(end) ? "despawn" : end;
            }

            public float Weight { get; }
            public IReadOnlyList<Waypoint> Waypoints { get; }
            public string End { get; }
        }

        /// <summary>時間帯。from_hour 以上 to_hour 未満。</summary>
        public sealed class Band
        {
            public Band(
                string id, string labelJa, string labelEn, float fromHour, float toHour,
                int count, float walkSpeed, bool loop, IReadOnlyList<Route> routes)
            {
                Id = id ?? string.Empty;
                LabelJa = labelJa ?? string.Empty;
                LabelEn = labelEn ?? string.Empty;
                FromHour = fromHour;
                ToHour = toHour;
                Count = Mathf.Max(0, count);
                WalkSpeed = walkSpeed > 0.1f ? walkSpeed : 1.3f;
                Loop = loop;
                Routes = routes;

                float total = 0f;
                foreach (Route route in routes)
                {
                    total += route.Weight;
                }

                TotalWeight = total;
            }

            public string Id { get; }
            public string LabelJa { get; }
            public string LabelEn { get; }
            public float FromHour { get; }
            public float ToHour { get; }

            /// <summary>この時間帯に同時に歩いている人数の目安。</summary>
            public int Count { get; }

            public float WalkSpeed { get; }

            /// <summary>true なら、誰かが着いて消えるたびに補充する。false なら Count 人を出したら終わり。</summary>
            public bool Loop { get; }

            public IReadOnlyList<Route> Routes { get; }
            public float TotalWeight { get; }

            public bool Contains(float hour)
            {
                return hour >= FromHour && hour < ToHour;
            }
        }

        /// <summary>見た目の組み合わせ。既存のキャラクターの体（Body）を、髪と服の色だけ変えて使う。</summary>
        public sealed class Variant
        {
            public Variant(string id, string bodyId, Color hair, Color top, Color bottom, Color shoes, float height)
            {
                Id = id ?? string.Empty;
                BodyId = string.IsNullOrEmpty(bodyId) ? DefaultBody : bodyId;
                Hair = hair;
                Top = top;
                Bottom = bottom;
                Shoes = shoes;
                Height = height > 0.5f ? height : 1.6f;
            }

            public string Id { get; }

            /// <summary>元にするキャラクター FBX の id（sora / inari / kaname / prof / mirai）。</summary>
            public string BodyId { get; }

            public Color Hair { get; }
            public Color Top { get; }
            public Color Bottom { get; }
            public Color Shoes { get; }

            /// <summary>身長（m）。元の体をこの高さに合わせて等倍で縮める / 伸ばす。</summary>
            public float Height { get; }
        }

        public const string KindEntrance = "entrance";
        public const string KindPlace = "place";
        public const string KindXz = "xz";
        public const string DefaultBody = "sora";

        private static readonly Color FallbackColor = new Color(0.5f, 0.5f, 0.5f);

        private MobSchedule(
            IReadOnlyList<Band> bands, IReadOnlyList<Variant> variants,
            float spawnStagger, float respawnDelay, float avoidPlayerRadius)
        {
            Bands = bands;
            Variants = variants;
            SpawnStaggerSeconds = spawnStagger;
            RespawnDelaySeconds = respawnDelay;
            AvoidPlayerRadius = avoidPlayerRadius;
        }

        public IReadOnlyList<Band> Bands { get; }
        public IReadOnlyList<Variant> Variants { get; }

        /// <summary>ふだん 1 人出してから次を出すまでの間隔（秒）。</summary>
        public float SpawnStaggerSeconds { get; }

        /// <summary>消えた枠をもう一度使えるようになるまでの秒数。</summary>
        public float RespawnDelaySeconds { get; }

        /// <summary>プレイヤーからこれより近づかない距離（m）。</summary>
        public float AvoidPlayerRadius { get; }

        /// <summary>JSON から作る。読めなければ null。</summary>
        public static MobSchedule Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || !(MiniJson.Deserialize(json) is Dictionary<string, object> root))
            {
                return null;
            }

            var variants = new List<Variant>();
            foreach (object node in MiniJson.GetArray(root, "variants"))
            {
                if (node is Dictionary<string, object> v)
                {
                    variants.Add(new Variant(
                        MiniJson.GetString(v, "id"),
                        MiniJson.GetString(v, "body", DefaultBody),
                        ParseColor(MiniJson.GetString(v, "hair")),
                        ParseColor(MiniJson.GetString(v, "top")),
                        ParseColor(MiniJson.GetString(v, "bottom")),
                        ParseColor(MiniJson.GetString(v, "shoes")),
                        MiniJson.GetFloat(v, "height", 1.6f)));
                }
            }

            var bands = new List<Band>();
            foreach (object node in MiniJson.GetArray(root, "bands"))
            {
                if (node is Dictionary<string, object> b)
                {
                    bands.Add(ParseBand(b));
                }
            }

            return new MobSchedule(
                bands,
                variants,
                Mathf.Max(0.1f, MiniJson.GetFloat(root, "spawn_stagger_seconds", 2.5f)),
                Mathf.Max(0f, MiniJson.GetFloat(root, "respawn_delay_seconds", 6f)),
                Mathf.Max(0.3f, MiniJson.GetFloat(root, "avoid_player_radius", 1.2f)));
        }

        /// <summary>Resources から読む。無ければ null。</summary>
        public static MobSchedule LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            return asset != null ? Parse(asset.text) : null;
        }

        /// <summary>その時刻の時間帯。どれにも入らなければ null（その時間は誰も出さない）。</summary>
        public Band BandAt(float hour)
        {
            float h = Mathf.Repeat(hour, 24f);
            foreach (Band band in Bands)
            {
                if (band.Contains(h))
                {
                    return band;
                }
            }

            return null;
        }

        /// <summary>その時刻に歩いていてほしい人数。</summary>
        public int CountAt(float hour)
        {
            Band band = BandAt(hour);
            return band != null ? band.Count : 0;
        }

        /// <summary>時間帯の人数を、プールの大きさと同時に出せる上限で切る。</summary>
        public static int ClampTarget(int count, int poolSize, int maxActive)
        {
            int cap = Mathf.Max(0, Mathf.Min(poolSize, maxActive));
            return Mathf.Clamp(count, 0, cap);
        }

        /// <summary>重みに比例して道のりを 1 つ選ぶ。r01 は 0 以上 1 以下の乱数。選べなければ null。</summary>
        public static Route PickRoute(Band band, float r01)
        {
            if (band == null || band.Routes.Count == 0 || band.TotalWeight <= 0f)
            {
                return null;
            }

            float r = Mathf.Clamp01(r01) * band.TotalWeight;
            float acc = 0f;
            Route last = null;
            foreach (Route route in band.Routes)
            {
                if (route.Weight <= 0f)
                {
                    continue;
                }

                acc += route.Weight;
                last = route;
                if (r < acc)
                {
                    return route;
                }
            }

            return last;
        }

        /// <summary>どこかの道のりに出てくる点をすべて、キーの重複なしで返す。</summary>
        public List<Waypoint> AllWaypoints()
        {
            var seen = new HashSet<string>();
            var result = new List<Waypoint>();
            foreach (Band band in Bands)
            {
                foreach (Route route in band.Routes)
                {
                    foreach (Waypoint waypoint in route.Waypoints)
                    {
                        if (seen.Add(waypoint.Key))
                        {
                            result.Add(waypoint);
                        }
                    }
                }
            }

            return result;
        }

        public static string KeyOf(string kind, string id, float x, float z)
        {
            if (kind == KindXz)
            {
                return KindXz + ":" + x.ToString("0.###", CultureInfo.InvariantCulture)
                    + ":" + z.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return kind + ":" + id;
        }

        private static Band ParseBand(Dictionary<string, object> b)
        {
            var routes = new List<Route>();
            foreach (object node in MiniJson.GetArray(b, "routes"))
            {
                if (!(node is Dictionary<string, object> r))
                {
                    continue;
                }

                var waypoints = new List<Waypoint>();
                foreach (object wp in MiniJson.GetArray(r, "waypoints"))
                {
                    Waypoint waypoint = ParseWaypoint(wp as Dictionary<string, object>);
                    if (waypoint != null)
                    {
                        waypoints.Add(waypoint);
                    }
                }

                // 出発点と行き先の 2 点が無い道は歩けない。
                if (waypoints.Count >= 2)
                {
                    routes.Add(new Route(MiniJson.GetFloat(r, "weight", 1f), waypoints, MiniJson.GetString(r, "end", "despawn")));
                }
            }

            return new Band(
                MiniJson.GetString(b, "id"),
                MiniJson.GetString(b, "label_ja"),
                MiniJson.GetString(b, "label_en"),
                MiniJson.GetFloat(b, "from_hour"),
                MiniJson.GetFloat(b, "to_hour"),
                MiniJson.GetInt(b, "count"),
                MiniJson.GetFloat(b, "walk_speed", 1.3f),
                MiniJson.GetBool(b, "loop", true),
                routes);
        }

        private static Waypoint ParseWaypoint(Dictionary<string, object> w)
        {
            if (w == null)
            {
                return null;
            }

            float wait = MiniJson.GetFloat(w, "wait");
            string entrance = MiniJson.GetString(w, "entrance");
            if (!string.IsNullOrEmpty(entrance))
            {
                return new Waypoint(KindEntrance, entrance, 0f, 0f, wait);
            }

            string place = MiniJson.GetString(w, "place");
            if (!string.IsNullOrEmpty(place))
            {
                return new Waypoint(KindPlace, place, 0f, 0f, wait);
            }

            if (w.ContainsKey("x") && w.ContainsKey("z"))
            {
                return new Waypoint(KindXz, string.Empty, MiniJson.GetFloat(w, "x"), MiniJson.GetFloat(w, "z"), wait);
            }

            return null;
        }

        private static Color ParseColor(string hex)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color)
                ? color
                : FallbackColor;
        }
    }
}
