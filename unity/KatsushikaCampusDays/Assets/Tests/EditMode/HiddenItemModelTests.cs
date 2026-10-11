using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 隠しアイテムが名前どおりの模型で置かれていること (#167)。以前は 20 個とも同じ八面体の宝石だった。
    /// 模型は HiddenItemModels（KCD.Editor）が Assets/Generated/Meshes/HiddenItems に作り、CollectibleStage が
    /// Campus.unity に置く。クエストの拾い物との見分けは、足元の光る輪と模型のまわりのきらめき（レア度の色）。
    /// </summary>
    public sealed class HiddenItemModelTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string GemMeshName = "Gem";

        private Scene _scene;
        private Dictionary<string, CollectableItem> _placed;

        private static CollectibleCatalog RealCatalog()
        {
            string path = Path.Combine(Application.dataPath, "Data", "Collectibles", "collectibles.json");
            return CollectibleCatalog.Parse(File.ReadAllText(path));
        }

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            _placed = new Dictionary<string, CollectableItem>();
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (CollectableItem item in root.GetComponentsInChildren<CollectableItem>(true))
                {
                    _placed[item.ItemId] = item;
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
        public void EveryHiddenItem_HasItsOwnModel()
        {
            Type models = EditorType("HiddenItemModels");
            MethodInfo has = models.GetMethod("Has", BindingFlags.Public | BindingFlags.Static);
            MethodInfo meshPath = models.GetMethod("MeshPath", BindingFlags.Public | BindingFlags.Static);

            var meshes = new HashSet<Mesh>();
            foreach (CatalogItem item in RealCatalog().Items)
            {
                if (!item.IsHidden)
                {
                    continue;
                }

                Assert.IsTrue((bool)has.Invoke(null, new object[] { item.Id }), item.Id + " の模型が HiddenItemModels に無い");
                string path = (string)meshPath.Invoke(null, new object[] { item.Id });
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Assert.IsNotNull(mesh, path + " が無い（SceneBuilder.BuildAll で作る）");
                Assert.AreNotEqual(GemMeshName, mesh.name, item.Id + " が宝石のまま");
                Assert.IsTrue(meshes.Add(mesh), item.Id + " が他の物と同じメッシュを使っている");
                Assert.AreSame(mesh, _placed[item.Id].GetComponent<MeshFilter>().sharedMesh,
                    item.Id + " がシーンで模型を使っていない（SceneBuilder.BuildAll で組み直す）");
            }

            Assert.AreEqual(20, meshes.Count);
        }

        [Test]
        public void EveryModel_IsAHiddenItemInTheCatalog()
        {
            var hidden = new HashSet<string>();
            foreach (CatalogItem item in RealCatalog().Items)
            {
                if (item.IsHidden)
                {
                    hidden.Add(item.Id);
                }
            }

            var ids = (IEnumerable<string>)EditorType("HiddenItemModels").GetProperty("Ids").GetValue(null);
            foreach (string id in ids)
            {
                Assert.IsTrue(hidden.Contains(id), id + " の模型があるのに collectibles.json の隠しアイテムに無い（id の綴りを確かめる）");
            }
        }

        [Test]
        public void Models_AreFindableSize()
        {
            Type models = EditorType("HiddenItemModels");
            float min = (float)models.GetField("MinSize").GetValue(null);
            float max = (float)models.GetField("MaxSize").GetValue(null);
            foreach (CollectableItem item in PlacedHiddenItems())
            {
                Mesh mesh = item.GetComponent<MeshFilter>().sharedMesh;
                Vector3 size = mesh.bounds.size;
                float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                Assert.That(longest, Is.InRange(min, max), item.ItemId + " の一番長い所 " + longest + " m");
                Assert.Less(mesh.bounds.center.magnitude, 0.01f, item.ItemId + " の真ん中が回る軸から外れている");
            }
        }

        [Test]
        public void Models_HaveOneMaterialPerSubmesh()
        {
            foreach (CollectableItem item in PlacedHiddenItems())
            {
                Mesh mesh = item.GetComponent<MeshFilter>().sharedMesh;
                Material[] materials = item.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.AreEqual(mesh.subMeshCount, materials.Length, item.ItemId + " のサブメッシュとマテリアルの数");
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Assert.Greater(mesh.GetSubMesh(i).indexCount, 0, item.ItemId + " のサブメッシュ " + i + " が空");
                    Assert.IsNotNull(materials[i], item.ItemId + " のマテリアル " + i + " が無い");
                }
            }
        }

        /// <summary>
        /// 法線が 0 や NaN の頂点があると、その物が真っ黒に写り、ブルームで周りが白く飛ぶ
        /// （両面張りの面が表と裏で同じ頂点を使うと、法線が打ち消し合って 0 になる）。
        /// </summary>
        [Test]
        public void Models_HaveUnitNormals()
        {
            foreach (CollectableItem item in PlacedHiddenItems())
            {
                foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] normals = filter.sharedMesh.normals;
                    Assert.AreEqual(filter.sharedMesh.vertexCount, normals.Length, filter.sharedMesh.name + " に法線が無い");
                    for (int i = 0; i < normals.Length; i++)
                    {
                        Assert.AreEqual(1f, normals[i].magnitude, 1e-3f,
                            item.ItemId + " の " + filter.sharedMesh.name + " の頂点 " + i + " の法線 " + normals[i]);
                    }
                }
            }
        }

        [Test]
        public void HiddenItems_HaveRarityRingOnTheFloorAndSparkle()
        {
            Type models = EditorType("HiddenItemModels");
            string ringName = (string)models.GetField("RingName").GetValue(null);
            string sparkleName = (string)models.GetField("SparkleName").GetValue(null);
            var rarities = new Dictionary<string, string>();
            foreach (CatalogItem item in RealCatalog().Items)
            {
                rarities[item.Id] = item.Rarity;
            }

            int count = 0;
            foreach (CollectableItem item in PlacedHiddenItems())
            {
                count++;
                string expected = "mark_" + rarities[item.ItemId];
                Transform ring = item.transform.Find(ringName);
                Transform sparkle = item.transform.Find(sparkleName);
                Assert.IsNotNull(ring, item.ItemId + " に足元の輪が無い");
                Assert.IsNotNull(sparkle, item.ItemId + " にきらめきが無い");
                Assert.AreSame(ring, item.GroundMark, item.ItemId + " の輪が GroundMark になっていない（揺れと一緒に浮く）");
                Assert.AreEqual(expected, ring.GetComponent<MeshRenderer>().sharedMaterial.name, item.ItemId + " の輪の色");
                Assert.AreEqual(expected, sparkle.GetComponent<MeshRenderer>().sharedMaterial.name,
                    item.ItemId + " のきらめきの色");

                // 輪は物の真下の床（浮かせた分だけ下）に寝ている。
                float drop = item.transform.position.y - ring.position.y;
                Assert.That(drop, Is.InRange(0.3f, 0.5f), item.ItemId + " の輪が床に無い（物から " + drop + " m 下）");
            }

            Assert.AreEqual(20, count, "Campus.unity の隠しアイテムの数");
        }

        [Test]
        public void QuestPickups_HaveNoRarityMarks()
        {
            var hidden = new HashSet<string>();
            foreach (CatalogItem item in RealCatalog().Items)
            {
                if (item.IsHidden)
                {
                    hidden.Add(item.Id);
                }
            }

            string ringName = (string)EditorType("HiddenItemModels").GetField("RingName").GetValue(null);
            int pickups = 0;
            foreach (CollectableItem item in _placed.Values)
            {
                if (hidden.Contains(item.ItemId))
                {
                    continue;
                }

                pickups++;
                Assert.IsNull(item.transform.Find(ringName), item.ItemId + " はクエストの拾い物なのに輪がある");
            }

            Assert.Greater(pickups, 0, ScenePath + " にクエストの拾い物（葉・牛乳）が無い");
        }

        [Test]
        public void GroundMark_StaysOnTheFloorWhileTheItemBobs()
        {
            var go = new GameObject("GroundMarkProbe");
            try
            {
                Transform ring = new GameObject("Ring").transform;
                ring.SetParent(go.transform, false);
                ring.localPosition = Vector3.down * 0.4f;
                CollectableItem item = go.AddComponent<CollectableItem>();
                item.GroundMark = ring;
                Invoke(item, "Awake");
                Vector3 floor = ring.position;

                go.transform.position += Vector3.up * 0.1f;
                Invoke(item, "LateUpdate");
                Assert.AreEqual(floor.y, ring.position.y, 1e-4f, "揺れた物と一緒に輪が浮いた");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PartMesh_FacesPointOutward()
        {
            Type type = EditorType("PartMesh");
            object parts = Activator.CreateInstance(type);
            var centers = new[] { Vector3.zero, new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f) };
            Vector3 torusCenter = Vector3.up * 2f;
            const float TorusMajor = 0.3f;
            type.GetMethod("Ellipsoid").Invoke(parts,
                new object[] { "a", new Vector3(0.2f, 0.3f, 0.2f), 12, 16, Matrix4x4.Translate(centers[0]) });
            type.GetMethod("Box").Invoke(parts,
                new object[] { "b", new Vector3(0.2f, 0.1f, 0.3f), Matrix4x4.Translate(centers[1]) });
            type.GetMethod("Cylinder").Invoke(parts, new object[] { "c", 0.1f, 0.2f, 16, Matrix4x4.Translate(centers[2]) });
            type.GetMethod("Torus").Invoke(parts,
                new object[] { "d", TorusMajor, 0.05f, 24, 8, Matrix4x4.Translate(torusCenter) });
            var mesh = (Mesh)type.GetMethod("Build").Invoke(parts, new object[] { "Probe", false });
            try
            {
                Vector3[] v = mesh.vertices;
                Assert.AreEqual(4, mesh.subMeshCount);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] tris = mesh.GetTriangles(s);
                    Assert.Greater(tris.Length, 0, "サブメッシュ " + s + " が空");
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 a = v[tris[t]];
                        Vector3 b = v[tris[t + 1]];
                        Vector3 c = v[tris[t + 2]];
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        Assert.Greater(normal.sqrMagnitude, 1e-14f, "サブメッシュ " + s + " に潰れた三角形がある");

                        Vector3 mid = (a + b + c) / 3f;
                        Vector3 inside;
                        if (s < centers.Length)
                        {
                            inside = centers[s];
                        }
                        else
                        {
                            // トーラスは、管の芯（輪の上で一番近い点）が内側。
                            Vector3 flat = Vector3.ProjectOnPlane(mid - torusCenter, Vector3.up);
                            inside = torusCenter + flat.normalized * TorusMajor;
                        }

                        Assert.Greater(Vector3.Dot(normal, mid - inside), 0f,
                            "サブメッシュ " + s + " の三角形 " + t / 3 + " が内を向いている");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, target.GetType().Name + "." + method + " が無い");
            info.Invoke(target, null);
        }

        private IEnumerable<CollectableItem> PlacedHiddenItems()
        {
            var placed = new List<CollectableItem>();
            foreach (CatalogItem item in RealCatalog().Items)
            {
                if (!item.IsHidden)
                {
                    continue;
                }

                Assert.IsTrue(_placed.TryGetValue(item.Id, out CollectableItem found),
                    item.Id + " が " + ScenePath + " に無い（SceneBuilder.BuildAll で組み直す）");
                placed.Add(found);
            }

            return placed;
        }

        private static Type EditorType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KCD.Editor")
                {
                    continue;
                }

                Type type = assembly.GetType("KCD.Editor." + name);
                Assert.IsNotNull(type, "KCD.Editor に " + name + " が無い（名前を変えたらこのテストも直す）");
                return type;
            }

            Assert.Fail("KCD.Editor アセンブリが読み込まれていない");
            return null;
        }
    }
}
