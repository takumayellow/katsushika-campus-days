using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// WebGL の data を食う設定が戻っていないことを守る (#70)。上限は tools/deploy_pages.py の MAX_DATA_BYTES。
    /// 実測と内訳は docs/WEBGL_BUDGET.md の 1.8。
    /// </summary>
    public sealed class WebGLDataBudgetTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

        /// <summary>CharacterImporter が建物と家具として取り込むフォルダ。</summary>
        private static readonly string[] CampusFolders = { "Assets/Models/Campus", "Assets/Models/Interiors" };

        /// <summary>CharacterFaceImportRules.WebGLMaxSize と同じ値。</summary>
        private const int FaceMaxSize = 1024;

        /// <summary>FontLibrary.FontAssetPath と同じ。</summary>
        private const string ScreenFontAssetPath = "Assets/Fonts/KCD_JP.asset";

        /// <summary>tools/make_ui_font.py の出力。</summary>
        private const string UiFontPath = "Assets/Fonts/KCDUIJP-Thin.ttf";

        private static List<string> FacePaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("face t:Texture2D", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/face.png") && path.Split('/').Length == 5)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        [Test]
        public void CharacterFacesAreCappedForWebGL()
        {
            List<string> paths = FacePaths();
            Assert.IsNotEmpty(paths, CharactersFolder + "/<id>/face.png が見つからない");
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path);
                TextureImporterPlatformSettings web = importer.GetPlatformTextureSettings("WebGL");
                Assert.IsTrue(web.overridden, path + " の WebGL の上書きが無い");
                Assert.AreEqual(FaceMaxSize, web.maxTextureSize, path + " の WebGL の最大サイズ");
                Assert.AreEqual(TextureImporterFormat.Automatic, web.format,
                    path + " の WebGL の形式（自動でないと DXT と ASTC のビルドで同じ形式になる）");
                Assert.AreEqual(TextureImporterCompression.Compressed, web.textureCompression, path);
            }
        }

        [Test]
        public void CharacterOutlineHullMeshesAreEmpty()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder });
            Assert.IsNotEmpty(guids, CharactersFolder + " にモデルが無い");
            int hulls = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path);
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    hulls++;
                    Assert.IsFalse(renderer.enabled, path + " の " + renderer.name + " が描かれる");
                    Assert.IsTrue(renderer.sharedMesh == null || renderer.sharedMesh.vertexCount == 0,
                        path + " の外形ハル " + renderer.name + " のメッシュが空でない（シーンの参照でビルドに入る）");
                }
            }

            Assert.Greater(hulls, 0, "外形ハル（<id>_outline）が 1 つも無い。名前の付け方が変わったらこのテストを直す");
        }

        [Test]
        public void CampusMeshesHaveNoTangents()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", CampusFolders);
            Assert.IsNotEmpty(guids, "キャンパスのモデルが無い");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(importer, path);
                Assert.AreEqual(ModelImporterTangents.None, importer.importTangents, path + " が tangent を作る");
            }
        }

        [Test]
        public void ScreenFontIsTheSubset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ScreenFontAssetPath);
            Assert.IsNotNull(asset, ScreenFontAssetPath);
            Assert.AreEqual(UiFontPath, AssetDatabase.GetAssetPath(asset.sourceFontFile),
                "KCD_JP の元が tools/make_ui_font.py の絞ったフォントでない（元のフォントがそのまま data に入る）");
        }

        [Test]
        public void SplashLogoIsNotBuilt()
        {
            Assert.IsFalse(PlayerSettings.SplashScreen.show, "スプラッシュは出さない");
            Assert.IsFalse(PlayerSettings.SplashScreen.showUnityLogo, "ロゴの画像がビルドに入る");
        }
    }
}
