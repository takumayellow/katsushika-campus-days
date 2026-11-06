using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// サブクエスト「出席は小テストで」（q_sq_pop_quiz）の、101 の隣の席の学生と、前の黒板の板書を置く (#424)。
    ///
    /// 学生（立石 みなと）は客席の NPC の empty（npc_lecture_2、客席の西のブロック）に立ち、通路の
    /// hall_seats を向く。101 の黒板には他の教室のような empty が無いので、教卓・客席・音響卓の 3 つの POI から
    /// 屋内モデルの平面の座標（blender/kcd_interior/plan_lecture_dims.py）を Unity の水平面へ写す変換を解き、
    /// 教卓の正面の黒板の面（Y 4.4）の位置と向きを出す。板書は見る人の右の板（中央から 3.1 m）に貼る。
    /// 屋内の当たり判定を使うので <see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class LectureSeatmateStage
    {
        public const string InteriorName = "Interior_lecture";

        /// <summary>学生の会話 id（Dialogue/tateishi.json）。見た目は男子学生を借りる。</summary>
        public const string SeatmateDialogueId = "tateishi";

        public const string SeatmateBodyId = "student_m";
        public const string SeatmateName = "立石 みなと";
        public const string SeatmateEmpty = "npc_lecture_2";

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

        /// <summary>黒板の面の中心（中央の板、教卓の正面）。板は壁の面 Y 4.3 から 0.04〜0.09 手前に出ている。</summary>
        private static readonly Vector2 BoardPlan = new Vector2(17.75f, 4.39f);

        /// <summary>黒板（平場の床から 0.9〜2.1 m）の中心の高さ。</summary>
        private const float BoardCenterHeight = 1.5f;

        /// <summary>中央の板から見る人の右の板の中心まで（板 13.5〜15.8 / 19.7〜22.0 の中心と 17.75 の差）。</summary>
        private const float ChalkSideOffset = 3.1f;

        private static readonly Vector2 ChalkFrame = new Vector2(2.1f, 0.9f);
        private static readonly Color32 Chalk = new Color32(236, 238, 228, 255);
        private const float FaceGap = 0.01f;

        /// <summary>板を調べる判定の箱（板に沿った幅・高さ・板から手前への奥行き、m）と、E が出る距離。</summary>
        private static readonly Vector3 TriggerSize = new Vector3(2.4f, 1.4f, 0.6f);

        private const float InspectRange = 2.5f;

        /// <summary>板書の 2 行。会話 board_lecture_101.json の板の行（written）と同じにしておく。</summary>
        public const string ChalkText = "次回　小テスト（出席を兼ねます）\n練習：4 席の空き(0)・埋まり(1)の並び方は何通り？";

        public static void Build(Transform root)
        {
            Transform interior = Find(root, InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(InteriorName + " が無いので、101 の隣の席の学生と板書は置きませんでした。");
                return;
            }

            Physics.SyncTransforms();
            PlaceSeatmate(interior);

            if (TryBoardFrame(interior, out Vector3 floor, out Vector3 facing))
            {
                PlaceBoard(interior, floor, facing);
            }
        }

        /// <summary>隣の席の学生。通路（hall_seats）を向いて立ち、動かない（屋内に NavMesh は無い）。</summary>
        private static void PlaceSeatmate(Transform interior)
        {
            Transform spot = Find(interior, SeatmateEmpty);
            if (spot == null)
            {
                EditorPaths.Report(SeatmateEmpty + " が無いので、101 の隣の席の学生は置きませんでした。");
                return;
            }

            Vector3 position = CollectibleStage.IndoorFloor(spot.position);
            float yaw = spot.eulerAngles.y;
            Transform seats = Find(interior, SeatsEmpty);
            if (seats != null)
            {
                Vector3 toSeats = seats.position - position;
                toSeats.y = 0f;
                if (toSeats.sqrMagnitude > 0.01f)
                {
                    yaw = Mathf.Atan2(toSeats.x, toSeats.z) * Mathf.Rad2Deg;
                }
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
            EditorPaths.Report("101 の客席に隣の席の学生を置きました: " + position.ToString("F1")
                + " 向き " + yaw.ToString("F0") + "°");
        }

        /// <summary>
        /// 中央の黒板の真下の床の点と、板が向く向き（部屋の側、水平）。3 つの POI から平面の座標を写す変換を解く。
        /// POI が足りないか、3 点が一直線に並んで解けなければ false。
        /// </summary>
        internal static bool TryBoardFrame(Transform interior, out Vector3 floor, out Vector3 facing)
        {
            floor = Vector3.zero;
            facing = Vector3.forward;
            Transform stage = Find(interior, StageEmpty);
            Transform seats = Find(interior, SeatsEmpty);
            Transform booth = Find(interior, BoothEmpty);
            if (stage == null || seats == null || booth == null)
            {
                EditorPaths.Report("101 の POI（教卓・客席・音響卓）が揃わないので、101 の板書は置きませんでした。");
                return false;
            }

            if (!TrySolvePlan(StagePlan, SeatsPlan, BoothPlan,
                    Flat(stage.position), Flat(seats.position), Flat(booth.position), out Matrix2 map))
            {
                EditorPaths.Report("101 の POI が一直線に並んでいるので、101 の板書は置きませんでした。");
                return false;
            }

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
