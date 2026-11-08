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
        [Timeout(SceneTestTimeoutMs)]
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
