using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 306 の TA と、最前列の PC を調べる判定 (#392)。SceneBuilder が置いた Campus.unity を開き、
    /// q_sq_first_program の talk 先と flag の出どころが、306 の白板の前で話しかけられる所にあるかを確かめる。
    /// </summary>
    public sealed class LectureTaSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>話しかける場所の目安。TA と PC は白板からこの距離の中、互いにはこれより離す（判定を取り合わない）。</summary>
        private const float NearBoard = 4f;

        private const float Apart = 2f;

        private Scene _scene;
        private NPCTalker _ta;
        private NPCTalker _pc;
        private Transform _board;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (NPCTalker each in root.GetComponentsInChildren<NPCTalker>(true))
                {
                    if (each.NpcId == "ta_306")
                    {
                        _ta = each;
                    }
                    else if (each.NpcId == "pc_306")
                    {
                        _pc = each;
                    }
                }

                foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
                {
                    if (each.name == "board_lecture_306")
                    {
                        _board = each;
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
        public void TAは306で話しかけられ_動かない()
        {
            Assert.IsNotNull(_ta, "ta_306 の NPC が無い");
            Assert.AreEqual("NPC_ta_306", _ta.name);
            Assert.AreEqual("TA", _ta.DisplayName);
            Assert.IsNull(_ta.GetComponent<NPCWander>(), "屋内に NavMesh は無いので歩かせない");
            Assert.IsNull(_ta.GetComponent<UnityEngine.AI.NavMeshAgent>());
            Assert.IsNotNull(_board, "board_lecture_306 が無い");
            Assert.Less(Flat(_ta.transform.position - _board.position), NearBoard);
        }

        [Test]
        public void PCは調べられ_TAと離れている()
        {
            Assert.IsNotNull(_pc, "pc_306 の判定が無い");
            Assert.AreEqual(LayerMask.NameToLayer("Interactable"), _pc.gameObject.layer);
            Assert.AreEqual(0f, _pc.TurnSpeed, "PC は向き直らない");
            Assert.AreEqual("ui.interact.inspect", _pc.PromptKey);

            var box = _pc.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.isTrigger);

            Assert.IsNotNull(_board, "board_lecture_306 が無い");
            Assert.Less(Flat(_pc.transform.position - _board.position), NearBoard);
            Assert.IsNotNull(_ta, "ta_306 の NPC が無い");
            Assert.Greater(Flat(_pc.transform.position - _ta.transform.position), Apart);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
