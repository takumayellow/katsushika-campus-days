using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// モールの花壇の花が、ゲームのパレットにある色で campus.fbx に載っていることを守る (#58)。
    ///
    /// 花の色は blender/kcd_lib/site.py の FLOWERS（と花の中心の FLOWER_EYES）が決め、
    /// Unity の色は MaterialLibrary.CampusColors が決める。Blender に色を足して CampusColors に足し忘れると、
    /// EnsureCampus は灰色（#B0B0AC）のマテリアルを作るので、花がゲームでだけ灰色になる。
    /// </summary>
    public sealed class FlowerBedTests
    {
        private const string CampusFbx = "Assets/Models/Campus/campus.fbx";
        private const string BedsObject = "site_props_beds";

        /// <summary>
        /// 花壇 18 基の三角形の上限。平たい 5 弁の花（1 輪 11 三角形）を 1 株 12 輪にして、
        /// 25,866 から 77,481 に増えた (#58)。花や株を増やすときは、ここを上げる前に見た目の得を確かめる。
        /// </summary>
        private const int MaxBedTriangles = 80000;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>site.py の FLOWERS と FLOWER_EYES に出てくる色の名前（重複なし, FLOWERS が先）。</summary>
        private static List<string> SiteFlowers(bool withEyes)
        {
            string path = Path.Combine(RepoRoot, "blender", "kcd_lib", "site.py");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            string source = File.ReadAllText(path);

            var names = new List<string>();
            void Collect(string pattern)
            {
                Match block = Regex.Match(source, pattern, RegexOptions.Multiline);
                Assert.IsTrue(block.Success, "site.py の " + pattern + " を読めない");
                foreach (Match m in Regex.Matches(block.Groups["body"].Value, @"""(?<n>flower_[a-z_]+)"""))
                {
                    if (!names.Contains(m.Groups["n"].Value))
                    {
                        names.Add(m.Groups["n"].Value);
                    }
                }
            }

            Collect(@"^FLOWERS\s*=\s*\((?<body>[^)]*)\)");
            if (withEyes)
            {
                Collect(@"^FLOWER_EYES\s*=\s*\{(?<body>[^}]*)\}");
            }

            Assert.Greater(names.Count, 0, "site.py の花の色を読めていない");
            return names;
        }

        [Test]
        public void 花壇の花の色はすべてゲームのパレットにある()
        {
            Dictionary<string, Vector3Int> palette = PaletteAgreementTests.UnityPalette();
            var missing = new List<string>();
            foreach (string name in SiteFlowers(true))
            {
                if (!palette.ContainsKey(name))
                {
                    missing.Add(name);
                }
            }

            Assert.IsEmpty(missing, "CampusColors に無い花の色（ゲームで灰色になる）: " + string.Join(", ", missing));
        }

        [Test]
        public void 花壇は花の色をすべて載せて三角形の予算に収まる()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(CampusFbx);
            Assert.IsNotNull(root, CampusFbx + " を読めない");

            MeshFilter filter = null;
            foreach (MeshFilter candidate in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (candidate.name == BedsObject)
                {
                    filter = candidate;
                }
            }

            Assert.IsNotNull(filter, CampusFbx + " に " + BedsObject + " が無い");
            Mesh mesh = filter.sharedMesh;
            Assert.IsNotNull(mesh, BedsObject + " のメッシュが無い");

            long triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                triangles += (long)mesh.GetIndexCount(i) / 3;
            }

            Assert.LessOrEqual(triangles, MaxBedTriangles, "花壇の三角形が予算を超えた");

            var materials = new List<string>();
            foreach (Material material in filter.GetComponent<MeshRenderer>().sharedMaterials)
            {
                if (material != null)
                {
                    materials.Add(material.name);
                }
            }

            var missing = new List<string>();
            foreach (string name in SiteFlowers(false))
            {
                if (!materials.Contains(name))
                {
                    missing.Add(name);
                }
            }

            Assert.IsEmpty(missing, "campus.fbx の花壇に無い花の色（site.py を変えたあと作り直していない）: " + string.Join(", ", missing));
        }
    }
}
