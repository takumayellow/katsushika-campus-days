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

        /// <summary>学生は客席の POI からこの範囲（m、水平）。隣の席で、通路の POI に重ならない。</summary>
        private const float SeatmateMin = 0.6f;

        private const float SeatmateMax = 1.2f;

        /// <summary>黒板は教卓からこの距離の中（m、水平）。</summary>
        private const float NearStage = 5f;

        private Scene _scene;
        private NPCTalker _seatmate;
        private NPCTalker _board;
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
                    if (each.name == LectureSeatmateStage.StageEmpty)
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
            Assert.IsNotNull(_seats, LectureSeatmateStage.SeatsEmpty + " が無い");

            // 客席の POI（プレイヤーが立つ通路）の隣、同じ列の高さに立ち、列に沿って POI の側を向く。
            Vector3 toSeats = _seats.position - _seatmate.transform.position;
            float apart = Flat(toSeats);
            Assert.Greater(apart, SeatmateMin, "客席の POI に近すぎる（通路に重なる）");
            Assert.Less(apart, SeatmateMax, "隣の席にいない");
            Assert.Less(Mathf.Abs(toSeats.y), 0.15f, "客席の POI と別の列の高さにいる");
            toSeats.y = 0f;
            Assert.Greater(Vector3.Dot(_seatmate.transform.forward, toSeats.normalized), 0.9f,
                "客席の POI の側を向いていない");
        }

        [Test]
        public void 隣の席の学生の立つ所は_通路と前の机に重ならない()
        {
            // 101 の 5 列目（plan_lecture_hall.rows: 踏面 Y 11.05〜12.05）。西のブロックは X 8.7〜13.5、
            // その東の通路は X 13.5〜14.5、前の机は Y 11.07〜11.47。当たり判定の半径は 0.34 で、体の半径は 0.28。
            // 前後は 0.58 m しかなく当たり判定は入らないので、前後は体の幅で見る（学生は動かない）。
            const float radius = 0.34f;
            const float body = 0.28f;
            Vector2 at = LectureSeatmateStage.SeatmatePlan;
            Assert.LessOrEqual(at.x + radius, 13.5f, "通路に重なる");
            Assert.GreaterOrEqual(at.x - radius, 8.7f);
            Assert.GreaterOrEqual(at.y - body, 11.47f, "体が前の机に重なる");
            Assert.LessOrEqual(at.y + body, 12.05f, "体が後ろの列の段に重なる");

            // 平面と Unity が同じ向き（屋内の (x, z) = 平面の (x, y)）なら、POI からの差分のまま置き、+X を向く。
            var identity = new LectureSeatmateStage.Matrix2(1f, 0f, 0f, 1f);
            var seats = new Vector3(14.0f, -0.6f, 11.55f);
            Assert.IsTrue(LectureSeatmateStage.TrySeatmatePose(identity, seats, -0.6f, out Vector3 pos, out float yaw));
            Assert.Less((pos - new Vector3(at.x, -0.6f, at.y)).magnitude, 1e-4f);
            Assert.AreEqual(90f, yaw, 1e-3f);
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
