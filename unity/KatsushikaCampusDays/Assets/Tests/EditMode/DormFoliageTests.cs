using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>寮の植え込み・観葉植物の葉は当たり判定から外す（#30 と同じ「植え込みにはまって動けない」を防ぐ）。</summary>
    public sealed class DormFoliageTests
    {
        private const string DormFbx = "Assets/Models/Interiors/dorm.fbx";

        /// <summary>KCD.Editor の CampusStage.IsFoliageMaterial（テストのアセンブリからは直接見えない）。</summary>
        private static bool IsFoliage(Material material)
        {
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "KCD.Editor")
                {
                    type = assembly.GetType("KCD.Editor.CampusStage");
                }
            }

            Assert.IsNotNull(type, "KCD.Editor に CampusStage が無い");
            MethodInfo method = type.GetMethod("IsFoliageMaterial", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "CampusStage.IsFoliageMaterial が無い");
            return (bool)method.Invoke(null, new object[] { material });
        }

        [TestCase("plant_green")]
        [TestCase("plant_yellow")]
        [TestCase("leaf_dark")]
        public void FoliageMaterialNames_AreDroppedFromColliders(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            try
            {
                Assert.IsTrue(IsFoliage(material), name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void DormLeafMaterials_AllCountAsFoliage()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(DormFbx);
            Assert.IsNotNull(fbx, DormFbx);
            var bad = new List<string>();
            int leaves = 0;
            foreach (Renderer renderer in fbx.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    string lower = material == null ? "" : material.name.ToLowerInvariant();
                    if (!lower.StartsWith("plant_green") && !lower.StartsWith("plant_yellow") && !lower.StartsWith("leaf"))
                    {
                        continue;
                    }

                    leaves++;
                    if (!IsFoliage(material))
                    {
                        bad.Add(renderer.name + ": " + material.name);
                    }
                }
            }

            Assert.Greater(leaves, 0, "寮に葉の材質が見つからない");
            Assert.IsEmpty(bad, "当たり判定に残る葉: " + string.Join(", ", bad));
        }
    }
}
