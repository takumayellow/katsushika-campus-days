using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// Campus シーンに trees.json の木がすべて植わっていることを守る (#57)。
    ///
    /// trees.fbx の tree_&lt;n&gt; は位置・向き・大きさだけの Empty で、メッシュを持たない。
    /// 以前は SceneBuilder がそのまま置いていたので、描かれていたのは原点に重なった原型 3 本だけで、
    /// キャンパスに木が 1 本も無かった。Blender のプレビューは原型の複製を並べて描くので、この差はプレビューでは見えない。
    /// </summary>
    public sealed class CampusTreesTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string TreesJson = "Assets/Models/Campus/trees.json";

        /// <summary>Empty の位置と trees.json の位置のずれの許容 [m]。</summary>
        private const float PositionTolerance = 0.05f;

        /// <summary>
        /// 幹の当たり判定の外側に空いていてほしい NavMesh の余白 [m]。NPC の NavMeshAgent の半径（0.32 m）。
        /// これより近いと、NPC の体が幹にめり込んで見える。
        /// </summary>
        private const float MinTrunkClearance = 0.32f;

        private Scene _scene;
        private Transform _trees;
        private List<Dictionary<string, object>> _expected;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            var root = MiniJson.Deserialize(File.ReadAllText(TreesJson)) as Dictionary<string, object>;
            Assert.IsNotNull(root, TreesJson + " を読めない");
            _expected = new List<Dictionary<string, object>>();
            foreach (object tree in MiniJson.GetArray(root, "trees"))
            {
                _expected.Add((Dictionary<string, object>)tree);
            }

            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Trees")
                    {
                        _trees = t;
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
        public void trees_json_の木がすべて描かれる()
        {
            Assert.IsNotNull(_trees, ScenePath + " に Trees が無い");
            Assert.Greater(_expected.Count, 0, TreesJson + " に木が無い");

            int drawn = 0;
            foreach (MeshRenderer renderer in _trees.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (renderer.enabled && renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                {
                    drawn++;
                }
            }

            Assert.AreEqual(_expected.Count, drawn, "描かれる木の数が trees.json と合わない");
        }

        [Test]
        public void 原型は原点に描かれない()
        {
            Assert.IsNotNull(_trees, ScenePath + " に Trees が無い");
            foreach (Transform t in _trees.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("tree_mesh_", System.StringComparison.Ordinal))
                {
                    Assert.IsFalse(t.gameObject.activeInHierarchy, t.name + " が表示されたまま（原点に木が重なる）");
                }
            }
        }

        [Test]
        public void 木は_trees_json_の位置に立ち_幹に当たり判定がある()
        {
            Assert.IsNotNull(_trees, ScenePath + " に Trees が無い");
            var spots = new Dictionary<string, Transform>();
            foreach (Transform t in _trees.GetComponentsInChildren<Transform>(true))
            {
                spots[t.name] = t;
            }

            var bad = new List<string>();
            foreach (Dictionary<string, object> tree in _expected)
            {
                string name = "tree_" + MiniJson.GetInt(tree, "i");
                string species = MiniJson.GetString(tree, "species");
                // build_campus.py の (x, y) は、CampusStage.PlaceModel が Y 180 度で戻したあとのワールド (x, z)。
                var expected = new Vector3(MiniJson.GetFloat(tree, "x"), 0f, MiniJson.GetFloat(tree, "y"));

                if (!spots.TryGetValue(name, out Transform spot))
                {
                    bad.Add(name + ": Empty が無い");
                    continue;
                }

                Vector3 p = spot.position;
                if (Mathf.Abs(p.x - expected.x) > PositionTolerance || Mathf.Abs(p.z - expected.z) > PositionTolerance
                    || Mathf.Abs(p.y) > 0.3f)
                {
                    bad.Add(string.Format("{0}: 位置 {1} / 期待 {2}", name, p, expected));
                    continue;
                }

                Transform body = spot.Find("body");
                MeshRenderer renderer = body != null ? body.GetComponent<MeshRenderer>() : null;
                MeshFilter filter = body != null ? body.GetComponent<MeshFilter>() : null;
                if (renderer == null || filter == null || filter.sharedMesh == null)
                {
                    bad.Add(name + ": 本体のメッシュが無い");
                    continue;
                }

                if (!filter.sharedMesh.name.Contains(species))
                {
                    bad.Add(string.Format("{0}: 種が {1} なのにメッシュが {2}", name, species, filter.sharedMesh.name));
                }

                // 立っているか: 高さ 4〜14 m、根元が地面、樹冠が幹の真上付近（横倒しや原点へのずれを拾う）
                Bounds b = renderer.bounds;
                if (b.size.y < 4f || b.size.y > 14f || Mathf.Abs(b.min.y) > 0.5f
                    || new Vector2(b.center.x - p.x, b.center.z - p.z).magnitude > 2f)
                {
                    bad.Add(string.Format("{0}: 立ち方がおかしい（bounds {1}）", name, b));
                }

                Transform trunk = spot.Find("trunk");
                CapsuleCollider capsule = trunk != null ? trunk.GetComponent<CapsuleCollider>() : null;
                if (capsule == null || Vector3.Dot(trunk.up, Vector3.up) < 0.99f || capsule.direction != 1
                    || capsule.bounds.max.y < 4f || capsule.bounds.max.y > 10f)
                {
                    bad.Add(name + ": 幹の当たり判定が無いか、上を向いていない");
                }
            }

            Assert.IsEmpty(bad, bad.Count + " 本がおかしい:\n" + string.Join("\n", bad.GetRange(0, Mathf.Min(bad.Count, 20))));
        }

        [Test]
        public void NPC_の_NavMesh_は幹の足もとを通らない()
        {
            Assert.IsNotNull(_trees, ScenePath + " に Trees が無い");

            int sampled = 0;
            var bad = new List<string>();
            foreach (Transform t in _trees.GetComponentsInChildren<Transform>(false))
            {
                if (t.name != "trunk")
                {
                    continue;
                }

                // 幹の真下に NavMesh があれば、最寄りの点は幹の中心そのものになる。
                // 抜けていれば、最寄りの点は除外の箱（幹の半径 + NPC の体の半径）の外になる。
                if (!NavMesh.SamplePosition(t.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    continue;
                }

                sampled++;
                float d = new Vector2(hit.position.x - t.position.x, hit.position.z - t.position.z).magnitude;
                float trunkRadius = t.GetComponent<CapsuleCollider>().radius * t.lossyScale.x;
                if (d < trunkRadius + MinTrunkClearance)
                {
                    bad.Add(string.Format("{0}: 幹の中心から {1:0.00} m に NavMesh（幹の半径 {2:0.00} m）",
                        t.parent.name, d, trunkRadius));
                }
            }

            Assert.Greater(sampled, _expected.Count / 2, "木の周りに NavMesh が見つからない（NavMesh が読めていない）");
            Assert.IsEmpty(bad, bad.Count + " 本の幹を NPC が通れる:\n" + string.Join("\n", bad.GetRange(0, Mathf.Min(bad.Count, 20))));
        }
    }
}
