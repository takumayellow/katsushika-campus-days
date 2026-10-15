using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 講義棟のエスカレーター 10 区間の上り・下り (#304)。上りの区間は階ごとに北と南の列で入れ替わり
    /// （1F→2F は北が上り。JSPE の「〜へ」「〜から」の表記）、もう一方の列が下り。
    /// SceneBuilder が置いた Campus.unity を開き、区間ごとの Escalator の向きを確かめる。
    /// </summary>
    public sealed class LectureEscalatorSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        private static readonly string[] DownFlights = { "south_1f", "north_2f", "south_3f", "north_4f", "south_5f" };
        private static readonly string[] UpFlights = { "north_1f", "south_2f", "north_3f", "south_4f", "north_5f" };

        private Scene _scene;
        private readonly Dictionary<string, Escalator> _flights = new Dictionary<string, Escalator>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Escalator each in root.GetComponentsInChildren<Escalator>(true))
                {
                    Transform interior = each.transform;
                    while (interior != null && QuestObjectiveLocator.InteriorIdFromName(interior.name) != "lecture")
                    {
                        interior = interior.parent;
                    }

                    if (interior != null)
                    {
                        _flights[each.name.Replace("Escalator_", string.Empty)] = each;
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
        public void 講義棟のエスカレーターは10区間ある()
        {
            Assert.AreEqual(DownFlights.Length + UpFlights.Length, _flights.Count,
                "講義棟のエスカレーター: " + string.Join(", ", _flights.Keys));
        }

        [TestCaseSource(nameof(DownFlights))]
        public void 下りの区間は上の端から下の端へ運ぶ(string flight)
        {
            Assert.IsTrue(_flights.ContainsKey(flight), "講義棟に Escalator_" + flight + " が無い");
            Assert.IsTrue(_flights[flight].Down, flight + " が上りになっている");
        }

        [TestCaseSource(nameof(UpFlights))]
        public void 上りの区間は下の端から上の端へ運ぶ(string flight)
        {
            Assert.IsTrue(_flights.ContainsKey(flight), "講義棟に Escalator_" + flight + " が無い");
            Assert.IsFalse(_flights[flight].Down, flight + " が下りになっている");
        }
    }
}
