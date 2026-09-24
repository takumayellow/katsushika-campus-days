using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 面の写真テクスチャ（Assets/Textures/surfaces）の tiling.json が、画像・CampusColors・
    /// 出所の data/textures/surfaces.json と食い違っていないことを守る (#59 / #78)。
    ///
    /// CampusSurfaces は tiling.json に載った名前にだけ画像を貼り、載っていないマテリアルからは
    /// このフォルダの画像を外す。tiling.json は tools/fetch_textures.py が surfaces.json から書くので、
    /// どちらかを手で直して流し直さないと、面が黙って単色に戻るか、違う大きさで並ぶ。
    /// 画像が無い名前は単色で塗られ、警告がコンソールに流れるだけで気付きにくい。
    ///
    /// 画像の平均色は CampusColors の宣言 hex に合わせて焼く。CampusColors に無い名前は
    /// 狙いの色が無いので焼けず、MaterialLibrary.Repaint も触らない。
    ///
    /// EditMode のアセンブリは KCD.Runtime しか参照しないので、CampusSurfaces や
    /// MaterialLibrary は使わず、JSON は MiniJson で、CampusColors はソースを字面で読む。
    /// </summary>
    public sealed class SurfaceTexturesTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static string SurfacesFolder => Path.Combine(Application.dataPath, "Textures", "surfaces");

        private static Dictionary<string, object> ReadJson(string path)
        {
            Assert.IsTrue(File.Exists(path), path + " が無い");
            var root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, path + " を読めない");
            return root;
        }

        /// <summary>tiling.json を読む。{マテリアル名: tile_cm}。</summary>
        private static Dictionary<string, float> Tiling()
        {
            string path = Path.Combine(SurfacesFolder, "tiling.json");
            var tiling = new Dictionary<string, float>();
            foreach (object item in MiniJson.GetArray(ReadJson(path), "surfaces"))
            {
                var entry = item as Dictionary<string, object>;
                string name = MiniJson.GetString(entry, "material");
                Assert.IsNotEmpty(name, path + " に名前の無い行がある");
                Assert.IsFalse(tiling.ContainsKey(name), path + " に " + name + " が 2 回ある");
                tiling[name] = MiniJson.GetFloat(entry, "tile_cm");
            }

            Assert.IsNotEmpty(tiling, path + " の surfaces を読めていない");
            return tiling;
        }

        /// <summary>surfaces.json を読む。{マテリアル名: そのマテリアルを含む面の tile_cm}。</summary>
        private static Dictionary<string, float> Manifest()
        {
            string path = Path.Combine(RepoRoot, "data", "textures", "surfaces.json");
            var manifest = new Dictionary<string, float>();
            foreach (object item in MiniJson.GetArray(ReadJson(path), "surfaces"))
            {
                var surface = item as Dictionary<string, object>;
                float tileCm = MiniJson.GetFloat(surface, "tile_cm");
                foreach (string name in MiniJson.ToStringList(MiniJson.GetArray(surface, "materials")))
                {
                    manifest[name] = tileCm;
                }
            }

            Assert.IsNotEmpty(manifest, path + " の surfaces を読めていない");
            return manifest;
        }

        /// <summary>
        /// MaterialLibrary.CampusColors の名前を読む。Editor のアセンブリは EditMode から参照できないので、
        /// PaletteAgreementTests と同じくソースを字面で読む。
        /// </summary>
        private static HashSet<string> CampusColorNames()
        {
            string path = Path.Combine(
                Application.dataPath, "Scripts", "Editor", "MaterialLibrary.cs");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            string source = File.ReadAllText(path);
            int start = source.IndexOf("CampusColors", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "CampusColors が見つからない");
            int end = source.IndexOf("};", start, System.StringComparison.Ordinal);

            var names = new HashSet<string>();
            foreach (Match m in Regex.Matches(
                source.Substring(start, end - start), @"\{\s*""(?<n>[a-z0-9_]+)"",\s*""[0-9A-Fa-f]{6}""\s*\}"))
            {
                names.Add(m.Groups["n"].Value);
            }

            Assert.Greater(names.Count, 20, "CampusColors を読めていない");
            return names;
        }

        [Test]
        public void タイリングの名前には画像と取り込み設定がある()
        {
            var missing = new List<string>();
            foreach (string name in Tiling().Keys)
            {
                string jpg = Path.Combine(SurfacesFolder, name + ".jpg");
                if (!File.Exists(jpg))
                {
                    missing.Add(name + ".jpg");
                }
                else if (!File.Exists(jpg + ".meta"))
                {
                    missing.Add(name + ".jpg.meta");
                }
            }

            Assert.IsEmpty(
                missing,
                "tiling.json に載っているのに Assets/Textures/surfaces に無い。その面は単色で塗られる:\n"
                + string.Join("\n", missing));
        }

        [Test]
        public void タイリングの名前はキャンパスの色表にある()
        {
            HashSet<string> colors = CampusColorNames();
            var unknown = new List<string>();
            foreach (string name in Tiling().Keys)
            {
                if (!colors.Contains(name))
                {
                    unknown.Add(name);
                }
            }

            Assert.IsEmpty(
                unknown,
                "CampusColors に無い名前は狙いの平均色が無く、Repaint も触らない:\n"
                + string.Join("\n", unknown));
        }

        [Test]
        public void タイリングの寸法は出所の指定と同じ()
        {
            Dictionary<string, float> manifest = Manifest();
            var wrong = new List<string>();
            foreach (KeyValuePair<string, float> pair in Tiling())
            {
                if (pair.Value <= 0f)
                {
                    wrong.Add(pair.Key + ": tile_cm " + pair.Value + " は正でない");
                }
                else if (!manifest.TryGetValue(pair.Key, out float expected))
                {
                    wrong.Add(pair.Key + ": surfaces.json に無い");
                }
                else if (!Mathf.Approximately(pair.Value, expected))
                {
                    wrong.Add(pair.Key + ": tiling.json " + pair.Value + " cm / surfaces.json " + expected + " cm");
                }
            }

            Assert.IsEmpty(
                wrong,
                "tiling.json が surfaces.json と違う。tools/fetch_textures.py を流し直す:\n"
                + string.Join("\n", wrong));
        }

        [Test]
        public void 出所の全マテリアルがタイリングにある()
        {
            Dictionary<string, float> tiling = Tiling();
            var missing = new List<string>();
            foreach (string name in Manifest().Keys)
            {
                if (!tiling.ContainsKey(name))
                {
                    missing.Add(name);
                }
            }

            Assert.IsEmpty(
                missing,
                "surfaces.json にあるのに tiling.json に無い。CampusSurfaces がその面から画像を外す:\n"
                + string.Join("\n", missing));
        }
    }
}
