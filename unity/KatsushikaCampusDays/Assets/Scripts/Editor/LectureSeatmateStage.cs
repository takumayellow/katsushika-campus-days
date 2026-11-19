using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// サブクエスト「出席は小テストで」（q_sq_pop_quiz）の、101 の隣の席の学生と、前の黒板の板書を置く (#424)。
    ///
    /// 101 の席と黒板には他の教室のような empty が無いので、教卓・客席・音響卓の 3 つの POI から
    /// 屋内モデルの平面の座標（blender/kcd_interior/plan_lecture_dims.py）を Unity の水平面へ写す変換を解き、
    /// 平面の座標で決めた位置を Unity へ写す。
    /// 学生（立石 みなと）は、客席の POI（hall_seats、西と中央のブロックの間の通路の 5 列目）の隣の席、
    /// 西のブロックの通路側の端の席に立ち、列に沿って通路の側（平面の +X）を向く。
    /// 板書は教卓の正面の黒板の面（Y 4.4）の、見る人の右の板（中央から 3.1 m）に貼る。
    /// 屋内の当たり判定を使うので <see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class LectureSeatmateStage
    {
        public const string InteriorName = "Interior_lecture";

        /// <summary>学生の会話 id（Dialogue/tateishi.json）。見た目は男子学生を借りる。</summary>
        public const string SeatmateDialogueId = "tateishi";

        public const string SeatmateBodyId = "student_m";
        public const string SeatmateName = "立石 みなと";

        /// <summary>板書の会話 id（Dialogue/board_lecture_101.json）と判定の名前。</summary>
        public const string BoardDialogueId = "board_lecture_101";

        public const string BoardTalkerName = "Talker_board_101";

        public const string StageEmpty = "poi_lecture_hall_stage";
        public const string SeatsEmpty = "poi_lecture_hall_seats";
        public const string BoothEmpty = "poi_lecture_hall_booth";

        /// <summary>3 つの POI の、屋内モデルでの平面の座標（plan_lecture_dims.py の POIS）。</summary>
        private static readonly Vector2 StagePlan = new Vector2(17.75f, 6.65f);

        private static readonly Vector2 SeatsPlan = new Vector2(14.0f, 11.55f);
        private static readonly Vector2 BoothPlan = new Vector2(20.3f, 6.05f);

        /// <summary>
        /// 学生が立つ平面の座標。客席の POI（14.0, 11.55）と同じ 5 列目（踏面 Y 11.05〜12.05）の、
        /// 西のブロック（X 8.7〜13.5）の通路側の端の席。席の中心は X 13.2・Y 11.77（plan_lecture_hall.seat_points）。
        /// 体の当たり判定（半径 0.34）が通路（X 13.5〜14.5）へはみ出さないよう X を 13.15 にし、
        /// Y は席の中心。前の机（Y 11.07〜11.47）と後ろの段（Y 12.05）の間は 0.58 m で、当たり判定の直径（0.68 m）は
        /// 入らないが、体の幅（半径 0.28）は収まる。学生は動かないので、当たり判定が机に少し掛かっても困らない。
        /// </summary>
        public static readonly Vector2 SeatmatePlan = new Vector2(13.15f, 11.77f);

        /// <summary>黒板の面の中心（中央の板、教卓の正面）。板は壁の面 Y 4.3 から 0.04〜0.09 手前に出ている。</summary>
        private static readonly Vector2 BoardPlan = new Vector2(17.75f, 4.39f);

        /// <summary>黒板（平場の床から 0.9〜2.1 m）の中心の高さ。</summary>
        private const float BoardCenterHeight = 1.5f;

        /// <summary>中央の板から見る人の右の板の中心まで（板 13.5〜15.8 / 19.7〜22.0 の中心と 17.75 の差）。</summary>
        private const float ChalkSideOffset = 3.1f;

        /// <summary>
        /// 板書の枠（板 2.3 x 1.2 m の内側）。文字の大きさは自動で、いちばん長い行が枠の幅に収まる大きさになるので、
        /// 2 行をそれぞれ途中で折って 4 行にし、客席の POI（約 7 m）から読める大きさにする。
        /// </summary>
        private static readonly Vector2 ChalkFrame = new Vector2(2.2f, 1.0f);
        private static readonly Color32 Chalk = new Color32(236, 238, 228, 255);
        private const float FaceGap = 0.01f;

        /// <summary>板を調べる判定の箱（板に沿った幅・高さ・板から手前への奥行き、m）と、E が出る距離。</summary>
        private static readonly Vector3 TriggerSize = new Vector3(2.4f, 1.4f, 0.6f);

        private const float InspectRange = 2.5f;

        /// <summary>板書。会話 board_lecture_101.json の板の 2 行（written）と同じ文で、どちらの行も途中で折る。</summary>
        public const string ChalkText = "次回　小テスト\n（出席を兼ねます）\n練習：4 席の空き(0)・\n埋まり(1)の並び方は何通り？";

        public static void Build(Transform root)
        {
            Transform interior = Find(root, InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(InteriorName + " が無いので、101 の隣の席の学生と板書は置きませんでした。");
                return;
            }

            Physics.SyncTransforms();
            if (!TryPlanMap(interior, out Matrix2 map, out Transform stage, out Transform seats))
            {
                return;
            }

            PlaceSeatmate(interior, map, seats);
            if (TryBoardFrame(map, stage, out Vector3 floor, out Vector3 facing))
            {
                PlaceBoard(interior, floor, facing);
            }
        }

        /// <summary>隣の席の学生。席に立って列に沿って通路の側を向き、動かない（屋内に NavMesh は無い）。</summary>
        private static void PlaceSeatmate(Transform interior, Matrix2 map, Transform seats)
        {
            if (!TrySeatmatePose(map, seats.position, CollectibleStage.IndoorFloor(seats.position).y,
                    out Vector3 position, out float yaw))
            {
                EditorPaths.Report("101 の席の並びの向きが解けないので、101 の隣の席の学生は置きませんでした。");
                return;
            }

            GameObject npc = ActorFactory.CreateNpc(interior, SeatmateBodyId, SeatmateName, position, yaw, 0f);
            npc.name = "NPC_" + SeatmateDialogueId;

            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = SeatmateDialogueId;
                talker.DisplayName = SeatmateName;
            }

            NPCWander wander = npc.GetComponent<NPCWander>();
            if (wander != null)
            {
                Object.DestroyImmediate(wander);
            }

            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                Object.DestroyImmediate(agent);
            }

            CampusStage.Ignore(npc);
            EditorPaths.Report("101 の客席の POI の隣の席に学生を置きました: " + position.ToString("F1")
                + " 向き " + yaw.ToString("F0") + "°");
        }

        /// <summary>
        /// 教卓・客席・音響卓の 3 つの POI から、平面の座標の差分を Unity の水平面の差分へ写す変換を解く。
        /// POI が足りないか、3 点が一直線に並んで解けなければ false。
        /// </summary>
        internal static bool TryPlanMap(Transform interior, out Matrix2 map, out Transform stage, out Transform seats)
        {
            map = default;
            stage = Find(interior, StageEmpty);
            seats = Find(interior, SeatsEmpty);
            Transform booth = Find(interior, BoothEmpty);
            if (stage == null || seats == null || booth == null)
            {
                EditorPaths.Report("101 の POI（教卓・客席・音響卓）が揃わないので、101 の隣の席の学生と板書は置きませんでした。");
                return false;
            }

            if (!TrySolvePlan(StagePlan, SeatsPlan, BoothPlan,
                    Flat(stage.position), Flat(seats.position), Flat(booth.position), out map))
            {
                EditorPaths.Report("101 の POI が一直線に並んでいるので、101 の隣の席の学生と板書は置きませんでした。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 学生の立つ点（客席の POI の床の高さ）と向き（列に沿って通路の側 = 平面の +X）。
        /// 客席の POI の位置から <see cref="SeatmatePlan"/> までの平面の差分を写す。向きが解けなければ false。テストから呼ぶ。
        /// </summary>
        public static bool TrySeatmatePose(Matrix2 map, Vector3 seats, float floorY, out Vector3 position, out float yaw)
        {
            Vector2 offset = map.Apply(SeatmatePlan - SeatsPlan);
            position = new Vector3(seats.x + offset.x, floorY, seats.z + offset.y);
            yaw = 0f;
            Vector2 alongRow = map.Apply(Vector2.right);
            if (alongRow.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            yaw = Mathf.Atan2(alongRow.x, alongRow.y) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>中央の黒板の真下の床の点と、板が向く向き（部屋の側、水平）。向きが解けなければ false。</summary>
        internal static bool TryBoardFrame(Matrix2 map, Transform stage, out Vector3 floor, out Vector3 facing)
        {
            floor = Vector3.zero;
            facing = Vector3.forward;
            Vector3 stageFloor = CollectibleStage.IndoorFloor(stage.position);
            Vector2 offset = map.Apply(BoardPlan - StagePlan);
            floor = new Vector3(stage.position.x + offset.x, stageFloor.y, stage.position.z + offset.y);

            // 板は平面の +Y（客席の側）を向く。
            Vector2 toRoom = map.Apply(Vector2.up);
            if (toRoom.sqrMagnitude < 1e-6f)
            {
                EditorPaths.Report("101 の黒板の向きが解けないので、101 の板書は置きませんでした。");
                return false;
            }

            facing = new Vector3(toRoom.x, 0f, toRoom.y).normalized;
            return true;
        }

        /// <summary>見る人の右の板に板書を貼り、板の手前に調べる判定を置く。原点は板の面の上、+Z は部屋の側。</summary>
        private static void PlaceBoard(Transform interior, Vector3 floor, Vector3 facing)
        {
            Vector3 center = floor + Vector3.up * BoardCenterHeight
                + LectureBoardStage.ViewerRight(facing) * ChalkSideOffset;
            var go = new GameObject(BoardTalkerName);
            go.transform.SetParent(interior, true);
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0)
            {
                go.layer = layer;
            }

            TMP_FontAsset font = FontLibrary.Ensure();
            if (font != null)
            {
                LectureBoardStage.AddText(go.transform, "BoardText", font, ChalkText, Vector3.zero, ChalkFrame, Chalk);
            }
            else
            {
                EditorPaths.Report("日本語フォントが無いので、101 の板書の文字は貼りませんでした（調べる判定だけ置きます）。");
            }

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0f, TriggerSize.z * 0.5f + FaceGap);
            box.size = TriggerSize;

            NPCTalker talker = go.AddComponent<NPCTalker>();
            talker.NpcId = BoardDialogueId;
            talker.DisplayName = "黒板";
            talker.PromptKey = "ui.interact.inspect";
            talker.PromptLabel = "調べる";
            talker.TurnSpeed = 0f;
            talker.InteractionRange = InspectRange;
            EditorPaths.Report("101 の黒板に板書を置きました: " + center.ToString("F1"));
        }

        /// <summary>平面の 2x2 の線形写像（列ベクトルに左から掛ける）。</summary>
        public readonly struct Matrix2
        {
            public readonly float M00;
            public readonly float M01;
            public readonly float M10;
            public readonly float M11;

            public Matrix2(float m00, float m01, float m10, float m11)
            {
                M00 = m00;
                M01 = m01;
                M10 = m10;
                M11 = m11;
            }

            public Vector2 Apply(Vector2 v)
            {
                return new Vector2(M00 * v.x + M01 * v.y, M10 * v.x + M11 * v.y);
            }
        }

        /// <summary>
        /// 平面の 3 点 p0..p2 を Unity の水平面の 3 点 u0..u2（x, z）へ写す線形写像を解く（p0→u0 を原点にした差分どうし）。
        /// 平面の 2 本の差分が平行なら false。テストから呼ぶ。
        /// </summary>
        public static bool TrySolvePlan(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 u0, Vector2 u1, Vector2 u2,
            out Matrix2 map)
        {
            Vector2 a = p1 - p0;
            Vector2 b = p2 - p0;
            Vector2 ua = u1 - u0;
            Vector2 ub = u2 - u0;
            float det = a.x * b.y - b.x * a.y;
            if (Mathf.Abs(det) < 1e-4f)
            {
                map = default;
                return false;
            }

            // M * [a b] = [ua ub] → M = [ua ub] * inv([a b])
            float i00 = b.y / det;
            float i01 = -b.x / det;
            float i10 = -a.y / det;
            float i11 = a.x / det;
            map = new Matrix2(
                ua.x * i00 + ub.x * i10, ua.x * i01 + ub.x * i11,
                ua.y * i00 + ub.y * i10, ua.y * i01 + ub.y * i11);
            return true;
        }

        private static Vector2 Flat(Vector3 v)
        {
            return new Vector2(v.x, v.z);
        }

        private static Transform Find(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
