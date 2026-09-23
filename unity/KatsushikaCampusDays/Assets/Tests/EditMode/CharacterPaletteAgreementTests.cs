using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの色が Blender（blender/kcd_chara/mats.py）とゲームで同じであることを守る (#55)。
    ///
    /// FBX が Unity へ渡すのはマテリアルの「名前」だけ。以前は Unity 側が名前の部分一致で
    /// 色を推測していて（MaterialLibrary.CharacterColor）、キャラの 31 色のうち 22 色が
    /// Blender と違い、まつ毛と眉は顔の線なのにベージュ #E8E4DC になっていた。
    /// 今は Blender が &lt;id&gt;/palette.json に色表を書き、MaterialLibrary.EnsureCharacter が
    /// それで .mat を塗る。ここでは palette.json と、実際に保存されている .mat の
    /// _BaseColor を突き合わせる（.mat を塗り直し忘れると落ちる）。
    ///
    /// 顔のテクスチャを貼るもの（face, eye_white, eye_l, eye_r）と輪郭線は palette.json に無い。
    ///
    /// 和柄（絣・ハート）の服は、palette.json の hex が柄の平均色で、ゲームでは柄の画像を貼って
    /// _BaseColor を白にする。こちらは画像の平均色と hex を突き合わせる。
    /// </summary>
    public sealed class CharacterPaletteAgreementTests
    {
        /// <summary>8 bit に戻したときのずれをどこまで許すか。丸めの往復で 1〜2 は動く。</summary>
        private const int Tolerance = 2;

        [System.Serializable]
        private sealed class Entry
        {
            public string name;
            public string hex;
            public string pattern;
            public float pattern_scale;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public Entry[] materials;
        }

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials", "Characters");

        private static string[] PaletteFiles()
        {
            return Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories);
        }

        private static Vector3Int ParseHex(string hex)
        {
            return new Vector3Int(
                int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }

        /// <summary>.mat（YAML）の _BaseColor を 0..255 で読む。マテリアルの色は sRGB のまま保存される。</summary>
        private static bool TryReadBaseColor(string matPath, out Vector3Int color)
        {
            color = default;
            Match m = Regex.Match(
                File.ReadAllText(matPath),
                @"- _BaseColor: \{r: (?<r>[-\d.eE]+), g: (?<g>[-\d.eE]+), b: (?<b>[-\d.eE]+)");
            if (!m.Success)
            {
                return false;
            }

            int To8(string v) => Mathf.Clamp(
                Mathf.RoundToInt(float.Parse(v, CultureInfo.InvariantCulture) * 255f), 0, 255);
            color = new Vector3Int(To8(m.Groups["r"].Value), To8(m.Groups["g"].Value), To8(m.Groups["b"].Value));
            return true;
        }

        [Test]
        public void 全キャラに色表がある()
        {
            foreach (string dir in Directory.GetDirectories(CharactersFolder))
            {
                if (Directory.GetFiles(dir, "*.fbx").Length == 0)
                {
                    continue;
                }

                Assert.IsTrue(
                    File.Exists(Path.Combine(dir, "palette.json")),
                    Path.GetFileName(dir) + "/palette.json が無い。blender/build_characters.py で作り直す");
            }
        }

        [Test]
        public void 色表とマテリアルの色が一致する()
        {
            string[] files = PaletteFiles();
            Assert.IsNotEmpty(files, "palette.json が 1 つも無い");
            var mismatched = new List<string>();
            int checkedCount = 0;

            foreach (string file in files)
            {
                string id = Path.GetFileName(Path.GetDirectoryName(file));
                PaletteFile palette = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file));
                Assert.IsNotNull(palette?.materials, file + " を読めない");
                Assert.IsNotEmpty(palette.materials, file + " が空");

                foreach (Entry entry in palette.materials)
                {
                    string mat = Path.Combine(MaterialsFolder, id + "_" + entry.name + ".mat");
                    if (!File.Exists(mat))
                    {
                        mismatched.Add(id + "_" + entry.name + ".mat が無い");
                        continue;
                    }

                    Assert.IsTrue(TryReadBaseColor(mat, out Vector3Int actual), mat + " に _BaseColor が無い");
                    Vector3Int declared = string.IsNullOrEmpty(entry.pattern)
                        ? ParseHex(entry.hex)
                        : new Vector3Int(255, 255, 255);
                    checkedCount++;
                    int worst = Mathf.Max(
                        Mathf.Abs(declared.x - actual.x),
                        Mathf.Max(Mathf.Abs(declared.y - actual.y), Mathf.Abs(declared.z - actual.z)));
                    if (worst > Tolerance)
                    {
                        mismatched.Add(string.Format(
                            "{0}_{1}: palette.json #{2} / .mat #{3:X2}{4:X2}{5:X2} (差 {6})",
                            id, entry.name, entry.hex, actual.x, actual.y, actual.z, worst));
                    }
                }
            }

            Assert.Greater(checkedCount, 20, "突き合わせた色が少なすぎる。読み取りが壊れている");
            Assert.IsEmpty(
                mismatched,
                "Blender のプレビューとゲームでキャラの色が違う。SceneBuilder.BuildAll で塗り直す:\n"
                + string.Join("\n", mismatched));
        }

        /// <summary>
        /// 柄の画像があるのに palette.json に倍率が無いと、ゲームでは柄が消えて平均色のベタ塗りになる。
        /// Blender（kcd_chara/mats.py の palette）が "pattern" と "pattern_scale" を書き忘れていないかを見る。
        /// </summary>
        [Test]
        public void 柄の画像がある服には柄の名前と倍率がある()
        {
            var missing = new List<string>();
            foreach (string file in PaletteFiles())
            {
                string dir = Path.GetDirectoryName(file);
                string id = Path.GetFileName(dir);
                foreach (Entry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file)).materials)
                {
                    foreach (string word in entry.name.Split('_'))
                    {
                        if (word == "face" || !File.Exists(Path.Combine(dir, word + ".png")))
                        {
                            continue;
                        }

                        if (entry.pattern != word || entry.pattern_scale <= 0f)
                        {
                            missing.Add(string.Format(
                                "{0}/{1}: {2}.png があるのに pattern='{3}', pattern_scale={4}",
                                id, entry.name, word, entry.pattern, entry.pattern_scale));
                        }
                    }
                }
            }

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        /// <summary>
        /// 柄の服の .mat に柄の画像と倍率が入っていて、画像の平均色が palette.json の hex と一致する。
        /// Blender の palette は柄をリニアで平均した色を hex にしているので、同じ計算で確かめる。
        /// </summary>
        [Test]
        public void 柄の服に柄の画像が貼られ平均色が色表と一致する()
        {
            float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
            float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

            int checkedCount = 0;
            foreach (string file in PaletteFiles())
            {
                string dir = Path.GetDirectoryName(file);
                string id = Path.GetFileName(dir);
                foreach (Entry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file)).materials)
                {
                    if (string.IsNullOrEmpty(entry.pattern))
                    {
                        continue;
                    }

                    string png = Path.Combine(dir, entry.pattern + ".png");
                    Assert.IsTrue(File.Exists(png), png + " が無い");
                    string assetPath = "Assets/Models/Characters/" + id + "/" + entry.pattern + ".png";
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    Assert.IsNotEmpty(guid, assetPath + " が取り込まれていない");
                    var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                    Assert.AreEqual(
                        TextureWrapMode.Repeat,
                        importer.wrapMode,
                        assetPath + " は倍率ぶん繰り返して貼るので Repeat にする（Clamp だと端の 1 画素が伸びる）");

                    string matName = id + "_" + entry.name + ".mat";
                    string mat = File.ReadAllText(Path.Combine(MaterialsFolder, matName));
                    StringAssert.Contains("_PATTERN_ON", mat, matName + " で柄が有効になっていない");
                    StringAssert.IsMatch(
                        @"- _PatternMap:\s*\n\s*m_Texture: \{fileID: \d+, guid: " + guid,
                        mat,
                        matName + " に " + entry.pattern + ".png が貼られていない");
                    Match scale = Regex.Match(mat, @"- _PatternScale: (?<v>[-\d.eE]+)");
                    Assert.IsTrue(scale.Success, matName + " に _PatternScale が無い");
                    Assert.AreEqual(
                        entry.pattern_scale,
                        float.Parse(scale.Groups["v"].Value, CultureInfo.InvariantCulture),
                        1e-4f,
                        matName + " の柄の倍率が palette.json と違う");

                    var texture = new Texture2D(2, 2);
                    Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(png)), png + " を読めない");
                    Color32[] pixels = texture.GetPixels32();
                    Object.DestroyImmediate(texture);
                    var sum = Vector3.zero;
                    foreach (Color32 c in pixels)
                    {
                        sum += new Vector3(ToLinear(c.r / 255f), ToLinear(c.g / 255f), ToLinear(c.b / 255f));
                    }

                    sum /= pixels.Length;
                    var mean = new Vector3Int(
                        Mathf.RoundToInt(ToSrgb(sum.x) * 255f),
                        Mathf.RoundToInt(ToSrgb(sum.y) * 255f),
                        Mathf.RoundToInt(ToSrgb(sum.z) * 255f));
                    Vector3Int declared = ParseHex(entry.hex);
                    int worst = Mathf.Max(
                        Mathf.Abs(declared.x - mean.x),
                        Mathf.Max(Mathf.Abs(declared.y - mean.y), Mathf.Abs(declared.z - mean.z)));
                    Assert.LessOrEqual(worst, Tolerance, string.Format(
                        "{0}/{1}: palette.json #{2} と {3}.png の平均 #{4:X2}{5:X2}{6:X2} が違う",
                        id, entry.name, entry.hex, entry.pattern, mean.x, mean.y, mean.z));
                    checkedCount++;
                }
            }

            Assert.Greater(checkedCount, 0, "柄のある服が 1 着も無い（坊っちゃんの絣はあるはず）");
        }

        /// <summary>
        /// 柄を貼るキャラのメッシュに Generated 座標（UV2, 軸ごとに 0..1）と bind 法線（UV3）が焼かれている。
        /// CharacterImporter が取り込み時に焼く。無いと柄が (0,0) の 1 色になる。
        /// </summary>
        [Test]
        public void 柄を貼るキャラのメッシュに柄の座標が焼かれている()
        {
            int checkedCount = 0;
            foreach (string file in PaletteFiles())
            {
                string id = Path.GetFileName(Path.GetDirectoryName(file));
                bool patterned = false;
                foreach (Entry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file)).materials)
                {
                    patterned |= !string.IsNullOrEmpty(entry.pattern);
                }

                if (!patterned)
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Models/Characters/" + id + "/" + id + ".fbx");
                Assert.IsNotNull(model, id + ".fbx を読めない");
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Mesh mesh = renderer.sharedMesh;
                    var generated = new List<Vector3>();
                    var normals = new List<Vector3>();
                    mesh.GetUVs(2, generated);
                    mesh.GetUVs(3, normals);
                    Assert.AreEqual(mesh.vertexCount, generated.Count, id + " の UV2（Generated 座標）が無い");
                    Assert.AreEqual(mesh.vertexCount, normals.Count, id + " の UV3（bind 法線）が無い");

                    Vector3 min = Vector3.positiveInfinity;
                    Vector3 max = Vector3.negativeInfinity;
                    foreach (Vector3 g in generated)
                    {
                        min = Vector3.Min(min, g);
                        max = Vector3.Max(max, g);
                    }

                    Assert.Less(min.magnitude, 1e-3f, id + " の Generated 座標の最小が 0 でない: " + min);
                    Assert.Less((max - Vector3.one).magnitude, 1e-3f, id + " の Generated 座標の最大が 1 でない: " + max);
                    checkedCount++;
                }
            }

            Assert.Greater(checkedCount, 0, "柄を貼るキャラのメッシュが見つからない");
        }

        [Test]
        public void まつ毛は肌より暗い()
        {
            // まつ毛は目の輪郭線。肌に近い明るさだと目元がぼやけて顔が読めなくなる。
            // 以前はまつ毛と眉がベージュ #E8E4DC で、肌とほとんど同じ明るさだった (#55)。
            // 眉は髪の色に合わせるので見ない（教授は白髪なので眉も白い）。
            float Luma(Vector3Int c) => (0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z) / 255f;

            foreach (string file in PaletteFiles())
            {
                string id = Path.GetFileName(Path.GetDirectoryName(file));
                var colors = new Dictionary<string, Vector3Int>();
                foreach (Entry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(file)).materials)
                {
                    colors[entry.name] = ParseHex(entry.hex);
                }

                if (colors.TryGetValue("skin", out Vector3Int skin) && colors.TryGetValue("lash", out Vector3Int lash))
                {
                    Assert.Less(Luma(lash), Luma(skin) - 0.2f, id + " のまつ毛が肌に近すぎる");
                }
            }
        }
    }
}
