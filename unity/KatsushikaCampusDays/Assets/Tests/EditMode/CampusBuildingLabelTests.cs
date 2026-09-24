using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 建物の名札（BuildingLabel）が縁取り付きのマテリアルを 1 枚だけ共有していること (#64)。
    ///
    /// 名札の縁取りを TextMeshPro の outlineWidth / outlineColor で付けると、編集時に renderer.material が呼ばれ、
    /// 名札 11 枚ぶんのマテリアルの複製（KCD_JP Material (Instance)）が Campus.unity に埋め込まれ、
    /// シーンを組み直すたびに「Instantiating material due to calling renderer.material」が 11 行出ていた。
    /// 縁取りは FontLibrary.EnsureSignMaterial の共有マテリアルに持たせる。
    /// </summary>
    public sealed class CampusBuildingLabelTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>FontLibrary.SignMaterialPath と同じ（テストのアセンブリは KCD.Editor を参照しない）。</summary>
        private const string SignMaterialPath = "Assets/Fonts/KCD_JP - Sign Outline.mat";

        private const string OutlineKeyword = "OUTLINE_ON";

        private Scene _scene;
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (BuildingLabel label in root.GetComponentsInChildren<BuildingLabel>(true))
                {
                    _labels.Add(label.GetComponent<TextMeshPro>());
                }
            }
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void 名札は縁取りの共有マテリアルを1枚だけ使う()
        {
            Assert.IsNotEmpty(_labels, ScenePath + " に建物の名札が無い（KCD/シーンを組み直す）");

            Material shared = AssetDatabase.LoadAssetAtPath<Material>(SignMaterialPath);
            Assert.IsNotNull(shared, SignMaterialPath + " が無い（KCD/シーンを組み直す）");

            foreach (TextMeshPro text in _labels)
            {
                Assert.IsNotNull(text, "BuildingLabel に TextMeshPro が無い");
                Assert.AreSame(shared, text.fontSharedMaterial,
                    text.name + " の名札が共有マテリアルでない: " + AssetPathOf(text.fontSharedMaterial));
                Assert.AreSame(shared, text.GetComponent<MeshRenderer>().sharedMaterial,
                    text.name + " の MeshRenderer に共有マテリアルが差さっていない");
            }
        }

        [Test]
        public void 共有マテリアルは縁取りを描きフォントのアトラスを読む()
        {
            Material shared = AssetDatabase.LoadAssetAtPath<Material>(SignMaterialPath);
            Assert.IsNotNull(shared, SignMaterialPath + " が無い（KCD/シーンを組み直す）");

            // Mobile/Distance Field の縁取りは shader_feature なので、キーワードが無いと幅を入れても描かれない。
            Assert.IsTrue(shared.IsKeywordEnabled(OutlineKeyword), SignMaterialPath + " で " + OutlineKeyword + " が無効");
            Assert.Greater(shared.GetFloat("_OutlineWidth"), 0f, SignMaterialPath + " の縁取りの幅が 0");

            Assert.IsNotEmpty(_labels, ScenePath + " に建物の名札が無い（KCD/シーンを組み直す）");
            TMP_FontAsset font = _labels[0].font;
            Assert.IsNotNull(font, _labels[0].name + " にフォントが無い");
            // アトラスが違うと TextMeshPro はフォントの既定マテリアルへ戻してしまい、縁取りが消える。
            Assert.AreSame(font.atlasTexture, shared.GetTexture("_MainTex"),
                SignMaterialPath + " の _MainTex が " + font.name + " のアトラスでない");
        }

        [Test]
        public void シーンに複製されたマテリアルを埋め込まない()
        {
            string path = Path.Combine(Application.dataPath, "Scenes", "Campus.unity");
            Assert.IsTrue(File.Exists(path), path + " が無い");

            var copies = new List<string>();
            foreach (string document in File.ReadAllText(path).Split(new[] { "\n--- " }, System.StringSplitOptions.None))
            {
                if (!document.StartsWith("!u!21 "))
                {
                    continue;
                }

                Match name = Regex.Match(document, @"^\s*m_Name:\s*(.*?)\s*$", RegexOptions.Multiline);
                if (name.Success && name.Groups[1].Value.EndsWith("(Instance)"))
                {
                    copies.Add(name.Groups[1].Value);
                }
            }

            Assert.IsEmpty(copies,
                ScenePath + " に renderer.material の複製が " + copies.Count + " 個埋め込まれている: " + string.Join(", ", copies));
        }

        private static string AssetPathOf(Material material)
        {
            if (material == null)
            {
                return "(なし)";
            }

            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? material.name + "（シーン埋め込み）" : path;
        }
    }
}
