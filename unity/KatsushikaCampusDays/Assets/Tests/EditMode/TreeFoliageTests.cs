using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Tests
{
    /// <summary>
    /// 木の原型が「幹と葉の 2 マテリアル + 葉の明暗は頂点カラー」で描かれることを守る (#51)。
    ///
    /// 以前の木は葉を leaf_dark〜leaf_top の 4 マテリアルに分け、帯ごとに 1 色で塗っていたので、
    /// 樹冠が横縞の団子に見え、色も 4 段しかなかった。今は blender/kcd_lib/trees.py が面ごとの明るさ
    /// （空の見え方と樹冠の中の高さ）を 4 色の間で補間して頂点カラーに焼き、KCD/Toon の _VERTEXCOLOR_ON が
    /// 白い基本色に掛ける。頂点カラーが FBX から落ちたり、マテリアルがキーワードを失ったりすると、
    /// 木がまっ白か一色の塊になる。
    /// </summary>
    public sealed class TreeFoliageTests
    {
        private const string TreesFbx = "Assets/Models/Campus/trees.fbx";
        private const string LeafMaterial = "Assets/Materials/Campus/leaf.mat";
        private const string VertexColorKeyword = "_VERTEXCOLOR_ON";
        private static readonly string[] Species = { "keyaki", "round", "pine" };
        private static readonly string[] LeafRamp = { "leaf_dark", "leaf", "leaf_light", "leaf_top" };

        /// <summary>
        /// 原型 1 本の頂点の上限。trees.fbx は weldVertices 0 なので、Unity の頂点の数は面の角の数と同じ。
        /// blender/kcd_lib/trees.py の MAX_CORNERS と同じ値。
        /// </summary>
        private const int MaxVertices = 600;

        /// <summary>葉の色が 4 色の範囲からはみ出してよい幅（sRGB の 0..255）。8 bit への丸めのぶん。</summary>
        private const int ColorTolerance = 3;

        /// <summary>
        /// 1 本の葉の明るさ（sRGB の輝度 0..255）の幅の下限。いま 83〜95 ある。
        /// これを割るほど狭いと、樹冠が一色に見える（#51 の「色が単調」に戻る）。
        /// </summary>
        private const float MinLumaSpread = 60f;

        private static Dictionary<string, (Mesh Mesh, Material[] Materials)> Prototypes()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(TreesFbx);
            Assert.IsNotNull(root, TreesFbx + " を読めない");

            var found = new Dictionary<string, (Mesh Mesh, Material[] Materials)>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("tree_mesh_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer, filter.name + " に MeshRenderer が無い");
                found[filter.name.Substring("tree_mesh_".Length)] = (filter.sharedMesh, renderer.sharedMaterials);
            }

            foreach (string species in Species)
            {
                Assert.IsTrue(found.ContainsKey(species), TreesFbx + " に tree_mesh_" + species + " が無い");
            }

            return found;
        }

        private static float Luma(Color32 c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        [Test]
        public void 木の原型は頂点600以下で幹と葉の2マテリアル()
        {
            foreach (KeyValuePair<string, (Mesh Mesh, Material[] Materials)> pair in Prototypes())
            {
                Mesh mesh = pair.Value.Mesh;
                Assert.IsNotNull(mesh, pair.Key + " のメッシュが無い");
                Assert.LessOrEqual(mesh.vertexCount, MaxVertices, pair.Key + " の頂点が多すぎる");

                var names = new List<string>();
                foreach (Material material in pair.Value.Materials)
                {
                    names.Add(material != null ? material.name : "(なし)");
                }

                CollectionAssert.AreEquivalent(
                    new[] { "trunk", "leaf" }, names, pair.Key + " のマテリアルが幹と葉の 2 つでない");
                Assert.AreEqual(2, mesh.subMeshCount, pair.Key + " のサブメッシュが 2 つでない");
            }
        }

        [Test]
        public void 葉は頂点カラーで明暗を持ち色は葉の4色の範囲に収まる()
        {
            Dictionary<string, Vector3Int> palette = PaletteAgreementTests.UnityPalette();
            var lo = new Vector3Int(255, 255, 255);
            var hi = new Vector3Int(0, 0, 0);
            foreach (string name in LeafRamp)
            {
                Assert.IsTrue(palette.ContainsKey(name), name + " が CampusColors に無い");
                lo = Vector3Int.Min(lo, palette[name]);
                hi = Vector3Int.Max(hi, palette[name]);
            }

            foreach (KeyValuePair<string, (Mesh Mesh, Material[] Materials)> pair in Prototypes())
            {
                Mesh mesh = pair.Value.Mesh;
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.Color), pair.Key + " に頂点カラーが無い");

                int leaf = System.Array.FindIndex(pair.Value.Materials, m => m != null && m.name == "leaf");
                Assert.GreaterOrEqual(leaf, 0, pair.Key + " に葉のマテリアルが無い");

                Color32[] colors = mesh.colors32;
                float darkest = float.MaxValue;
                float brightest = float.MinValue;
                var outside = new List<string>();
                foreach (int index in mesh.GetIndices(leaf))
                {
                    Color32 c = colors[index];
                    if (c.r < lo.x - ColorTolerance || c.r > hi.x + ColorTolerance
                        || c.g < lo.y - ColorTolerance || c.g > hi.y + ColorTolerance
                        || c.b < lo.z - ColorTolerance || c.b > hi.z + ColorTolerance)
                    {
                        outside.Add(string.Format("#{0:X2}{1:X2}{2:X2}", c.r, c.g, c.b));
                    }

                    darkest = Mathf.Min(darkest, Luma(c));
                    brightest = Mathf.Max(brightest, Luma(c));
                }

                Assert.IsEmpty(outside, pair.Key + " の葉に 4 色の範囲の外の色がある: " + string.Join(", ", outside));
                Assert.GreaterOrEqual(
                    brightest - darkest, MinLumaSpread,
                    string.Format("{0} の葉の明暗が狭い（輝度 {1:0}〜{2:0}）。樹冠が一色に見える", pair.Key, darkest, brightest));
            }
        }

        [Test]
        public void 葉のマテリアルは頂点カラーを白の基本色に掛ける()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LeafMaterial);
            Assert.IsNotNull(material, LeafMaterial + " が無い");
            Assert.AreEqual("KCD/Toon", material.shader.name, "葉のシェーダが KCD/Toon でない");
            Assert.IsTrue(
                material.shader.keywordSpace.FindKeyword(VertexColorKeyword).isValid,
                "KCD/Toon が " + VertexColorKeyword + " を宣言していない");
            Assert.IsTrue(material.IsKeywordEnabled(VertexColorKeyword), "葉のマテリアルで頂点カラーが切れている");

            // 基本色が白でないと、頂点カラーにもう一度色が掛かって葉が沈む。
            Color baseColor = material.GetColor("_BaseColor");
            Assert.AreEqual(1f, baseColor.r, 0.01f, "葉の基本色が白でない");
            Assert.AreEqual(1f, baseColor.g, 0.01f, "葉の基本色が白でない");
            Assert.AreEqual(1f, baseColor.b, 0.01f, "葉の基本色が白でない");
        }
    }
}
