using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 髪の天使の輪（ツヤの帯）が、Blender から Unity の材質まで通っていることを守る (#47)。
    ///
    /// Blender（kcd_chara/hair.py の ring_coords）が、髪の頂点の高さと向きを UV の 2 枚目に書く
    /// （x = 高さ。髪の頂上が 0、頭の中心の高さが 0.5。y = 正面からの方位角の絶対値 / pi）。髪でない頂点は (1, 1)。
    /// KCD/Toon の _HAIRRING_ON がその座標から画素ごとに帯を塗る。MaterialLibrary は髪の材質だけに付ける。
    /// このアセンブリは KCD.Editor を参照していないので、MaterialLibrary はリフレクションで呼ぶ。
    /// </summary>
    public sealed class CharacterHairRingTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

        private const string Keyword = "_HAIRRING_ON";

        private static string HairMaterial => (string)LibraryField("HairRingMaterial");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials", "Characters");

        private static Type MaterialLibraryType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KCD.Editor")
                {
                    continue;
                }

                Type type = assembly.GetType("KCD.Editor.MaterialLibrary");
                Assert.IsNotNull(type, "KCD.Editor に MaterialLibrary が無い（名前を変えたらこのテストも直す）");
                return type;
            }

            Assert.Fail("KCD.Editor アセンブリが読み込まれていない");
            return null;
        }

        private static object LibraryField(string name)
        {
            FieldInfo field = MaterialLibraryType().GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "MaterialLibrary." + name + " が無い");
            return field.GetValue(null);
        }

        private static float ReadFloat(string text, string property, float missing)
        {
            Match m = Regex.Match(text, @"- " + property + @": (?<v>[-\d.eE]+)");
            return m.Success ? float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : missing;
        }

        private static bool HasKeyword(string text)
        {
            return Regex.IsMatch(text, @"^\s*- " + Keyword + @"\s*$", RegexOptions.Multiline);
        }

        [Test]
        public void 髪の材質だけに天使の輪が付いている()
        {
            string[] materials = Directory.GetFiles(MaterialsFolder, "*.mat");
            Assert.IsNotEmpty(materials, "キャラの材質が見つからない");
            int hairCount = 0;
            foreach (string path in materials)
            {
                string text = File.ReadAllText(path);
                string file = Path.GetFileNameWithoutExtension(path);
                if (file.EndsWith("_" + HairMaterial, System.StringComparison.Ordinal))
                {
                    hairCount++;
                    Assert.AreEqual(1f, ReadFloat(text, "_HairRing", 0f), file + " に天使の輪が付いていない");
                    Assert.IsTrue(HasKeyword(text), file + " の " + Keyword + " が有効になっていない");
                }
                else
                {
                    Assert.IsFalse(HasKeyword(text), file + " に天使の輪が付いている");
                }
            }

            Assert.Greater(hairCount, 0, "髪の材質が見つからない");
        }

        [Test]
        public void 天使の輪は髪の色を白へ寄せた色で付け直しても材質が変わらない()
        {
            MethodInfo apply = MaterialLibraryType().GetMethod("ApplyHairRing", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(apply, "MaterialLibrary.ApplyHairRing が無い");
            float lighten = (float)LibraryField("HairRingLighten");
            Shader toon = Shader.Find("KCD/Toon");
            Assert.IsNotNull(toon, "KCD/Toon シェーダが見つからない");
            var hairColor = new Color(0.2f, 0.1f, 0.05f, 1f);

            var hair = new Material(toon);
            var skin = new Material(toon);
            try
            {
                object[] args = { hair, HairMaterial, hairColor };
                Assert.IsTrue((bool)apply.Invoke(null, args), "髪の材質に天使の輪が付かない");
                Assert.AreEqual(1f, hair.GetFloat("_HairRing"));
                Assert.IsTrue(hair.IsKeywordEnabled(Keyword), Keyword + " が有効にならない");
                Color ring = hair.GetColor("_HairRingColor");
                Color expected = Color.Lerp(hairColor, Color.white, lighten);
                Assert.AreEqual(expected.r, ring.r, 1e-4f, "輪の色 (r)");
                Assert.AreEqual(expected.g, ring.g, 1e-4f, "輪の色 (g)");
                Assert.AreEqual(expected.b, ring.b, 1e-4f, "輪の色 (b)");
                Assert.IsFalse((bool)apply.Invoke(null, args), "付け直すたびに髪の材質が変わる");

                Assert.IsFalse((bool)apply.Invoke(null, new object[] { skin, "skin", hairColor }), "髪でない材質に天使の輪が付く");
                Assert.AreEqual(0f, skin.GetFloat("_HairRing"));
                Assert.IsFalse(skin.IsKeywordEnabled(Keyword), "髪でない材質の " + Keyword + " が有効になる");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hair);
                UnityEngine.Object.DestroyImmediate(skin);
            }
        }

        [Test]
        public void 髪の頂点に天使の輪の座標が入っている()
        {
            var bad = new List<string>();
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx"))
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path + " を読めない");
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int sub = HairSubmesh(renderer.sharedMaterials);
                    if (sub < 0)
                    {
                        continue;
                    }

                    checkedCount++;
                    string problem = Check(renderer.sharedMesh, sub);
                    if (problem != null)
                    {
                        bad.Add(model.name + "/" + renderer.name + ": " + problem);
                    }
                }
            }

            Assert.Greater(checkedCount, 0, "髪のあるキャラの FBX が見つからない");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static int HairSubmesh(Material[] materials)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                string name = materials[i] != null ? materials[i].name : "";
                if (name == HairMaterial || name.EndsWith("_" + HairMaterial, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Check(Mesh mesh, int hairSubmesh)
        {
            var ring = new List<Vector2>();
            mesh.GetUVs(1, ring);
            if (ring.Count != mesh.vertexCount)
            {
                return "UV の 2 枚目が無い";
            }

            var hair = new HashSet<int>(mesh.GetTriangles(hairSubmesh));
            int front = 0;
            int back = 0;
            foreach (int i in hair)
            {
                Vector2 c = ring[i];
                if (c.x < 0f || c.x > 1f || c.y < 0f || c.y > 1f)
                {
                    return string.Format("髪の頂点 {0} の座標が {1}", i, c);
                }

                // 天使の輪を置く高さ（x = 0.1〜0.25。KCD_Toon の帯 0.13〜0.165 と歯の深さ 0.04 を包む）の髪が、前にも後ろにもある
                bool band = c.x > 0.1f && c.x < 0.25f;
                front += band && c.y < 0.2f ? 1 : 0;
                back += band && c.y > 0.8f ? 1 : 0;
            }

            if (front == 0 || back == 0)
            {
                return string.Format("天使の輪を置く高さの髪の頂点が 前 {0} / 後ろ {1}", front, back);
            }

            // 髪でない頂点は (1, 1)。1 枚目の UV（顔のタイル）と取り違えていないことも、ここで分かる
            int others = Enumerable.Range(0, mesh.vertexCount).Count(i => !hair.Contains(i) && ring[i] != Vector2.one);
            return others == 0 ? null : string.Format("髪でない頂点 {0} 個の座標が (1, 1) でない", others);
        }
    }
}
