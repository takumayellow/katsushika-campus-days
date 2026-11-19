using System;
using System.Collections.Generic;
using KCD.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// KCD/Toon のマテリアルで、画に出ないパスを切る仕組み（<see cref="ToonPasses"/>）を守る (#15)。
    ///
    /// 輪郭のパス（SRPDefaultUnlit）は幅 0 だと線にならず、描画コールだけが増える。木（幹と葉）と、
    /// 幅 0 の顔の細部（眉・目・まつ毛・二重線）は輪郭を切る。顔の細部は顔の表面すれすれの薄い板なので、
    /// 影のパス（ShadowCaster）も切る。葉は、輪郭の殻が塊の内側を塞いでいた代わりに両面で描く。
    /// 前半は規則そのもの（メモリ上のマテリアル）、後半は保存してある .mat が規則どおりかを確かめる。
    /// 切りすぎ（部品名で終わらない「madonna_boots_brown」の影まで切る）も確かめる。
    /// </summary>
    public sealed class ToonPassTrimTests
    {
        private const string ToonShader = "KCD/Toon";
        private const string CampusFolder = "Assets/Materials/Campus";
        private static readonly string[] CharacterFolders = { "Assets/Materials/Characters", "Assets/Materials/Mobs" };

        private readonly List<Material> _created = new List<Material>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (Material material in _created)
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            _created.Clear();
        }

        private Material Toon(float outlineWidth)
        {
            Shader shader = Shader.Find(ToonShader);
            Assert.IsNotNull(shader, ToonShader + " が見つからない");
            var material = new Material(shader);
            material.SetFloat("_OutlineWidth", outlineWidth);
            _created.Add(material);
            return material;
        }

        private static bool Outline(Material material) => material.GetShaderPassEnabled(ToonPasses.OutlinePass);

        private static bool Shadow(Material material) => material.GetShaderPassEnabled(ToonPasses.ShadowPass);

        [TestCase("mirai_brow", true)]
        [TestCase("mirai_eye_l", true)]
        [TestCase("mirai_eye_r", true)]
        [TestCase("mirai_eye_rim", true)]
        [TestCase("mirai_eye_white", true)]
        [TestCase("student_m_lash", true)]
        [TestCase("mob_a_brow", true)]
        [TestCase("mob_a_brow_lod1", true)]
        [TestCase("prof_eye_white_lod1", true)]
        [TestCase("brow", true)]
        [TestCase("madonna_boots_brown", false)]
        [TestCase("mirai_face", false)]
        [TestCase("mirai_eyelash_band", false)]
        [TestCase("mirai_hair_lod1", false)]
        [TestCase("mirai_outline", false)]
        public void 顔の細部は名前の末尾の部品名で見分ける(string name, bool expected)
        {
            Assert.AreEqual(expected, ToonPasses.IsFacePart(name), name);
        }

        [Test]
        public void 幅0の葉は輪郭を切って両面で描き影は残す()
        {
            Material leaf = Toon(0f);
            leaf.SetFloat("_Cull", 2f);

            Assert.IsTrue(ToonPasses.Apply(leaf, "leaf"));
            Assert.IsFalse(Outline(leaf), "葉の輪郭のパスが残っている");
            Assert.IsTrue(Shadow(leaf), "葉の影まで切った");
            Assert.AreEqual(0f, leaf.GetFloat("_Cull"), "葉が両面で描かれない（塊の内側から空が透ける）");
            Assert.IsFalse(ToonPasses.Apply(leaf, "leaf"), "2 回目も変えたことになっている");
        }

        [Test]
        public void 幅0の幹は輪郭だけを切り片面のまま()
        {
            Material trunk = Toon(0f);
            trunk.SetFloat("_Cull", 2f);

            Assert.IsTrue(ToonPasses.Apply(trunk, "trunk"));
            Assert.IsFalse(Outline(trunk));
            Assert.IsTrue(Shadow(trunk));
            Assert.AreEqual(2f, trunk.GetFloat("_Cull"), "幹まで両面にした");
        }

        [Test]
        public void 幅0の顔の細部は輪郭も影も切る()
        {
            Material brow = Toon(0f);

            Assert.IsTrue(ToonPasses.Apply(brow, "mirai_brow"));
            Assert.IsFalse(Outline(brow));
            Assert.IsFalse(Shadow(brow));
        }

        [Test]
        public void 輪郭の幅がある顔の細部は輪郭を残して影だけ切る()
        {
            Material lash = Toon(0.005f);

            Assert.IsTrue(ToonPasses.Apply(lash, "madonna_lash"));
            Assert.IsTrue(Outline(lash), "幅のある輪郭まで切った（見た目が変わる）");
            Assert.IsFalse(Shadow(lash));
        }

        [TestCase("madonna_boots_brown", 0f)]
        [TestCase("mirai_hair", 0.005f)]
        [TestCase("mirai_outline", 0f)]
        public void 木でも顔の細部でもない材質には触らない(string name, float width)
        {
            Material material = Toon(width);
            float cull = material.GetFloat("_Cull");

            Assert.IsFalse(ToonPasses.Apply(material, name));
            Assert.IsTrue(Outline(material), name + " の輪郭を切った");
            Assert.IsTrue(Shadow(material), name + " の影を切った");
            Assert.AreEqual(cull, material.GetFloat("_Cull"));
        }

        [Test]
        public void トゥーンでない材質には触らない()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(lit);
            var material = new Material(lit);
            _created.Add(material);

            Assert.IsFalse(ToonPasses.Apply(material, "leaf"));
            Assert.IsTrue(material.GetShaderPassEnabled(ToonPasses.ShadowPass));
        }

        private static List<Material> ToonMaterials(params string[] folders)
        {
            var materials = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", folders))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.shader != null && material.shader.name == ToonShader)
                {
                    materials.Add(material);
                }
            }

            return materials;
        }

        [Test]
        public void 保存してある木の材質は輪郭を描かず葉は両面で描く()
        {
            var bad = new List<string>();
            int trees = 0;
            foreach (Material material in ToonMaterials(CampusFolder))
            {
                if (!ToonPasses.IsTree(material.name))
                {
                    continue;
                }

                trees++;
                if (Outline(material))
                {
                    bad.Add(material.name + ": 輪郭のパスが有効（幅 " + material.GetFloat("_OutlineWidth") + "）");
                }

                if (!Shadow(material))
                {
                    bad.Add(material.name + ": 影のパスが切ってある");
                }

                if (material.name.StartsWith("leaf", StringComparison.Ordinal) && material.GetFloat("_Cull") != 0f)
                {
                    bad.Add(material.name + ": 葉が両面で描かれない");
                }
            }

            Assert.GreaterOrEqual(trees, 2, CampusFolder + " に幹と葉の .mat が無い");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 保存してある顔の細部は影を落とさず幅0なら輪郭も描かない()
        {
            var bad = new List<string>();
            int parts = 0;
            foreach (Material material in ToonMaterials(CharacterFolders))
            {
                if (!ToonPasses.IsFacePart(material.name))
                {
                    continue;
                }

                parts++;
                bool far = material.name.EndsWith(ToonPasses.FarSuffix, StringComparison.Ordinal);
                bool outline = material.GetFloat("_OutlineWidth") > 0f && !far;
                if (Outline(material) != outline)
                {
                    bad.Add(material.name + ": 輪郭のパスが " + (outline ? "切ってある" : "有効")
                        + "（幅 " + material.GetFloat("_OutlineWidth") + "）");
                }

                if (Shadow(material))
                {
                    bad.Add(material.name + ": 影のパスが有効");
                }
            }

            Assert.GreaterOrEqual(parts, ToonPasses.FaceSlots.Length, "顔の細部の .mat が見つからない");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void 保存してある顔の細部でないキャラとモブの材質は影を落とす()
        {
            var bad = new List<string>();
            foreach (Material material in ToonMaterials(CharacterFolders))
            {
                if (!ToonPasses.IsFacePart(material.name) && !Shadow(material))
                {
                    bad.Add(material.name + ": 顔の細部でないのに影のパスが切ってある");
                }
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
