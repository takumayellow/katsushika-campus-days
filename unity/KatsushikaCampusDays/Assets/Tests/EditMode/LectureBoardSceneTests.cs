using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟の板書と、板を調べる判定 (#389)。SceneBuilder が置いた Campus.unity を開き、
    /// q_sq_hello_world の talk 先の 6 枚の板が調べられ、板書が板の手前から読める向きにあるかを確かめる。
    /// </summary>
    public sealed class LectureBoardSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        private static readonly string[] Boards = { "203", "302", "402", "502", "602", "7F" };

        private Scene _scene;
        private readonly Dictionary<string, NPCTalker> _talkers = new Dictionary<string, NPCTalker>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (NPCTalker each in root.GetComponentsInChildren<NPCTalker>(true))
                {
                    if (each.name.StartsWith("Talker_board_"))
                    {
                        _talkers[each.name.Replace("Talker_board_", string.Empty)] = each;
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

        [TestCaseSource(nameof(Boards))]
        public void 板は調べられる(string key)
        {
            Assert.IsTrue(_talkers.TryGetValue(key, out NPCTalker talker), "Talker_board_" + key + " が無い");
            Assert.AreEqual("board_" + key, talker.NpcId);
            Assert.AreEqual(LayerMask.NameToLayer("Interactable"), talker.gameObject.layer);
            Assert.AreEqual(0f, talker.TurnSpeed, "板は向き直らない");

            var box = talker.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.isTrigger);
        }

        [TestCaseSource(nameof(Boards))]
        public void 板書は板の手前から読める(string key)
        {
            Assert.IsTrue(_talkers.TryGetValue(key, out NPCTalker talker), "Talker_board_" + key + " が無い");
            TextMeshPro text = talker.transform.Find("BoardText")?.GetComponent<TextMeshPro>();
            Assert.IsNotNull(text, key + " に板書が無い");
            Assert.IsFalse(string.IsNullOrEmpty(text.text));

            // TextMeshPro はローカルの −Z の側から読める。その向きが板の手前（Talker の +Z）と揃っていること。
            Vector3 readFrom = -text.transform.forward;
            Assert.Greater(Vector3.Dot(readFrom, talker.transform.forward), 0.99f);
            Assert.IsNotNull(text.font, "日本語フォントが付いていない");
        }

        [Test]
        public void 白板の自分の行はクエストを終えるまで隠れている()
        {
            Assert.IsTrue(_talkers.TryGetValue("7F", out NPCTalker talker), "Talker_board_7F が無い");
            var reveal = talker.GetComponent<QuestDoneReveal>();
            Assert.IsNotNull(reveal);
            Assert.AreEqual("q_sq_hello_world", reveal.QuestId);
            Assert.IsNotNull(reveal.Target);
            Assert.IsFalse(reveal.Target.activeSelf);
            Assert.AreEqual("WORLD", reveal.Target.GetComponent<TextMeshPro>().text);
        }
    }
}
