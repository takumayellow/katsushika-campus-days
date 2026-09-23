using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 裏エンド「寮でぐーたら」(#41) が使う、西の回廊と葛飾コミュニティハウスの数値。
    /// 既定値は data/osm/route.json（OpenStreetMap 由来、2026-09-23 取得）の実測値をそのまま写したもの。
    /// route.fbx と一緒に書き出される Assets/Models/Campus/route.json があればそちらで上書きする。
    ///
    /// 幾何の計算はすべて静的な純関数にしてある。RouteStage / DormStage（Editor 側）とテストの両方から
    /// 参照するので、置き場所は Runtime。EditMode テストの asmdef は KCD.Runtime しか参照しないため、
    /// Editor アセンブリに置くと Unity を起動せずに確かめられなくなる。
    /// </summary>
    public static class DormRoute
    {
        /// <summary>建物 id。InteriorLoader への登録と DormEntrance が使う。</summary>
        public const string Id = "dorm";

        /// <summary>表示名。ローカライズ鍵 ui.building.dorm が無いときの受け皿でもある。</summary>
        public const string DisplayName = "葛飾コミュニティハウス";

        /// <summary>寮長の会話 id。Assets/Data/Dialogue/dorm_head.json。</summary>
        public const string DialogueId = "dorm_head";

        /// <summary>寮長の表示名。</summary>
        public const string NpcName = "寮長";

        /// <summary>寮長の見た目に借りるリグ。教授のモデルをそのまま使う。</summary>
        public const string NpcBodyId = "prof";

        /// <summary>寮長との会話が立てるフラグ。DormEnding がこれを見て裏エンドを出す。</summary>
        public const string FlagId = "dorm_skip_day";

        // ---- 既定値（data/osm/route.json の実測。sidecar が無いときはこの値で組む）----

        /// <summary>建物の重心 x（m）。route.json stats.dorm_centroid。</summary>
        public const float DefaultCentroidX = -464.52f;

        /// <summary>建物の重心 z（m）。</summary>
        public const float DefaultCentroidZ = 202f;

        /// <summary>玄関の位置 x（m）。route.json dormitory.entrance.point。</summary>
        public const float DefaultDoorX = -455.83f;

        /// <summary>玄関の位置 z（m）。</summary>
        public const float DefaultDoorZ = 183.24f;

        /// <summary>玄関が向く方位（度）。0 が +z（北）で時計回り。route.json の facing_bearing 159.5（南南東）。</summary>
        public const float DefaultDoorBearing = 159.5f;

        /// <summary>西の壁（x = -340）を経路が横切る z（m）。route.json routes[].points から算出。</summary>
        public const float DefaultGateZ = 239.83f;

        /// <summary>門の幅の半分（m）。両開きの門ひとつぶん。</summary>
        public const float DefaultGateHalfWidth = 12f;

        /// <summary>西の増築区画の西端 x（m）。寮の footprint 西端 -474.92 より 15 m 外。</summary>
        public const float DefaultAnnexXMin = -490f;

        /// <summary>西の増築区画の南端 z（m）。経路の南端 175.57 より 20 m 南。</summary>
        public const float DefaultAnnexZMin = 156f;

        /// <summary>西の増築区画の東端 x（m）。キャンパスの西の壁とぴったり合わせる。</summary>
        public const float DefaultAnnexXMax = -340f;

        /// <summary>西の増築区画の北端 z（m）。門の北端 251.83 より 10 m 北。</summary>
        public const float DefaultAnnexZMax = 262f;

        // ---- 壁の寸法の既定値。WorldBoundsStage の同名の定数と同じ値 ----
        // Runtime から Editor アセンブリは見えないので写してある。RouteStage が組むときに突き合わせて、
        // ずれていたらレポートに出す。

        /// <summary>壁の厚さ（m）。WorldBoundsStage.WallThickness と同じ。</summary>
        public const float DefaultWallThickness = 2f;

        /// <summary>壁の下端（m）。WorldBoundsStage.WallBottom と同じ。</summary>
        public const float DefaultWallBottom = -2f;

        /// <summary>壁の上端（m）。WorldBoundsStage.WallTop と同じ。</summary>
        public const float DefaultWallTop = 6f;

        /// <summary>門を割った西の壁の南側の名前。</summary>
        public const string WestWallSouthName = "Wall_West_S";

        /// <summary>門を割った西の壁の北側の名前。</summary>
        public const string WestWallNorthName = "Wall_West_N";

        /// <summary>西の増築区画を囲う壁の名前（西・北・南）。東はキャンパスの西の壁が兼ねる。</summary>
        public static readonly string[] AnnexWallNames = { "Wall_Annex_West", "Wall_Annex_North", "Wall_Annex_South" };

        /// <summary>見えない壁 1 枚ぶんの BoxCollider の置き方。</summary>
        public struct WallSlab
        {
            /// <summary>GameObject の名前。</summary>
            public string Name;

            /// <summary>中心（ワールド座標）。</summary>
            public Vector3 Center;

            /// <summary>大きさ。</summary>
            public Vector3 Size;
        }

        /// <summary>route.json（sidecar）から読み取る値。欠けていれば既定値に落ちる。</summary>
        public struct Info
        {
            /// <summary>玄関の位置（xz）。</summary>
            public Vector2 Door;

            /// <summary>玄関が向く方位（度、0 = +z、時計回り）。</summary>
            public float DoorBearing;

            /// <summary>建物の重心（xz）。</summary>
            public Vector2 Centroid;

            /// <summary>西の壁を経路が横切る z。</summary>
            public float GateZ;

            /// <summary>門の幅の半分（m）。</summary>
            public float GateHalfWidth;

            /// <summary>西の増築区画（xz の矩形）。</summary>
            public Rect Annex;
        }

        /// <summary>sidecar が無いときに使う値。</summary>
        public static Info Default => new Info
        {
            Door = new Vector2(DefaultDoorX, DefaultDoorZ),
            DoorBearing = DefaultDoorBearing,
            Centroid = new Vector2(DefaultCentroidX, DefaultCentroidZ),
            GateZ = DefaultGateZ,
            GateHalfWidth = DefaultGateHalfWidth,
            Annex = DefaultAnnex,
        };

        /// <summary>西の増築区画の既定の矩形。</summary>
        public static Rect DefaultAnnex =>
            Rect.MinMaxRect(DefaultAnnexXMin, DefaultAnnexZMin, DefaultAnnexXMax, DefaultAnnexZMax);

        /// <summary>
        /// sidecar JSON（Assets/Models/Campus/route.json）の形。blender 側が書き出す想定の最小限の鍵だけを見る。
        /// 増えた鍵は JsonUtility が黙って捨てるので、blender 側が他の情報を足しても壊れない。
        /// </summary>
        [System.Serializable]
        private sealed class JsonShape
        {
            public float[] door;
            public float door_bearing;
            public float[] centroid;
            public float gate_z;
            public float gate_half_width;
            public float[] annex;
        }

        /// <summary>
        /// sidecar の中身を読む。null・空・壊れている・値がありえない、のどれでも既定値に落ちる。
        /// 鍵ごとに見るので、door だけ書いてある sidecar でも残りは既定値で埋まる。
        /// </summary>
        public static Info FromJson(string text)
        {
            Info info = Default;
            if (string.IsNullOrEmpty(text))
            {
                return info;
            }

            JsonShape shape;
            try
            {
                shape = JsonUtility.FromJson<JsonShape>(text);
            }
            catch (System.Exception)
            {
                return info;
            }

            if (shape == null)
            {
                return info;
            }

            if (TryPoint(shape.door, out Vector2 door))
            {
                info.Door = door;
            }

            if (TryPoint(shape.centroid, out Vector2 centroid))
            {
                info.Centroid = centroid;
            }

            // 方位 0 は「北向き」ではなく「書いてない」とみなす。玄関が真北を向くことは無い。
            if (shape.door_bearing > 0f && shape.door_bearing < 360f)
            {
                info.DoorBearing = shape.door_bearing;
            }

            if (shape.gate_z != 0f)
            {
                info.GateZ = shape.gate_z;
            }

            if (shape.gate_half_width > 0f)
            {
                info.GateHalfWidth = shape.gate_half_width;
            }

            if (shape.annex != null && shape.annex.Length >= 4)
            {
                info.Annex = Rect.MinMaxRect(
                    Mathf.Min(shape.annex[0], shape.annex[2]),
                    Mathf.Min(shape.annex[1], shape.annex[3]),
                    Mathf.Max(shape.annex[0], shape.annex[2]),
                    Mathf.Max(shape.annex[1], shape.annex[3]));
            }

            return IsPlausible(info) ? info : Default;
        }

        private static bool TryPoint(float[] values, out Vector2 point)
        {
            if (values == null || values.Length < 2)
            {
                point = Vector2.zero;
                return false;
            }

            point = new Vector2(values[0], values[1]);
            return true;
        }

        /// <summary>
        /// sidecar の値として筋が通っているか。矩形がつぶれていない、玄関と重心が区画の中、
        /// 門が区画の南北の中に収まっている、を見る。1 つでも外れたら sidecar ごと捨てて既定値に戻す。
        /// </summary>
        public static bool IsPlausible(Info info)
        {
            if (info.Annex.width <= 1f || info.Annex.height <= 1f || info.GateHalfWidth <= 0f)
            {
                return false;
            }

            if (!Contains(info.Annex, info.Door, 0f) || !Contains(info.Annex, info.Centroid, 0f))
            {
                return false;
            }

            return GateOpensIntoAnnex(info.Annex, info.GateZ, info.GateHalfWidth);
        }

        /// <summary>点が矩形の中にあるか。margin だけ内側に絞って見る。</summary>
        public static bool Contains(Rect area, Vector2 point, float margin)
        {
            return point.x >= area.xMin + margin && point.x <= area.xMax - margin
                && point.y >= area.yMin + margin && point.y <= area.yMax - margin;
        }

        /// <summary>2 つの矩形を包む最小の矩形。</summary>
        public static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin),
                Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax));
        }

        /// <summary>
        /// WorldBounds に持たせる見張りの矩形。キャンパスの矩形と西の増築区画を両方含む。
        /// これを広げておかないと、門をくぐった瞬間に「範囲外」とみなされて引き戻される。
        /// </summary>
        public static Rect WatchArea(Rect campus, Rect annex)
        {
            return Union(campus, annex);
        }

        /// <summary>門がキャンパスの矩形の南北の中に収まっているか（壁を割れるか）。</summary>
        public static bool GateFitsWall(Rect campus, float gateZ, float gateHalfWidth)
        {
            return gateHalfWidth > 0f
                && gateZ - gateHalfWidth > campus.yMin
                && gateZ + gateHalfWidth < campus.yMax;
        }

        /// <summary>門の外側が増築区画の中に開いているか。外れていると門の先が虚空になる。</summary>
        public static bool GateOpensIntoAnnex(Rect annex, float gateZ, float gateHalfWidth)
        {
            return gateHalfWidth > 0f
                && gateZ - gateHalfWidth >= annex.yMin
                && gateZ + gateHalfWidth <= annex.yMax;
        }

        /// <summary>増築区画の東端がキャンパスの西端とぴったり合っているか。ずれると角に隙間ができる。</summary>
        public static bool AnnexTouchesCampus(Rect campus, Rect annex)
        {
            return Mathf.Abs(annex.xMax - campus.xMin) < 0.01f;
        }

        /// <summary>
        /// 西の壁に門を開ける。もとの Wall_West を南北 2 枚に割った形を返す。
        /// 門が壁の外へはみ出すなら false（呼び側は壁に手を触れない）。
        /// 壁の寸法は WorldBoundsStage.Build と同じ作り方にしてある（内側の面が矩形の縁に来る）。
        /// </summary>
        public static bool SplitWestWall(Rect campus, float gateZ, float gateHalfWidth,
            float thickness, float bottom, float top, out WallSlab south, out WallSlab north)
        {
            south = default;
            north = default;

            if (!GateFitsWall(campus, gateZ, gateHalfWidth))
            {
                return false;
            }

            float height = top - bottom;
            float centerY = (top + bottom) * 0.5f;
            float x = campus.xMin - thickness * 0.5f;
            float gateMin = gateZ - gateHalfWidth;
            float gateMax = gateZ + gateHalfWidth;

            south = new WallSlab
            {
                Name = WestWallSouthName,
                Center = new Vector3(x, centerY, (campus.yMin + gateMin) * 0.5f),
                Size = new Vector3(thickness, height, gateMin - campus.yMin),
            };

            north = new WallSlab
            {
                Name = WestWallNorthName,
                Center = new Vector3(x, centerY, (gateMax + campus.yMax) * 0.5f),
                Size = new Vector3(thickness, height, campus.yMax - gateMax),
            };

            return true;
        }

        /// <summary>
        /// 西の増築区画を囲う壁（西・北・南）。東はキャンパスの Wall_West が兼ねるので立てない。
        ///
        /// 南北の壁は西の角を覆うために西へ厚さぶん伸ばすが、東は区画の東端
        /// （= キャンパスの西端 <c>campus.xMin</c>）でぴったり止める。東へも厚さぶん伸ばすと、
        /// キャンパスの内側へ壁が厚さぶん（既定 2 m）突き出し、キャンパスの中に見えない角柱が
        /// 2 本立つ（増築区画からは見えず、ぶつかる理由も分からない当たり判定になる）。
        /// 東の角に隙間はできない。Wall_West は内側の面が campus.xMin に来る立て方なので
        /// x = campus.xMin - thickness .. campus.xMin を占め、南北の壁の東端と必ず重なる。
        /// </summary>
        public static WallSlab[] AnnexWalls(Rect annex, float thickness, float bottom, float top)
        {
            float height = top - bottom;
            float centerY = (top + bottom) * 0.5f;
            float half = thickness * 0.5f;
            // 西へだけ厚さぶん伸ばす。東端は annex.xMax のまま。
            float spanX = annex.width + thickness;
            float centerX = annex.xMax - spanX * 0.5f;

            return new[]
            {
                new WallSlab
                {
                    Name = AnnexWallNames[0],
                    Center = new Vector3(annex.xMin - half, centerY, annex.center.y),
                    Size = new Vector3(thickness, height, annex.height),
                },
                new WallSlab
                {
                    Name = AnnexWallNames[1],
                    Center = new Vector3(centerX, centerY, annex.yMax + half),
                    Size = new Vector3(spanX, height, thickness),
                },
                new WallSlab
                {
                    Name = AnnexWallNames[2],
                    Center = new Vector3(centerX, centerY, annex.yMin - half),
                    Size = new Vector3(spanX, height, thickness),
                },
            };
        }

        /// <summary>方位（度、0 = +z、時計回り）から外向きの水平な単位ベクトル。</summary>
        public static Vector3 OutwardFromBearing(float bearingDegrees)
        {
            float rad = bearingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>玄関の立ち位置（xz を y に載せたワールド座標）。</summary>
        public static Vector3 DoorWorld(Info info, float groundY)
        {
            return new Vector3(info.Door.x, groundY, info.Door.y);
        }
    }
}
