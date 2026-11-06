using KCD.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 101 の隣の席の学生と、101 の黒板の板書 (#424)。SceneBuilder が置いた Campus.unity を開き、
    /// q_sq_pop_quiz の依頼主と talk 先が、101 の客席と前の黒板の、話しかけられる所にあるかを確かめる。
    /// </summary>
    public sealed class LectureSeatmateSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>学生は客席の empty から、黒板は教卓からこの距離の中（m、水平）。</summary>
        private const float NearSpot = 1f;

        private const float NearStage = 5f;

        private Scene _scene;
        private NPCTalker _seatmate;
        private NPCTalker _board;
        private Transform _seatSpot;
        private Transform _stage;
        private Transform _seats;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (NPCTalker each in root.GetComponentsInChildren<NPCTalker>(true))
                {
                    if (each.NpcId == LectureSeatmateStage.SeatmateDialogueId)
                    {
                        _seatmate = each;
                    }
                    else if (each.NpcId == LectureSeatmateStage.BoardDialogueId)
                    {
                        _board = each;
                    }
                }

                foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
                {
                    if (each.name == LectureSeatmateStage.SeatmateEmpty)
                    {
                        _seatSpot = each;
                    }
                    else if (each.name == LectureSeatmateStage.StageEmpty)
                    {
                        _stage = each;
                    }
                    else if (each.name == LectureSeatmateStage.SeatsEmpty)
                    {
                        _seats = each;
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
        public void 隣の席の学生は101の客席で話しかけられ_動かない()
        {
            Assert.IsNotNull(_seatmate, "tateishi の NPC が無い");
            Assert.AreEqual("NPC_tateishi", _seatmate.name);
            Assert.AreEqual(LectureSeatmateStage.SeatmateName, _seatmate.DisplayName);
            Assert.IsNull(_seatmate.GetComponent<NPCWander>(), "屋内に NavMesh は無いので歩かせない");
            Assert.IsNull(_seatmate.GetComponent<UnityEngine.AI.NavMeshAgent>());
            Assert.IsNotNull(_seatSpot, LectureSeatmateStage.SeatmateEmpty + " が無い");
            Assert.Less(Flat(_seatmate.transform.position - _seatSpot.position), NearSpot);
        }

        [Test]
        public void 黒板は教卓の前で調べられ_板書が客席を向いている()
        {
            Assert.IsNotNull(_board, "board_lecture_101 の判定が無い");
            Assert.AreEqual(LectureSeatmateStage.BoardTalkerName, _board.name);
            Assert.AreEqual(LayerMask.NameToLayer("Interactable"), _board.gameObject.layer);
            Assert.AreEqual(0f, _board.TurnSpeed, "黒板は向き直らない");
            Assert.AreEqual("ui.interact.inspect", _board.PromptKey);

            var box = _board.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.isTrigger);

            TextMeshPro chalk = _board.GetComponentInChildren<TextMeshPro>(true);
            Assert.IsNotNull(chalk, "101 の板書の文字が無い");
            Assert.AreEqual(LectureSeatmateStage.ChalkText, chalk.text);

            Assert.IsNotNull(_stage, LectureSeatmateStage.StageEmpty + " が無い");
            Assert.IsNotNull(_seats, LectureSeatmateStage.SeatsEmpty + " が無い");
            Assert.Less(Flat(_board.transform.position - _stage.position), NearStage);

            // 判定の +Z（板の手前）は客席の側を向き、教卓は黒板より客席に近い。
            Vector3 toSeats = _seats.position - _board.transform.position;
            Assert.Greater(Vector3.Dot(_board.transform.forward, toSeats), 0f, "板が客席を向いていない");
            Assert.Less(Flat(_stage.position - _seats.position), Flat(_board.transform.position - _seats.position),
                "黒板が教卓より客席の側にある");
        }

        [Test]
        public void 平面の3点から写す変換は_回転と平行移動を復元する()
        {
            // 平面の (x, y) を、90° 回して (10, 5) ずらした Unity の (x, z) に写す。
            Vector2 Rot(Vector2 p) => new Vector2(-p.y + 10f, p.x + 5f);
            var p0 = new Vector2(17.75f, 6.65f);
            var p1 = new Vector2(14.0f, 11.55f);
            var p2 = new Vector2(20.3f, 6.05f);
            Assert.IsTrue(LectureSeatmateStage.TrySolvePlan(p0, p1, p2, Rot(p0), Rot(p1), Rot(p2),
                out LectureSeatmateStage.Matrix2 map));

            var offset = new Vector2(0f, -2.26f);
            Vector2 got = map.Apply(offset) + Rot(p0);
            Vector2 want = Rot(p0 + offset);
            Assert.Less((got - want).magnitude, 1e-3f);

            Assert.IsFalse(LectureSeatmateStage.TrySolvePlan(Vector2.zero, Vector2.one, Vector2.one * 2f,
                Vector2.zero, Vector2.one, Vector2.one * 2f, out _), "一直線の 3 点では解けない");
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
