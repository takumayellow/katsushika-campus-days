using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// Campus シーンの NPC が NavMesh に乗れることを守る (#63)。
    ///
    /// NavMeshAgent がシーンで有効だと、読み込みのとき NavMeshSurface が NavMesh を登録するより先に agent の
    /// OnEnable が走り、「Failed to create agent because there is no valid NavMesh」が NPC の数だけ出ていた。
    /// agent はシーンでは無効にしておき、NPCWander.Start が足もとの NavMesh を確かめてから有効にする。
    /// </summary>
    public sealed class CampusNpcNavMeshTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        private Scene _scene;
        private readonly List<NPCWander> _npcs = new List<NPCWander>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                _npcs.AddRange(go.GetComponentsInChildren<NPCWander>(true));
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
        public void 巡回する_NPC_がいる()
        {
            Assert.Greater(_npcs.Count, 0, ScenePath + " に NPCWander が無い");
        }

        [Test]
        public void NavMeshAgent_はシーンでは無効にしてある()
        {
            var bad = new List<string>();
            foreach (NPCWander npc in _npcs)
            {
                NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
                if (agent == null)
                {
                    bad.Add(npc.name + ": NavMeshAgent が無い");
                }
                else if (agent.enabled)
                {
                    bad.Add(npc.name + ": NavMeshAgent が有効（読み込みで NavMesh より先に作られて失敗する）");
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void NPC_の足もとに_NavMesh_がある()
        {
            var bad = new List<string>();
            foreach (NPCWander npc in _npcs)
            {
                Vector3 p = npc.transform.position;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, NPCWander.NavMeshSnapDistance, NavMesh.AllAreas))
                {
                    bad.Add(string.Format("{0}: {1} から {2} m 以内に NavMesh が無い", npc.name, p, NPCWander.NavMeshSnapDistance));
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
