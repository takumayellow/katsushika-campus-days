using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// シーンに置いた屋内の部品の材質が、FBX の材質の枠と同じ数・同じ並びになっていることを守る (#115)。
    ///
    /// SceneBuilder は屋内の FBX を置いて unpack する（CampusStage.PlaceModel）。部品のメッシュは FBX の中を参照するので、
    /// FBX を書き出し直せばシーンにもついて来るが、MeshRenderer の材質の並びは置いたときのままシーンに残る。
    /// 材質の枠が増えたり並びが変わったりした FBX を入れてシーンを組み直さないと、足りない枠のサブメッシュは描かれず、
    /// ずれた枠には別の材質が当たる。
    /// </summary>
    public sealed class InteriorSceneMaterialsTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string ModelsFolder = "Assets/Models/Interiors";

        /// <summary>FBX の中の部品 1 つ。名前と材質の並び。</summary>
        private readonly struct Part
        {
            public readonly string Name;
            public readonly Material[] Materials;

            public Part(string name, Material[] materials)
            {
                Name = name;
                Materials = materials;
            }
        }

        [Test]
        public void 屋内の部品の材質がFBXの材質の枠と同じ並びになっている()
        {
            var placed = new HashSet<string>();
            var bad = new List<string>();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                CheckPlacedParts(scene, placed, bad);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            List<string> models = InteriorModels();
            Assert.IsNotEmpty(models, ModelsFolder + " に屋内の FBX が無い");
            foreach (string path in models)
            {
                if (!placed.Contains(path))
                {
                    bad.Add(path + ": シーンに置いていない");
                }
            }

            Assert.IsEmpty(bad, "材質が FBX と合わない屋内の部品がある（FBX を書き出し直した後に、シーンを組み直していない）:\n"
                + string.Join("\n", bad));
        }

        /// <summary>シーンの中で屋内の FBX のメッシュを描く部品を調べ、置いてある FBX を placed に、材質が FBX と合わない部品を bad に足す。</summary>
        private static void CheckPlacedParts(Scene scene, HashSet<string> placed, List<string> bad)
        {
            var expected = new Dictionary<Mesh, List<Part>>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = MeshOf(renderer);
                    string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;
                    if (!path.StartsWith(ModelsFolder + "/") || !path.EndsWith(".fbx"))
                    {
                        continue;
                    }

                    if (placed.Add(path))
                    {
                        CollectParts(path, expected);
                    }

                    string problem = MaterialProblem(renderer, mesh, expected);
                    if (problem != null)
                    {
                        bad.Add(PathOf(renderer.transform) + ": " + problem);
                    }
                }
            }
        }

        /// <summary>FBX の部品を、使うメッシュごとに集める。</summary>
        private static void CollectParts(string path, Dictionary<Mesh, List<Part>> expected)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                return;
            }

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(renderer);
                if (mesh == null)
                {
                    continue;
                }

                if (!expected.TryGetValue(mesh, out List<Part> parts))
                {
                    parts = new List<Part>();
                    expected.Add(mesh, parts);
                }

                parts.Add(new Part(renderer.name, renderer.sharedMaterials));
            }
        }

        /// <summary>
        /// 部品の材質が FBX と同じなら null を、違えばどこが違うかを返す。FBX の中で同じメッシュを使う部品が複数あれば、
        /// 同じ名前の部品と比べる（同じ名前の部品が無ければ、どれか 1 つと同じならよい）。
        /// </summary>
        private static string MaterialProblem(Renderer renderer, Mesh mesh, Dictionary<Mesh, List<Part>> expected)
        {
            if (!expected.TryGetValue(mesh, out List<Part> parts))
            {
                return "FBX の中に、このメッシュを使う部品が無い";
            }

            List<Part> named = parts.FindAll(part => part.Name == renderer.name);
            List<Part> candidates = named.Count > 0 ? named : parts;
            Material[] actual = renderer.sharedMaterials;
            foreach (Part candidate in candidates)
            {
                if (SameMaterials(candidate.Materials, actual))
                {
                    return null;
                }
            }

            return FirstDifference(candidates[0].Materials, actual);
        }

        /// <summary>描くメッシュ。MeshFilter の無い描画（粒子や線など）は null。</summary>
        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static List<string> InteriorModels()
        {
            var models = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx"))
                {
                    models.Add(path);
                }
            }

            return models;
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string FirstDifference(Material[] fbx, Material[] scene)
        {
            for (int i = 0; i < Mathf.Min(fbx.Length, scene.Length); i++)
            {
                if (fbx[i] != scene[i])
                {
                    return (i + 1) + " 番目の枠が " + NameOf(scene[i]) + "（FBX は " + NameOf(fbx[i]) + "）";
                }
            }

            return "材質が " + scene.Length + " 個（FBX は " + fbx.Length + " 個）";
        }

        private static string NameOf(Material material)
        {
            return material != null ? material.name : "なし";
        }

        private static string PathOf(Transform node)
        {
            string path = node.name;
            for (Transform parent = node.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }
    }
}
