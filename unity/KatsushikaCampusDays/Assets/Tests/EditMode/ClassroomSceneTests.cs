using System.Collections.Generic;
using KCD.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の、時間割どおりに授業をしている教室 (#439)。SceneBuilder が置いた Campus.unity を開き、
    /// 104・202・304 に講師・着席した学生・板書があり、どれも部屋の中で、授業の外は姿を隠しているかを確かめる。
    /// </summary>
    public sealed class ClassroomSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>202・304 の板書の枠は、黒板の empty（板の面の中心）からこの距離の中（m）。</summary>
        private const float BoardNear = 0.3f;

        private Scene _scene;
        private readonly Dictionary<string, Classroom> _rooms = new Dictionary<string, Classroom>();
        private readonly Dictionary<string, Transform> _named = new Dictionary<string, Transform>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Classroom each in root.GetComponentsInChildren<Classroom>(true))
                {
                    _rooms[each.RoomId] = each;
                }

                foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!_named.ContainsKey(each.name))
                    {
                        _named[each.name] = each;
                    }
                }
            }
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void 教室は104と202と304の3室で_講義棟の屋内にある()
        {
            CollectionAssert.AreEquivalent(new[] { "104", "202", "304" }, _rooms.Keys);
            foreach (Classroom room in _rooms.Values)
            {
                Assert.AreEqual(ClassroomStage.RootName, room.transform.parent.name);
                Assert.AreEqual(LectureSeatmateStage.InteriorName, room.transform.parent.parent.name);
            }
        }

        [Test]
        public void 学生は6人以上が部屋の中で着席し_講師も部屋の中にいる()
        {
            foreach (Classroom room in _rooms.Values)
            {
                Assert.GreaterOrEqual(room.Seats.Count, 6, room.RoomId + " の学生が少ない");
                Assert.AreEqual(room.Seats.Count + 1, room.People.Count, room.RoomId + " の人数（学生 + 講師）");
                foreach (GameObject person in room.People)
                {
                    Assert.IsTrue(room.Room.Contains(person.transform.position),
                        room.RoomId + " の " + person.name + " が部屋の外にいる");
                    Assert.AreEqual(room.FloorY, person.transform.position.y, 0.2f, person.name + " が床から浮いている");
                    Assert.IsFalse(person.activeSelf, person.name + " は授業の外は隠しておく（授業中だけ出す）");
                    Assert.IsNull(person.GetComponent<NPCWander>(), "屋内に NavMesh は無いので歩かせない");
                    Assert.IsNull(person.GetComponent<UnityEngine.AI.NavMeshAgent>());
                }

                foreach (Classroom.Seat seat in room.Seats)
                {
                    Assert.IsNotNull(seat.Animator, room.RoomId + " の学生に Animator が無い");
                    Assert.IsNotNull(seat.Pose, room.RoomId + " の学生に SitPose が無い");
                    Assert.IsTrue(room.Room.Contains(seat.Note), room.RoomId + " のノートが部屋の外");
                }

                Assert.IsNotNull(room.Lecturer, room.RoomId + " の講師が無い");
                NPCTalker lecturer = room.Lecturer.GetComponent<NPCTalker>();
                Assert.AreEqual(Classroom.LecturerDialogueId, lecturer.NpcId);
                Assert.IsNotNull(room.Lecturer.GetComponentInChildren<LecturerGesture>(true), "講師の手の IK が無い");
                Assert.IsNull(room.Lecturer.GetComponentInChildren<SitPose>(true), "講師は座らない");
            }
        }

        [Test]
        public void 学生の会話は授業中の小声で_人数ぶんの体は同じ見た目ばかりではない()
        {
            foreach (Classroom room in _rooms.Values)
            {
                var bodies = new HashSet<string>();
                foreach (GameObject person in room.People)
                {
                    if (person.transform == room.Lecturer)
                    {
                        continue;
                    }

                    NPCTalker talker = person.GetComponent<NPCTalker>();
                    Assert.AreEqual(Classroom.StudentDialogueId, talker.NpcId, person.name);
                    Assert.AreEqual(ClassroomStage.StudentName, talker.DisplayName, person.name);
                    bodies.Add(DescribeLook(person));
                }

                Assert.GreaterOrEqual(bodies.Count, 3, room.RoomId + " の学生の見た目が揃いすぎ");
            }
        }

        [Test]
        public void 板書は黒板の面にあり_部屋の側を向く()
        {
            foreach (Classroom room in _rooms.Values)
            {
                Assert.IsNotNull(room.Board, room.RoomId + " の板書の文字が無い");
                Transform frame = room.Board.transform.parent;
                Assert.IsTrue(room.Room.Contains(frame.position), room.RoomId + " の板書が部屋の外");
                Assert.AreEqual(room.FloorY + 1.5f, frame.position.y, 0.2f, room.RoomId + " の板書の高さ");

                // 板の +Z は部屋の側。学生（の平均）は板の前にいる。
                Vector3 students = Vector3.zero;
                foreach (Classroom.Seat seat in room.Seats)
                {
                    students += seat.Note;
                }

                students /= room.Seats.Count;
                Vector3 toStudents = students - frame.position;
                toStudents.y = 0f;
                Assert.Greater(Vector3.Dot(frame.forward, toStudents.normalized), 0.8f, room.RoomId + " の板書が部屋を向いていない");
            }

            foreach (string id in new[] { "202", "304" })
            {
                Assert.IsTrue(_named.TryGetValue("board_lecture_" + id, out Transform board), "board_lecture_" + id + " が無い");
                Vector3 frame = _rooms[id].Board.transform.parent.position;
                Assert.Less(Vector3.Distance(frame, board.position), BoardNear,
                    id + " の板書が黒板の empty から離れている（平面の写像がずれている）");
            }
        }

        /// <summary>見た目の目印（体の renderer の材質名を並べたもの）。</summary>
        private static string DescribeLook(GameObject person)
        {
            var names = new List<string>();
            foreach (Renderer renderer in person.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    names.Add(material != null ? material.name : "-");
                }
            }

            return string.Join("|", names);
        }
    }
}
