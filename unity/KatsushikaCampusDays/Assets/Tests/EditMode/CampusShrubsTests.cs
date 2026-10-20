using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// Campus シーンに shrubs.json の低木と草むらがすべて植わっていることを守る (#78)。
    ///
    /// shrubs.fbx は trees.fbx と同じ作りで、shrub_&lt;n&gt; は位置・向き・大きさだけの Empty でメッシュを持たない。
    /// CampusStage.DressShrubs が原点にある shrub_mesh_&lt;モデル&gt; のメッシュを各 Empty に載せる。
    /// 低木と草むらは歩いて抜けられる高さなので、当たり判定は付けない。
    /// </summary>
    public sealed class CampusShrubsTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string ShrubsFbx = "Assets/Models/Campus/shrubs.fbx";
        private const string ShrubsJson = "Assets/Models/Campus/shrubs.json";
        private const string LeafMaterial = "Assets/Materials/Campus/leaf.mat";

        /// <summary>Empty の位置と shrubs.json の位置のずれの許容 [m]。</summary>
        private const float PositionTolerance = 0.05f;

        /// <summary>立った低木と草むらの高さの範囲 [m]。低木は 0.5〜1.3 m、草むらは 0.3〜0.6 m に作ってある。</summary>
        private const float MinHeight = 0.2f;
        private const float MaxHeight = 2.0f;

        /// <summary>
        /// まとめたあとの描画コールの上限（パス 1 つあたり）。低木は 128 m のマス 8 個にまたがり、
        /// 材質は葉の 1 つだけなので、マス 1 つが 1 つのメッシュにまとまれば 8。影の違うものが混ざってマスが割れても足りる数。
        /// </summary>
        private const int MaxShrubDrawsPerPass = 16;

        private Scene _scene;
        private Transform _shrubs;
        private List<Dictionary<string, object>> _expected;

        [OneTimeSetUp]
        public void OpenCampus()
        {
            var root = MiniJson.Deserialize(File.ReadAllText(ShrubsJson)) as Dictionary<string, object>;
            Assert.IsNotNull(root, ShrubsJson + " を読めない");
            _expected = new List<Dictionary<string, object>>();
            foreach (object shrub in MiniJson.GetArray(root, "shrubs"))
            {
                _expected.Add((Dictionary<string, object>)shrub);
            }

            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject go in _scene.GetRootGameObjects())
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Shrubs")
                    {
                        _shrubs = t;
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
        public void shrubs_fbx_は実行時にまとめられるよう読み込める()
        {
            var importer = AssetImporter.GetAtPath(ShrubsFbx) as ModelImporter;
            Assert.IsNotNull(importer, ShrubsFbx + " が無い");
            // 読めないメッシュはビルドした版でまとめられず、1 つずつ描かれたままになる。
            Assert.IsTrue(importer.isReadable, ShrubsFbx + " の Read/Write が無効");
        }

        [Test]
        public void shrubs_json_の低木と草むらがすべて描かれる()
        {
            Assert.IsNotNull(_shrubs, ScenePath + " に Shrubs が無い");
            Assert.Greater(_expected.Count, 0, ShrubsJson + " に低木も草むらも無い");

            int drawn = 0;
            foreach (MeshRenderer renderer in _shrubs.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (renderer.enabled && renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                {
                    drawn++;
                }
            }

            Assert.AreEqual(_expected.Count, drawn, "描かれる低木と草むらの数が shrubs.json と合わない");
        }

        [Test]
        public void 原型は原点に描かれない()
        {
            Assert.IsNotNull(_shrubs, ScenePath + " に Shrubs が無い");
            foreach (Transform t in _shrubs.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("shrub_mesh_", System.StringComparison.Ordinal))
                {
                    Assert.IsFalse(t.gameObject.activeInHierarchy, t.name + " が表示されたまま（原点に低木が重なる）");
                }
            }
        }

        [Test]
        public void 低木と草むらは_shrubs_json_の位置に立つ()
        {
            Assert.IsNotNull(_shrubs, ScenePath + " に Shrubs が無い");
            var spots = new Dictionary<string, Transform>();
            foreach (Transform t in _shrubs.GetComponentsInChildren<Transform>(true))
            {
                spots[t.name] = t;
            }

            var bad = new List<string>();
            foreach (Dictionary<string, object> shrub in _expected)
            {
                string name = "shrub_" + MiniJson.GetInt(shrub, "i");
                string model = MiniJson.GetString(shrub, "model");
                // build_campus.py の (x, y) は、CampusStage.PlaceModel が Y 180 度で戻したあとのワールド (x, z)。
                var expected = new Vector3(MiniJson.GetFloat(shrub, "x"), 0f, MiniJson.GetFloat(shrub, "y"));

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

                if (!filter.sharedMesh.name.Contains(model))
                {
                    bad.Add(string.Format("{0}: モデルが {1} なのにメッシュが {2}", name, model, filter.sharedMesh.name));
                }

                // 立っているか: 高さが範囲に収まり、根元が地面、本体が Empty の真上付近（横倒しや原点へのずれを拾う）
                Bounds b = renderer.bounds;
                if (b.size.y < MinHeight || b.size.y > MaxHeight || Mathf.Abs(b.min.y) > 0.3f
                    || new Vector2(b.center.x - p.x, b.center.z - p.z).magnitude > 2f)
                {
                    bad.Add(string.Format("{0}: 立ち方がおかしい（bounds {1}）", name, b));
                }
            }

            Assert.IsEmpty(bad, bad.Count + " 個がおかしい:\n" + string.Join("\n", bad.GetRange(0, Mathf.Min(bad.Count, 20))));
        }

        [Test]
        public void 材質は共有の_leaf_で_当たり判定は付かない()
        {
            Assert.IsNotNull(_shrubs, ScenePath + " に Shrubs が無い");
            Assert.IsEmpty(_shrubs.GetComponentsInChildren<Collider>(true), "低木と草むらに当たり判定が付いている（歩いて抜けられない）");

            var bad = new List<string>();
            foreach (MeshRenderer renderer in _shrubs.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length != 1 || materials[0] == null || AssetDatabase.GetAssetPath(materials[0]) != LeafMaterial)
                {
                    bad.Add(renderer.transform.parent != null ? renderer.transform.parent.name : renderer.name);
                }
            }

            Assert.IsEmpty(bad, bad.Count + " 個の材質が " + LeafMaterial + " でない: "
                + string.Join(", ", bad.GetRange(0, Mathf.Min(bad.Count, 10))));
        }

        [Test]
        public void 再生の始めにマスごとにまとめられ_描画コールが少ない()
        {
            Assert.IsNotNull(_shrubs, ScenePath + " に Shrubs が無い");
            TreeChunkCombiner combiner = _shrubs.GetComponent<TreeChunkCombiner>();
            Assert.IsNotNull(combiner, "Shrubs に TreeChunkCombiner が付いていない");

            try
            {
                Assert.Greater(combiner.Combine(), 0, "まとめたメッシュが無い");

                int draws = 0;
                var stray = new List<string>();
                foreach (MeshRenderer renderer in _shrubs.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    draws += renderer.sharedMaterials.Length;
                    if (!renderer.name.StartsWith(TreeChunkCombiner.ChunkPrefix, System.StringComparison.Ordinal))
                    {
                        stray.Add(renderer.transform.parent != null ? renderer.transform.parent.name : renderer.name);
                    }
                }

                Assert.IsEmpty(stray, stray.Count + " 個がまとめられずに 1 つずつ描かれる: "
                    + string.Join(", ", stray.GetRange(0, Mathf.Min(stray.Count, 10))));
                Assert.LessOrEqual(draws, MaxShrubDrawsPerPass, "低木と草むらの描画コールが多すぎる（マス " + combiner.Chunks.Count + " 個）");
            }
            finally
            {
                foreach (GameObject chunk in combiner.Chunks)
                {
                    if (chunk != null)
                    {
                        Object.DestroyImmediate(chunk.GetComponent<MeshFilter>().sharedMesh);
                    }
                }
            }
        }
    }
}
