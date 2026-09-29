using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// 顔の輪郭線が palette.json の outward_faces と合っていることを守る (#47)。
    ///
    /// KCD/Toon の輪郭線は法線の向きへ押し出した殻の裏面なので、面が内向きに出力された顔に
    /// 付けると顔の前に殻がかぶる。Blender が顔の面を外向きに出したキャラだけ palette.json に
    /// outward_faces を書き、MaterialLibrary.ApplyFaceLook がそのキャラの face に輪郭線を付ける。
    /// ここでは実際に保存されている .mat の _OutlineWidth を突き合わせる。目は常に線なし。
    ///
    /// まつ毛・眉・二重線は顔の表面に貼った細い帯で、殻を押し出すと縁がぎざぎざの黒い線になるので、
    /// どのキャラでも線を付けない（Blender の kcd_chara/outline.py の SKIP_PARTS と同じ）。
    /// </summary>
    public sealed class CharacterFaceOutlineTests
    {
        [System.Serializable]
        private sealed class PaletteEntry
        {
            public string name;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public PaletteEntry[] materials;
            public bool outward_faces;
        }

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials", "Characters");

        private static float ReadOutlineWidth(string matPath)
        {
            Match m = Regex.Match(File.ReadAllText(matPath), @"- _OutlineWidth: (?<w>[-\d.eE]+)");
            Assert.IsTrue(m.Success, matPath + " に _OutlineWidth が無い");
            return float.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
        }

        [Test]
        public void 顔の輪郭線は面が外向きのキャラだけに付く()
        {
            int outward = 0;
            foreach (string palette in Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories))
            {
                string id = Path.GetFileName(Path.GetDirectoryName(palette));
                string face = Path.Combine(MaterialsFolder, id + "_face.mat");
                if (!File.Exists(face))
                {
                    continue;
                }

                bool expected = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(palette)).outward_faces;
                float width = ReadOutlineWidth(face);
                if (expected)
                {
                    outward++;
                    Assert.Greater(width, 0f, id + " は顔の面が外向きなのに顔に輪郭線が無い");
                }
                else
                {
                    Assert.AreEqual(0f, width, id + " は顔の面が内向きなのに顔に輪郭線がある");
                }

                foreach (string eye in new[] { "eye_white", "eye_l", "eye_r" })
                {
                    string path = Path.Combine(MaterialsFolder, id + "_" + eye + ".mat");
                    if (File.Exists(path))
                    {
                        Assert.AreEqual(0f, ReadOutlineWidth(path), id + "_" + eye + " に輪郭線がある");
                    }
                }
            }

            // mirai・坊っちゃん・マドンナちゃんは顔の面が外向き
            Assert.GreaterOrEqual(outward, 3, "outward_faces のキャラが見つからない");
        }

        [Test]
        public void まつ毛と眉と二重線には輪郭線が無い()
        {
            // palette.json は FBX が使う材質の一覧。顔テクスチャに描いたキャラの古い lash.mat などは見ない
            int checkedCount = 0;
            foreach (string palette in Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories))
            {
                string id = Path.GetFileName(Path.GetDirectoryName(palette));
                foreach (PaletteEntry entry in JsonUtility.FromJson<PaletteFile>(File.ReadAllText(palette)).materials)
                {
                    if (entry.name != "lash" && entry.name != "brow" && entry.name != "eye_rim")
                    {
                        continue;
                    }

                    string path = Path.Combine(MaterialsFolder, id + "_" + entry.name + ".mat");
                    checkedCount++;
                    Assert.AreEqual(0f, ReadOutlineWidth(path), id + "_" + entry.name + " に輪郭線がある");
                }
            }

            Assert.Greater(checkedCount, 0, "まつ毛・眉・二重線の材質が見つからない");
        }
    }
}
