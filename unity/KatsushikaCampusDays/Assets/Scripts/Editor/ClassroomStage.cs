using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KCD.Editor
{
    /// <summary>
    /// 講義棟の、時間割どおりに授業をしている教室（#439）。104（1F）・202（2F）・304（3F）に、
    /// 講師 1 人・着席した学生・板書の枠を置き、<see cref="Classroom"/> に渡す。授業の有無と板書の中身は実行時に決まる。
    ///
    /// 位置は屋内モデルの平面の座標（blender/kcd_interior/plan_lecture_dims.py・plan_lecture_rooms_dims.py）で決め、
    /// 101 の 3 つの POI から解いた平面 → Unity の写像（<see cref="LectureSeatmateStage.TryPlanMap"/>）で写す。
    /// 講義棟の部屋はすべて同じ平面の座標で書かれているので、部屋ごとの基準の empty からの差分を写せばよい。
    /// 202・304 の基準は黒板の empty（board_lecture_&lt;室&gt;）、104 の基準はゼミの POI（poi_lecture_seminar）。
    /// 屋内の当たり判定で床の高さを測るので、<see cref="InteriorStage.Build"/> のあとに呼ぶ。
    /// </summary>
    public static class ClassroomStage
    {
        public const string RootName = "Classes_lecture";
        public const string TimetablePath = "Assets/Data/Classes/timetable.json";
        public const string StudentName = "学生";
        public const string LecturerName = "講師";

        /// <summary>学生の体に使わない体（講師の体と、操作キャラの体）。</summary>
        private static readonly string[] ExcludedStudentBodies = { "prof", "mirai" };

        /// <summary>黒板の枠の中心の、板の床からの高さ（板は床から 0.9〜2.1 m）。</summary>
        private const float BoardCenterHeight = 1.5f;

        /// <summary>ノート（机の天板の上）の高さ（天板は 0.70〜0.73 m）。</summary>
        private const float NoteHeight = 0.75f;

        /// <summary>座る点からノートの中心まで（座る人の前へ, m）。</summary>
        private const float NoteReach = 0.5f;

        /// <summary>板書の枠。題目を含めて 5 行が収まる高さ。</summary>
        private static readonly Vector2 TextFrame = new Vector2(5.6f, 1.05f);

        private static readonly Color32 Chalk = new Color32(236, 238, 228, 255);
        private const float TextMaxSize = 1.8f;
        private const float TextMinSize = 0.5f;

        /// <summary>部屋の箱の、床から上の高さと下の余裕（m）。</summary>
        private const float RoomHeight = 3.2f;

        private const float RoomBelow = 0.5f;

        /// <summary>1 室ぶんの配置（平面の座標）。</summary>
        public sealed class RoomPlan
        {
            public string Id;

            /// <summary>基準の empty の名前と、その平面の座標。empty の高さは床から AnchorLift の所。</summary>
            public string Anchor;

            public Vector2 AnchorPlan;
            public float AnchorLift;

            /// <summary>部屋の内法（平面, x0..x1 × y0..y1）。</summary>
            public Rect Inside;

            /// <summary>黒板の面の中心（平面）と、板から部屋へ向かう向き（平面）。</summary>
            public Vector2 BoardPlan;

            public Vector2 ToRoom;

            /// <summary>講師の立つ点（平面）。</summary>
            public Vector2 LecturerPlan;

            /// <summary>学生の座る点（平面）。全員が黒板の側（-ToRoom）を向く。</summary>
            public Vector2[] Seats;
        }

        /// <summary>
        /// 型 A の部屋（202・304）の座る点。机の k 列目の天板の後ろの縁は x = xf1 − 3.15 − k、
        /// 長椅子の座る点はその 0.38 後ろ。机の中心は Y 7.55 / 10.55 / 13.55 / 16.55 で、1 つの机に 3 人（中心と ±0.6）座れる。
        /// </summary>
        public static Vector2 TypeASeat(float xf1, int column, float deskY, float side)
        {
            return new Vector2(xf1 - 3.15f - column - 0.38f, deskY + side);
        }

        /// <summary>104 の座る点。机の中心から、椅子は左右 ±0.40・後ろ 0.55。</summary>
        public static Vector2 SeminarSeat(float deskX, float deskY, float side)
        {
            return new Vector2(deskX + side, deskY - 0.55f);
        }

        /// <summary>
        /// 3 室の配置。202（xf1 6.82、13 列）・304（xf1 −3.58、9 列）は東の壁に黒板（中心 Y 11.97・面 xf1 − 0.031）、
        /// 104 は北の壁に黒板（X 11.0〜18.2・面 Y 49.934）。104 の席はゼミの POI（14.6, 42.0）と NPC の点（12.0, 44.0）の
        /// 半径 1 m を避ける。
        /// </summary>
        public static readonly RoomPlan[] Rooms =
        {
            new RoomPlan
            {
                Id = "104",
                Anchor = "poi_lecture_seminar",
                AnchorPlan = new Vector2(14.6f, 42.0f),
                AnchorLift = 0f,
                Inside = Rect.MinMaxRect(7.38f, 35.26f, 21.52f, 49.94f),
                BoardPlan = new Vector2(14.6f, 49.934f),
                ToRoom = new Vector2(0f, -1f),
                LecturerPlan = new Vector2(16.2f, 49.3f),
                Seats = new[]
                {
                    SeminarSeat(11.85f, 46.5f, -0.4f), SeminarSeat(15.75f, 46.5f, 0.4f),
                    SeminarSeat(9.9f, 44.8f, 0.4f), SeminarSeat(17.7f, 44.8f, -0.4f),
                    SeminarSeat(19.65f, 43.1f, -0.4f), SeminarSeat(11.85f, 41.4f, -0.4f),
                    SeminarSeat(17.7f, 41.4f, 0.4f), SeminarSeat(9.9f, 39.7f, 0.4f),
                    SeminarSeat(15.75f, 39.7f, -0.4f),
                },
            },
            new RoomPlan
            {
                Id = "202",
                Anchor = "board_lecture_202",
                AnchorPlan = new Vector2(6.82f - 0.031f, 11.97f),
                AnchorLift = BoardCenterHeight,
                Inside = Rect.MinMaxRect(-10.82f, 4.3f, 6.82f, 19.64f),
                BoardPlan = new Vector2(6.82f - 0.031f, 11.97f),
                ToRoom = new Vector2(-1f, 0f),
                LecturerPlan = new Vector2(6.82f - 0.62f, 13.17f),
                Seats = new[]
                {
                    TypeASeat(6.82f, 1, 10.55f, 0.6f), TypeASeat(6.82f, 1, 13.55f, -0.6f),
                    TypeASeat(6.82f, 2, 7.55f, 0f), TypeASeat(6.82f, 3, 13.55f, 0.6f),
                    TypeASeat(6.82f, 3, 16.55f, 0f), TypeASeat(6.82f, 4, 10.55f, -0.6f),
                    TypeASeat(6.82f, 5, 13.55f, 0f), TypeASeat(6.82f, 6, 7.55f, 0.6f),
                },
            },
            new RoomPlan
            {
                Id = "304",
                Anchor = "board_lecture_304",
                AnchorPlan = new Vector2(-3.58f - 0.031f, 11.97f),
                AnchorLift = BoardCenterHeight,
                Inside = Rect.MinMaxRect(-17.72f, 4.3f, -3.58f, 19.64f),
                BoardPlan = new Vector2(-3.58f - 0.031f, 11.97f),
                ToRoom = new Vector2(-1f, 0f),
                LecturerPlan = new Vector2(-3.58f - 0.62f, 13.17f),
                Seats = new[]
                {
                    TypeASeat(-3.58f, 1, 13.55f, 0f), TypeASeat(-3.58f, 1, 7.55f, 0.6f),
                    TypeASeat(-3.58f, 2, 10.55f, -0.6f), TypeASeat(-3.58f, 2, 16.55f, 0f),
                    TypeASeat(-3.58f, 3, 13.55f, -0.6f), TypeASeat(-3.58f, 4, 10.55f, 0.6f),
                    TypeASeat(-3.58f, 5, 16.55f, -0.6f),
                },
            },
        };

        public static void Build(Transform root)
        {
            Transform interior = Find(root, LectureSeatmateStage.InteriorName);
            if (interior == null)
            {
                EditorPaths.Report(LectureSeatmateStage.InteriorName + " が無いので、授業をしている教室は置きませんでした。");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TimetablePath);
            ClassTimetable timetable = asset != null ? ClassTimetable.Parse(asset.text) : null;
            var scheduleAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MobStage.SchedulePath);
            MobSchedule schedule = scheduleAsset != null ? MobSchedule.Parse(scheduleAsset.text) : null;
            List<MobSchedule.Variant> students = StudentVariants(schedule);
            if (timetable == null || timetable.Lecturers.Count == 0 || students.Count == 0)
            {
                EditorPaths.Report("時間割か学生の見た目が読めないので、授業をしている教室は置きませんでした: " + TimetablePath);
                return;
            }

            Physics.SyncTransforms();
            if (!LectureSeatmateStage.TryPlanMap(interior, out LectureSeatmateStage.Matrix2 map, out _, out _))
            {
                return;
            }

            var group = new GameObject(RootName);
            group.transform.SetParent(interior, false);
            TMP_FontAsset font = FontLibrary.Ensure();
            var cache = new Dictionary<string, Material>();
            int placed = 0;
            for (int r = 0; r < Rooms.Length; r++)
            {
                if (BuildRoom(group.transform, interior, map, Rooms[r], r, timetable, students, font, cache))
                {
                    placed++;
                }
            }

            AssetDatabase.SaveAssets();
            EditorPaths.Report("授業をしている教室を " + placed + "/" + Rooms.Length + " 室に置きました。");
        }

        /// <summary>学生に使う見た目（講師と操作キャラの体を除く）。</summary>
        public static List<MobSchedule.Variant> StudentVariants(MobSchedule schedule)
        {
            var result = new List<MobSchedule.Variant>();
            if (schedule == null)
            {
                return result;
            }

            foreach (MobSchedule.Variant variant in schedule.Variants)
            {
                if (System.Array.IndexOf(ExcludedStudentBodies, variant.BodyId) < 0)
                {
                    result.Add(variant);
                }
            }

            return result;
        }

        /// <summary>平面の点を Unity へ写す（基準の empty からの差分）。高さは別に渡す。</summary>
        public static Vector3 ToWorld(LectureSeatmateStage.Matrix2 map, Vector3 anchor, Vector2 anchorPlan, Vector2 plan, float y)
        {
            Vector2 offset = map.Apply(plan - anchorPlan);
            return new Vector3(anchor.x + offset.x, y, anchor.z + offset.y);
        }

        /// <summary>平面の向きを Unity の水平の向きへ写す。</summary>
        public static Vector3 ToWorldDirection(LectureSeatmateStage.Matrix2 map, Vector2 plan)
        {
            Vector2 d = map.Apply(plan);
            return new Vector3(d.x, 0f, d.y).normalized;
        }

        private static bool BuildRoom(Transform group, Transform interior, LectureSeatmateStage.Matrix2 map, RoomPlan plan,
            int roomIndex, ClassTimetable timetable, List<MobSchedule.Variant> students, TMP_FontAsset font,
            Dictionary<string, Material> cache)
        {
            Transform anchor = Find(interior, plan.Anchor);
            if (anchor == null)
            {
                EditorPaths.Report(plan.Anchor + " が無いので、" + plan.Id + " の授業は置きませんでした。");
                return false;
            }

            Vector3 probe = ToWorld(map, anchor.position, plan.AnchorPlan, plan.Inside.center, anchor.position.y - plan.AnchorLift);
            float floorY = CollectibleStage.IndoorFloor(probe).y;
            Vector3 toRoom = ToWorldDirection(map, plan.ToRoom);
            if (toRoom.sqrMagnitude < 0.5f)
            {
                EditorPaths.Report(plan.Id + " の黒板の向きが解けないので、この部屋の授業は置きませんでした。");
                return false;
            }

            var room = new GameObject("Class_" + plan.Id);
            room.transform.SetParent(group, false);

            Vector3 boardCenter = ToWorld(map, anchor.position, plan.AnchorPlan, plan.BoardPlan, floorY + BoardCenterHeight);
            Transform frame = BuildBoard(room.transform, boardCenter, toRoom, font, out TextMeshPro text);

            var people = new List<GameObject>();
            var seats = new List<Classroom.Seat>();
            Vector3 audience = Vector3.zero;
            Vector3 facing = -toRoom;
            Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
            for (int i = 0; i < plan.Seats.Length; i++)
            {
                Vector3 seat = ToWorld(map, anchor.position, plan.AnchorPlan, plan.Seats[i], floorY);
                MobSchedule.Variant variant = students[StudentLook(roomIndex, i, students.Count)];
                GameObject student = PlaceStudent(room.transform, plan.Id, i, variant, seat, facing, cache, out Classroom.Seat sit);
                sit.Note = seat + facing * NoteReach + Vector3.up * NoteHeight;
                sit.Right = right;
                sit.Phase = i * 1.37f + roomIndex * 0.61f;
                people.Add(student);
                seats.Add(sit);
                audience += seat;
            }

            audience = audience / Mathf.Max(1, plan.Seats.Length) + Vector3.up * 1.1f;

            Vector3 standing = ToWorld(map, anchor.position, plan.AnchorPlan, plan.LecturerPlan, floorY);
            GameObject lecturer = PlaceLecturer(room.transform, plan.Id, timetable, standing, toRoom, cache,
                out Transform body, out Renderer[] renderers, out Classroom.LecturerLook[] looks);
            people.Add(lecturer);

            foreach (GameObject person in people)
            {
                person.SetActive(false);
            }

            Classroom classroom = room.AddComponent<Classroom>();
            classroom.Configure(plan.Id, floorY, RoomBounds(map, anchor.position, plan, floorY), people.ToArray(),
                seats.ToArray(), lecturer.transform, body, renderers, looks, text, frame, TextFrame.x, audience);
            EditorPaths.Report(plan.Id + " に授業の教室を置きました（学生 " + seats.Count + " 人, 床 "
                + floorY.ToString("F2") + "）。");
            return true;
        }

        /// <summary>
        /// 部屋の i 番目の学生の見た目の番号。部屋ごとに始まりをずらし、1 部屋の中では続きの番号を使うので、
        /// 見た目の数が席の数以上なら 1 部屋に同じ見た目は並ばない。
        /// </summary>
        public static int StudentLook(int roomIndex, int seat, int count)
        {
            return count <= 0 ? 0 : (roomIndex * 9 + seat) % count;
        }

        /// <summary>部屋の内法を写した箱（ワールド）。</summary>
        public static Bounds RoomBounds(LectureSeatmateStage.Matrix2 map, Vector3 anchor, RoomPlan plan, float floorY)
        {
            Rect inside = plan.Inside;
            var corners = new[]
            {
                new Vector2(inside.xMin, inside.yMin), new Vector2(inside.xMax, inside.yMin),
                new Vector2(inside.xMin, inside.yMax), new Vector2(inside.xMax, inside.yMax),
            };
            var bounds = new Bounds(ToWorld(map, anchor, plan.AnchorPlan, corners[0], floorY - RoomBelow), Vector3.zero);
            foreach (Vector2 corner in corners)
            {
                bounds.Encapsulate(ToWorld(map, anchor, plan.AnchorPlan, corner, floorY - RoomBelow));
                bounds.Encapsulate(ToWorld(map, anchor, plan.AnchorPlan, corner, floorY + RoomHeight));
            }

            return bounds;
        }

        /// <summary>板書の枠。原点は板の面の上、+Z は部屋の側。文字は左上から書き、中身は実行時に入る。</summary>
        private static Transform BuildBoard(Transform room, Vector3 center, Vector3 toRoom, TMP_FontAsset font,
            out TextMeshPro text)
        {
            text = null;
            var go = new GameObject("Board");
            go.transform.SetParent(room, true);
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(toRoom, Vector3.up));
            if (font == null)
            {
                EditorPaths.Report("日本語フォントが無いので、授業の板書の文字は置きませんでした。");
                return go.transform;
            }

            GameObject textObject = LectureBoardStage.AddText(go.transform, "BoardText", font, string.Empty,
                Vector3.zero, TextFrame, Chalk);
            text = textObject.GetComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.fontSizeMax = TextMaxSize;
            text.fontSizeMin = TextMinSize;
            text.fontSize = TextMaxSize;
            text.lineSpacing = 8f;
            return go.transform;
        }

        private static GameObject PlaceStudent(Transform room, string roomId, int index, MobSchedule.Variant variant,
            Vector3 seat, Vector3 facing, Dictionary<string, Material> cache, out Classroom.Seat sit)
        {
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            GameObject npc = ActorFactory.CreateNpc(room, variant.BodyId, StudentName, seat, yaw, 0f);
            npc.name = "Student_" + roomId + "_" + index;
            StripWalking(npc);
            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = Classroom.StudentDialogueId;
                talker.DisplayName = StudentName;
                talker.TurnSpeed = 0f;
            }

            GameObject body = BodyOf(npc);
            sit = new Classroom.Seat();
            if (body != null)
            {
                MobStage.PaintNear(body, variant, cache);
                MobStage.FitBodyHeight(npc.transform, body, variant.Height);
                sit.Animator = body.GetComponent<Animator>();
                sit.Pose = body.GetComponent<SitPose>();
                if (sit.Animator != null)
                {
                    sit.Animator.keepAnimatorStateOnDisable = true;
                }
            }

            return npc;
        }

        private static GameObject PlaceLecturer(Transform room, string roomId, ClassTimetable timetable, Vector3 standing,
            Vector3 toRoom, Dictionary<string, Material> cache, out Transform bodyTransform, out Renderer[] renderers,
            out Classroom.LecturerLook[] looks)
        {
            MobSchedule.Variant first = timetable.Lecturers[0].Look;
            float yaw = Mathf.Atan2(toRoom.x, toRoom.z) * Mathf.Rad2Deg;
            GameObject npc = ActorFactory.CreateNpc(room, first.BodyId, LecturerName, standing, yaw, 0f);
            npc.name = "Lecturer_" + roomId;
            StripWalking(npc);
            NPCTalker talker = npc.GetComponent<NPCTalker>();
            if (talker != null)
            {
                talker.NpcId = Classroom.LecturerDialogueId;
                talker.DisplayName = LecturerName;
                talker.TurnSpeed = 0f;
            }

            GameObject body = BodyOf(npc);
            bodyTransform = body != null ? body.transform : null;
            renderers = System.Array.Empty<Renderer>();
            looks = System.Array.Empty<Classroom.LecturerLook>();
            if (body == null)
            {
                return npc;
            }

            // 講師は座らない。手を黒板へ伸ばす IK を足す。
            SitPose sit = body.GetComponent<SitPose>();
            if (sit != null)
            {
                Object.DestroyImmediate(sit);
            }

            body.AddComponent<LecturerGesture>();
            Animator animator = body.GetComponent<Animator>();
            if (animator != null)
            {
                animator.keepAnimatorStateOnDisable = true;
            }

            renderers = EnabledRenderers(body);
            Material[][] original = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                original[i] = renderers[i].sharedMaterials;
            }

            Vector3 originalScale = body.transform.localScale;
            looks = new Classroom.LecturerLook[timetable.Lecturers.Count];
            for (int f = 0; f < timetable.Lecturers.Count; f++)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterials = original[i];
                }

                body.transform.localScale = originalScale;
                ClassTimetable.Lecturer lecturer = timetable.Lecturers[f];
                Material[][] painted = MobStage.PaintNear(body, lecturer.Look, cache);
                MobStage.FitBodyHeight(npc.transform, body, lecturer.Look.Height);
                var sets = new Classroom.MaterialSet[painted.Length];
                for (int i = 0; i < painted.Length; i++)
                {
                    sets[i] = new Classroom.MaterialSet { Materials = painted[i] };
                }

                looks[f] = new Classroom.LecturerLook
                {
                    Field = lecturer.Field,
                    Renderers = sets,
                    BodyScale = body.transform.localScale,
                };
            }

            // シーンには最初の分野の見た目で置く（実行時に授業の分野へ替わる）。
            for (int i = 0; i < renderers.Length && looks.Length > 0; i++)
            {
                renderers[i].sharedMaterials = looks[0].Renderers[i].Materials;
            }

            if (looks.Length > 0)
            {
                body.transform.localScale = looks[0].BodyScale;
            }

            return npc;
        }

        /// <summary><see cref="MobStage.PaintNear"/> と同じ並びの renderer（有効なものだけ、子の順）。</summary>
        private static Renderer[] EnabledRenderers(GameObject body)
        {
            var result = new List<Renderer>();
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                {
                    result.Add(renderer);
                }
            }

            return result.ToArray();
        }

        /// <summary>屋内に NavMesh は無いので、歩く部品を外して動かないようにする。</summary>
        private static void StripWalking(GameObject npc)
        {
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
        }

        private static GameObject BodyOf(GameObject npc)
        {
            Transform body = npc.transform.Find("Body");
            return body != null ? body.gameObject : null;
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
