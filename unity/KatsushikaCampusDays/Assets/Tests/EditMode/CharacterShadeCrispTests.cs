using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの陰の境目が画面の 1 画素の幅になることを守る (#47)。
    ///
    /// KCD/Toon の陰は half-lambert を smoothstep で塗り分ける。幅を ±_ShadeSoftness に固定すると、
    /// half-lambert がゆっくり変わる後頭部のような大きな丸みで境目が数十画素にぼけ、陰が輪郭の無い斑になる。
    /// MaterialLibrary は、キャラの材質に _ShadeCrisp = 1 を付ける。ここでは実際に保存されている .mat と、
    /// 顔と目の見た目を付ける経路を確かめる。キャンパスの材質（木の葉や幹）は柔らかい境目のまま。
    /// このアセンブリは KCD.Editor を参照していないので、MaterialLibrary はリフレクションで呼ぶ。
    /// </summary>
    public sealed class CharacterShadeCrispTests
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
        }

        private static string CharactersFolder => Path.Combine(Application.dataPath, "Models", "Characters");

        private static string MaterialsFolder => Path.Combine(Application.dataPath, "Materials");

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

        private static string[] FaceTexturedNames()
        {
            FieldInfo field = MaterialLibraryType().GetField("FaceTexturedNames", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "MaterialLibrary.FaceTexturedNames が無い");
            return (string[])field.GetValue(null);
        }

        private static float ReadFloat(string text, string property, float missing)
        {
            Match m = Regex.Match(text, @"- " + property + @": (?<v>[-\d.eE]+)");
            return m.Success ? float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : missing;
        }

        [Test]
        public void キャラの材質は陰の境目を画面の1画素の幅にする()
        {
            string[] faceTextured = FaceTexturedNames();
            string[] palettes = Directory.GetFiles(CharactersFolder, "palette.json", SearchOption.AllDirectories);
            Assert.IsNotEmpty(palettes, "palette.json が見つからない");
            foreach (string palette in palettes)
            {
                string id = Path.GetFileName(Path.GetDirectoryName(palette));
                int checkedCount = 0;
                PaletteEntry[] entries = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(palette)).materials;
                Assert.IsNotNull(entries, palette + " に materials が無い");

                // palette.json は FBX が使う材質の色表。顔テクスチャを貼る顔と目は載らないので足す
                foreach (string name in entries.Select(e => e.name).Concat(faceTextured))
                {
                    string path = Path.Combine(MaterialsFolder, "Characters", id + "_" + name + ".mat");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    checkedCount++;
                    Assert.AreEqual(1f, ReadFloat(File.ReadAllText(path), "_ShadeCrisp", 0f),
                        id + "_" + name + " の陰の境目が ±_ShadeSoftness の幅でぼける");
                }

                Assert.Greater(checkedCount, 0, id + " の材質が見つからない");
            }
        }

        [Test]
        public void 顔と目の見た目を付けると陰の境目も1画素の幅になる()
        {
            MethodInfo apply = MaterialLibraryType().GetMethod("ApplyFaceLook", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(apply, "MaterialLibrary.ApplyFaceLook が無い");
            Shader toon = Shader.Find("KCD/Toon");
            Assert.IsNotNull(toon, "KCD/Toon シェーダが見つからない");
            foreach (string name in FaceTexturedNames())
            {
                var material = new Material(toon);
                try
                {
                    object[] args = { material, name, Texture2D.whiteTexture, true };
                    Assert.IsTrue((bool)apply.Invoke(null, args), name + " の見た目が付かない");
                    Assert.AreEqual(1f, material.GetFloat("_ShadeCrisp"), name);
                    Assert.IsFalse((bool)apply.Invoke(null, args), name + " を付け直すたびに材質が変わる");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void キャンパスの材質の陰の境目は柔らかいまま()
        {
            string[] campus = Directory.GetFiles(Path.Combine(MaterialsFolder, "Campus"), "*.mat");
            Assert.IsNotEmpty(campus, "キャンパスの材質が見つからない");
            foreach (string path in campus)
            {
                Assert.AreEqual(0f, ReadFloat(File.ReadAllText(path), "_ShadeCrisp", 0f),
                    Path.GetFileName(path) + " の陰の境目が 1 画素の幅になる");
            }
        }
    }
}
