using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の、時間割どおりに授業をしている教室 (#439)。月曜 2 限の途中に講義棟へ入り、104・202・304 の
    /// 後ろに立つと、講師と学生が出て、板書が時間割の行数だけ書かれ、入った教室では「授業中」の会話が始まる。
    /// 昼休みにすると人は消え、板書も消える。目で確かめる絵（後ろから黒板・黒板の脇から学生）は、
    /// 描画のデバイスがあるときだけ撮って unity/logs/classroom/ に置く。
    /// </summary>
    public sealed class ClassroomPlayTests : SceneTestBase
    {
        private const string BuildingId = "lecture";

        /// <summary>月曜 2 限（10:40〜12:00）の 50 分過ぎ。3 室とも授業がある。</summary>
        private const float InClassHour = 11.5f;

        /// <summary>昼休み。どの教室も空。</summary>
        private const float LunchHour = 12.5f;

        /// <summary>後ろに立つ位置の、板からの距離（いちばん遠い席より奥, m）。</summary>
        private const float BehindLastSeat = 1.2f;

        [UnityTest]
        // 絵を撮るときは時限ごとの板書と講師の 3 つの動きも撮るので、ほかのシーンのテストより長く待つ。
        [Timeout(SceneTestTimeoutMs * 2)]
        public IEnumerator ClassesFollowTheTimetableAndEmptyAtLunch()
        {
            CollectErrorsInsteadOfLogAssert();
            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "キャンパスに PlayerController が無い");
            DayNightCycle cycle = Object.FindAnyObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle, "DayNightCycle が無い");
            cycle.Paused = true;
            cycle.SetHours(InClassHour);

            var driver = new CampusDriver(player, InteriorLoader.Instance, DialogueSystem.Instance,
                Object.FindAnyObjectByType<FormalEnding>());
            yield return driver.Enter(BuildingId);
            Assert.AreEqual(BuildingId, InteriorLoader.Instance.CurrentId, "講義棟へ入れない");

            var rooms = new List<Classroom>(Object.FindObjectsByType<Classroom>(FindObjectsInactive.Include));
            rooms.Sort((a, b) => string.CompareOrdinal(a.RoomId, b.RoomId));
            Assert.AreEqual(3, rooms.Count, "教室が 3 室ない");

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "classroom"));
            if (CanRender)
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Debug.Log("[classroom] 描画のデバイスが無い（-nographics）ので絵は撮らない");
            }

            var bad = new List<string>();
            int day = GameManager.Instance.DayNumber;
            foreach (Classroom room in rooms)
            {
                Vector3 back = BackOf(room, out Vector3 boardFacing);
                player.Teleport(back, Quaternion.LookRotation(-boardFacing).eulerAngles.y);
                yield return PlayModeScenes.Advance(10, 1.0f);

                ClassTimetable.Session expected = Classroom.Timetable.SessionAt(room.RoomId, day, InClassHour);
                CheckInClass(room, expected, bad);
                CheckEntryTalk(room, bad);
                yield return CloseDialogue();

                if (CanRender)
                {
                    Transform frame = room.Board.transform.parent;
                    yield return Shoot(Path.Combine(folder, room.RoomId + "-back.png"),
                        back + Vector3.up * 1.6f, frame.position);
                    Vector3 side = frame.position + boardFacing * 1.2f + Vector3.Cross(Vector3.up, boardFacing) * 2.2f;
                    side.y = room.FloorY + 1.9f;
                    yield return Shoot(Path.Combine(folder, room.RoomId + "-front.png"), side, Audience(room));
                    Vector3 near = frame.position + boardFacing * 3.2f + Vector3.up * 0.1f;
                    yield return Shoot(Path.Combine(folder, room.RoomId + "-board.png"), near, frame.position);
                }
            }

            if (CanRender)
            {
                yield return ShootPeriods(rooms, player, cycle, folder, bad);
            }

            yield return CheckBeforeClass(rooms, player, cycle, folder, bad);
            yield return CheckQuiz(rooms, player, cycle, folder, bad);

            cycle.SetHours(LunchHour);
            yield return PlayModeScenes.Advance(10, 1.0f);
            foreach (Classroom room in rooms)
            {
                player.Teleport(BackOf(room, out _), 0f);
                yield return PlayModeScenes.Advance(10, 1.0f);
                if (room.InSession)
                {
                    bad.Add(room.RoomId + ": 昼休みに授業がある");
                }

                foreach (GameObject person in room.People)
                {
                    if (person.activeSelf)
                    {
                        bad.Add(room.RoomId + ": 昼休みに " + person.name + " が残っている");
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(room.Board.text))
                {
                    bad.Add(room.RoomId + ": 昼休みに板書が残っている");
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
            AssertNoErrors("時間割どおりの授業");
        }

        /// <summary>目で確かめる時刻（月曜）。1 限の途中、2 限の始まり、2 限の終わり際、4 限の途中。</summary>
        private static readonly float[] ShotHours = { 9.5f, 10.7f, 11.9f, 15f };

        /// <summary>
        /// 時限ごとの板書と、講師の 3 つの動き（書く・指す・振り返る）を撮る。
        /// 板書は時刻ごとに後ろから、講師の動きは 2 限の始まってすぐに黒板の脇から、それぞれの動きに入って向きが収まった時点で撮る。
        /// </summary>
        private static IEnumerator ShootPeriods(List<Classroom> rooms, PlayerController player, DayNightCycle cycle,
            string folder, List<string> bad)
        {
            int day = GameManager.Instance.DayNumber;
            DialogueSystem dialogue = DialogueSystem.Instance;
            foreach (Classroom room in rooms)
            {
                Vector3 back = BackOf(room, out Vector3 boardFacing);
                Transform frame = room.Board.transform.parent;
                foreach (float hour in ShotHours)
                {
                    cycle.SetHours(hour);
                    player.Teleport(back, Quaternion.LookRotation(-boardFacing).eulerAngles.y);
                    yield return PlayModeScenes.Advance(5, 0.5f);
                    yield return CloseDialogue();
                    ClassTimetable.Session expected = Classroom.Timetable.SessionAt(room.RoomId, day, hour);
                    string text = room.Board.text;
                    int lines = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
                    if (lines != expected.BoardLines)
                    {
                        bad.Add(room.RoomId + " " + hour + " 時: 板書が " + lines + " 行（時間割では " + expected.BoardLines + " 行）");
                    }

                    string name = room.RoomId + "-" + Mathf.RoundToInt(hour * 60f).ToString("0000") + "-"
                        + (expected.IsInClass ? expected.PeriodNumber + "-" + expected.SubjectId : "free") + ".png";
                    Vector3 near = frame.position + boardFacing * 3.2f + Vector3.up * 0.1f;
                    yield return Shoot(Path.Combine(folder, name), near, frame.position);
                    Debug.Log("[classroom] " + name + " 板書 " + lines + " 行");
                }

                if (!Classroom.Timetable.SessionAt(room.RoomId, day, LecturerShotHours).IsInClass)
                {
                    // 講師は授業中にしか出ないので、空きの時限の部屋は動きを撮らない。
                    Debug.Log("[classroom] " + room.RoomId + " は " + LecturerShotHours + " 時に授業が無いので講師を撮らない");
                    continue;
                }

                // ゲームの 1 分は実時間 0.5 秒なので、時限の終わり近くだと撮っている間に授業が終わる。始まってすぐの時刻で撮る。
                cycle.SetHours(LecturerShotHours);
                player.Teleport(back, Quaternion.LookRotation(-boardFacing).eulerAngles.y);
                yield return PlayModeScenes.Advance(5, 0.5f);
                yield return CloseDialogue();
                Vector3 side = frame.position + boardFacing * 2.4f + Vector3.Cross(Vector3.up, boardFacing) * 2.6f;
                side.y = room.FloorY + 1.7f;
                Vector3 focus = room.Lecturer.position + Vector3.up * 1.3f;
                Classroom.LecturerMotion[] motions =
                {
                    Classroom.LecturerMotion.Write, Classroom.LecturerMotion.Point, Classroom.LecturerMotion.Turn,
                };
                foreach (Classroom.LecturerMotion motion in motions)
                {
                    string path = Path.Combine(folder, room.RoomId + "-lecturer-" + motion.ToString().ToLowerInvariant() + ".png");
                    bool caught = false;
                    for (int attempt = 0; attempt < LecturerShotAttempts && !caught; attempt++)
                    {
                        // 時計を戻すのは授業中を保つため。動きの位相は Time.time で決まるので、これでは揃わない。
                        cycle.SetHours(LecturerShotHours);
                        yield return WaitForMotion(room, motion);
                        yield return Shoot(path, side, focus);
                        // 撮っている間に次の動きへ移っていたら、名前と画が食い違うので撮り直す。
                        caught = AtPose(room, motion);
                    }

                    if (!caught)
                    {
                        bad.Add(room.RoomId + ": 講師の「" + motion + "」を撮れなかった");
                    }
                }
            }
        }

        /// <summary>授業の前の、学生が集まってくる時刻（月曜 2 限の 4 分前、10:36）。</summary>
        private const float BeforeShotHour = 10.6f;

        /// <summary>小テストの時間に入ってからの分。小テストは始まりから ClassTimetable.QuizMinutes 分続く。</summary>
        private const float QuizShotMinutes = 5f;

        /// <summary>
        /// 授業の前は、着いた学生だけが席にいる (#424)。席にいる人数が ClassAttendance の決める人数と合い、
        /// 誰もいないのでも全員でもない途中の人数になっているかを確かめ、後ろから撮る。
        /// </summary>
        private static IEnumerator CheckBeforeClass(List<Classroom> rooms, PlayerController player, DayNightCycle cycle,
            string folder, List<string> bad)
        {
            int day = GameManager.Instance.DayNumber;
            cycle.SetHours(BeforeShotHour);
            foreach (Classroom room in rooms)
            {
                Vector3 back = BackOf(room, out Vector3 boardFacing);
                player.Teleport(back, Quaternion.LookRotation(-boardFacing).eulerAngles.y);
                yield return PlayModeScenes.Advance(5, 0.5f);
                yield return CloseDialogue();
                ClassAttendance.Moment moment = ClassAttendance.At(Classroom.Timetable, room.RoomId, day, BeforeShotHour);
                if (moment.Phase != ClassAttendance.Phase.Before)
                {
                    bad.Add(room.RoomId + ": " + BeforeShotHour + " 時が授業の前になっていない（テストの前提が崩れている）");
                    continue;
                }

                int expected = ClassAttendance.TakenCount(moment, room.Seats.Count);
                int seated = SeatedCount(room);
                if (seated != expected)
                {
                    bad.Add(room.RoomId + ": 授業の前に席にいる学生が " + seated + " 人（" + expected + " 人のはず）");
                }

                if (expected == 0 || expected == room.Seats.Count)
                {
                    bad.Add(room.RoomId + ": 授業の 4 分前の人数が途中の人数でない（" + expected + "/" + room.Seats.Count + "）");
                }

                if (CanRender)
                {
                    string name = room.RoomId + "-" + Mathf.RoundToInt(BeforeShotHour * 60f).ToString("0000")
                        + "-before-" + seated + ".png";
                    yield return Shoot(Path.Combine(folder, name), back + Vector3.up * 1.6f, room.Board.transform.parent.position);
                    Debug.Log("[classroom] " + name + " 席 " + seated + "/" + room.Seats.Count);
                }
            }
        }

        /// <summary>
        /// 時間割の最初の小テストの曜日・時限に合わせ、始まって数分の教室で小テストをしているかを確かめて撮る。
        /// 小テストは月曜には無いので、日を小テストの曜日に動かし、終わったら戻す。
        /// </summary>
        private static IEnumerator CheckQuiz(List<Classroom> rooms, PlayerController player, DayNightCycle cycle,
            string folder, List<string> bad)
        {
            ClassTimetable timetable = Classroom.Timetable;
            if (timetable.Quizzes.Count == 0)
            {
                bad.Add("時間割に小テストが無い");
                yield break;
            }

            ClassTimetable.QuizSlot slot = timetable.Quizzes[0];
            Classroom room = rooms.Find(r => r.RoomId == slot.RoomId);
            ClassTimetable.Period period = null;
            foreach (ClassTimetable.Period p in timetable.Periods)
            {
                if (p.Number == slot.Period)
                {
                    period = p;
                }
            }

            int quizDay = -1;
            for (int d = 1; d <= 7 && quizDay < 0; d++)
            {
                if (ClassTimetable.WeekdayOf(d) == slot.Weekday)
                {
                    quizDay = d;
                }
            }

            if (room == null || period == null || quizDay < 0)
            {
                bad.Add("小テストの教室 " + slot.RoomId + " の " + slot.Period + " 限が見つからない");
                yield break;
            }

            GameManager game = GameManager.Instance;
            int day = game.DayNumber;
            game.DayNumber = quizDay;
            try
            {
                float hour = period.StartHour + QuizShotMinutes / 60f;
                cycle.SetHours(hour);
                Vector3 back = BackOf(room, out Vector3 boardFacing);
                player.Teleport(back, Quaternion.LookRotation(-boardFacing).eulerAngles.y);
                yield return PlayModeScenes.Advance(5, 0.5f);
                yield return CloseDialogue();
                if (!room.InQuiz)
                {
                    bad.Add(room.RoomId + ": " + quizDay + " 日目の " + slot.Period + " 限の始めに小テストをしていない");
                }

                if (CanRender)
                {
                    string name = room.RoomId + "-quiz-" + slot.Period + ".png";
                    yield return Shoot(Path.Combine(folder, name), back + Vector3.up * 1.6f, room.Board.transform.parent.position);
                    Debug.Log("[classroom] " + name + " 小テスト " + room.InQuiz);
                }
            }
            finally
            {
                game.DayNumber = day;
            }
        }

        /// <summary>席にいる学生の数。People の先頭から席の数までが学生。</summary>
        private static int SeatedCount(Classroom room)
        {
            int count = 0;
            for (int i = 0; i < room.Seats.Count && i < room.People.Count; i++)
            {
                if (room.People[i] != null && room.People[i].activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>講師の動きを撮る時刻（月曜 2 限の始まってすぐ、10:42）。</summary>
        private const float LecturerShotHours = 10.7f;

        /// <summary>
        /// 講師が回り終えてから、さらに待つ秒数。手の重みは LecturerGesture.BlendSeconds（0.35 秒）で移るので、それより少し長く待つ。
        /// いちばん短い「指す」は 1.5 秒で、向き替え（最大約 135 度を 200 度/秒で約 0.7 秒）とこの待ちを引いても 0.4 秒ほど残る。
        /// </summary>
        private const float MotionSettleSeconds = 0.4f;

        /// <summary>撮っている間に動きが移ったときに撮り直す回数。残りの 0.4 秒を外すのはフレームが重いときだけなので、3 回あれば足りる。</summary>
        private const int LecturerShotAttempts = 3;

        /// <summary>講師がその動きをしていて、向きが回り切っているか。</summary>
        private static bool AtPose(Classroom room, Classroom.LecturerMotion motion)
        {
            return room.Motion == motion && room.LecturerFacingSettled;
        }

        /// <summary>講師がその動きの向きまで回り切り、手が収まるまで待つ。</summary>
        private static IEnumerator WaitForMotion(Classroom room, Classroom.LecturerMotion motion)
        {
            float settledAt = -1f;
            float giveUp = Time.time + Classroom.GestureCycle * 4f;
            while (Time.time < giveUp)
            {
                if (AtPose(room, motion))
                {
                    if (settledAt < 0f)
                    {
                        settledAt = Time.time;
                    }
                    else if (Time.time - settledAt >= MotionSettleSeconds)
                    {
                        yield break;
                    }
                }
                else
                {
                    settledAt = -1f;
                }

                yield return null;
            }
        }

        /// <summary>授業中の人と板書が時間割どおりか。</summary>
        private static void CheckInClass(Classroom room, ClassTimetable.Session expected, List<string> bad)
        {
            if (!expected.IsInClass)
            {
                bad.Add(room.RoomId + ": 月曜 2 限が時間割で空き（テストの前提が崩れている）");
                return;
            }

            if (!room.InSession)
            {
                bad.Add(room.RoomId + ": 授業中のはずが授業が無い");
            }

            foreach (GameObject person in room.People)
            {
                if (!person.activeInHierarchy)
                {
                    bad.Add(room.RoomId + ": 授業中に " + person.name + " が出ていない");
                    break;
                }
            }

            string text = room.Board.text;
            int lines = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
            if (lines != expected.BoardLines)
            {
                bad.Add(room.RoomId + ": 板書が " + lines + " 行（時間割では " + expected.BoardLines + " 行）");
            }

            Debug.Log("[classroom] " + room.RoomId + " " + expected.SubjectId + " 板書 " + lines + " 行, 人 " + room.People.Count);
        }

        /// <summary>入った教室では「授業中」の短い会話が始まる。</summary>
        private static void CheckEntryTalk(Classroom room, List<string> bad)
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null || !dialogue.IsPlaying || dialogue.DialogueId != Classroom.EntryDialogueId)
            {
                bad.Add(room.RoomId + ": 入っても授業中の会話が始まらない");
            }
        }

        private static IEnumerator CloseDialogue()
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            for (int i = 0; i < 20 && dialogue != null && dialogue.IsPlaying; i++)
            {
                dialogue.Advance();
                yield return PlayModeScenes.Advance(3, 0.15f);
            }
        }

        /// <summary>教室の後ろ（いちばん遠い席の奥）の床。boardFacing は板の部屋を向く側。</summary>
        private static Vector3 BackOf(Classroom room, out Vector3 boardFacing)
        {
            Transform frame = room.Board.transform.parent;
            boardFacing = frame.forward;
            boardFacing.y = 0f;
            boardFacing.Normalize();
            float far = 0f;
            foreach (Classroom.Seat seat in room.Seats)
            {
                far = Mathf.Max(far, Vector3.Dot(seat.Note - frame.position, boardFacing));
            }

            Vector3 back = frame.position + boardFacing * (far + BehindLastSeat);
            Bounds inside = room.Room;
            back.x = Mathf.Clamp(back.x, inside.min.x + 0.4f, inside.max.x - 0.4f);
            back.z = Mathf.Clamp(back.z, inside.min.z + 0.4f, inside.max.z - 0.4f);
            back.y = room.FloorY;
            return back;
        }

        private static Vector3 Audience(Classroom room)
        {
            Vector3 sum = Vector3.zero;
            foreach (Classroom.Seat seat in room.Seats)
            {
                sum += seat.Note;
            }

            return sum / Mathf.Max(1, room.Seats.Count);
        }

        private static IEnumerator Shoot(string path, Vector3 position, Vector3 focus)
        {
            const int width = 1280;
            const int height = 720;
            var go = new GameObject("ClassroomShot");
            var camera = go.AddComponent<Camera>();
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                go.transform.position = position;
                go.transform.rotation = Quaternion.LookRotation(focus - position, Vector3.up);
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.05f;
                camera.targetTexture = texture;
                camera.depth = 100f;
                yield return null;
                camera.Render();
                RenderTexture.active = texture;
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                File.WriteAllBytes(path, readable.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.Destroy(texture);
                Object.Destroy(readable);
                Object.Destroy(go);
            }
        }
    }
}
