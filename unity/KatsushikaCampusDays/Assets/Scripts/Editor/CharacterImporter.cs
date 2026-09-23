using System.IO;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// Models/ 配下の FBX を取り込むときの共通設定。
    /// キャラクターは Humanoid + トゥーンマテリアル、キャンパスは静的メッシュとして扱う。
    /// </summary>
    public sealed class CharacterImporter : AssetPostprocessor
    {
        private const string CharacterRoot = "Assets/Models/Characters/";
        private const string CampusRoot = "Assets/Models/Campus/";
        private const string InteriorRoot = "Assets/Models/Interiors/";

        /// <summary>取り込み規則を変えたら上げる。既存の FBX が取り込み直される。</summary>
        public override uint GetVersion()
        {
            return 4;
        }

        /// <summary>
        /// Blender 側の外形ハル（<id>_outline）は本体と同じ巻き方向で出力されていて本体を覆ってしまう。
        /// 輪郭はシェーダの Outline パスで出すので、ハルの描画は止める。
        /// </summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (!IsCharacter(assetPath))
            {
                return;
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
            }
        }

        private void OnPreprocessModel()
        {
            if (assetImporter is not ModelImporter importer)
            {
                return;
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            if (IsCharacter(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.importBlendShapes = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.optimizeGameObjects = false;
                return;
            }

            if (IsCampus(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importBlendShapes = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.addCollider = false;
                importer.importNormals = ModelImporterNormals.Import;
                importer.weldVertices = false;
            }
        }

        /// <summary>
        /// FBX の take をクリップとして切り出し、待機・歩行・走行だけループさせる。
        ///
        /// Humanoid は既定で「root（骨盤）の回転と水平移動」をルートモーションとして取り出す。
        /// こちらは CharacterController / NavMeshAgent で動かすのでルートモーションは捨てており、
        /// 取り出された骨盤のヨーと左右の重心移動がまるごと消えて、腰から上が置き去りのまま
        /// 脚だけが動いて見えていた（#43 のくねくね）。3 軸とも Bake Into Pose にして、
        /// クリップに入れた重心移動をそのまま姿勢として再生させる。
        /// </summary>
        private void OnPreprocessAnimation()
        {
            if (assetImporter is not ModelImporter importer || !IsCharacter(assetPath))
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            foreach (ModelImporterClipAnimation clip in clips)
            {
                string name = clip.name.ToLowerInvariant();
                clip.loopTime = name.Contains("idle") || name.Contains("walk")
                    || name.Contains("run") || name.Contains("talk");
                clip.lockRootRotation = true;         // Root Transform Rotation: Bake Into Pose
                clip.keepOriginalOrientation = true;  // Based Upon: Original
                clip.lockRootHeightY = true;          // Root Transform Position (Y): Bake Into Pose
                clip.keepOriginalPositionY = true;
                clip.heightFromFeet = false;
                clip.lockRootPositionXZ = true;       // Root Transform Position (XZ): Bake Into Pose
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }

        /// <summary>
        /// FBX のマテリアル名から、こちらで用意した URP / Toon マテリアルへ差し替える。
        /// 取り込みの最中は新しいアセットを作れないので、既にあるものだけを返す。
        /// 足りないぶんは ResolveMaterials が後から作って貼り直す。
        /// </summary>
        private Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (material == null)
            {
                return null;
            }

            if (IsCharacter(assetPath))
            {
                return MaterialLibrary.FindCharacter(CharacterIdOf(assetPath), material.name);
            }

            return IsCampus(assetPath) ? MaterialLibrary.FindCampus(material.name) : null;
        }

        /// <summary>
        /// FBX に埋め込まれたままのマテリアルを、こちらのマテリアルへ差し替える。
        /// 取り込みが終わったあと（SceneBuilder の頭）で 1 回呼ぶ。
        /// </summary>
        public static int ResolveMaterials()
        {
            int remapped = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                bool character = IsCharacter(path);
                if (!character && !IsCampus(path))
                {
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    continue;
                }

                string characterId = character ? CharacterIdOf(path) : string.Empty;
                Texture2D face = character ? LoadFace(characterId) : null;
                bool changed = false;

                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not Material embedded)
                    {
                        continue;
                    }

                    Material target = character
                        ? MaterialLibrary.EnsureCharacter(characterId, embedded.name, face)
                        : MaterialLibrary.EnsureCampus(embedded.name);

                    if (target == null)
                    {
                        continue;
                    }

                    importer.AddRemap(
                        new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), target);
                    changed = true;
                    remapped++;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            return remapped;
        }

        /// <summary>
        /// face.png が FBX より後に取り込まれると顔テクスチャが空のままになる。
        /// SceneBuilder から呼んで、顔と目の 4 材質に貼り直す（既存の .mat も同じ見た目に揃える）。
        /// </summary>
        public static int RefreshFaceTextures()
        {
            if (!AssetDatabase.IsValidFolder(EditorPaths.CharactersFolder))
            {
                return 0;
            }

            int fixedCount = 0;
            string[] folders = AssetDatabase.GetSubFolders(EditorPaths.CharactersFolder);

            foreach (string folder in folders)
            {
                string characterId = Path.GetFileName(folder);
                Texture2D face = LoadFace(characterId);
                if (face == null)
                {
                    continue;
                }

                foreach (string name in MaterialLibrary.FaceTexturedNames)
                {
                    string path = MaterialLibrary.CharacterFolder + "/" + characterId + "_" + name + ".mat";
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || !MaterialLibrary.ApplyFaceLook(material, name, face))
                    {
                        continue;
                    }

                    EditorUtility.SetDirty(material);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return fixedCount;
        }

        private static Texture2D LoadFace(string characterId)
        {
            string path = EditorPaths.CharactersFolder + "/" + characterId + "/face.png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string CharacterIdOf(string path)
        {
            string tail = path.Substring(CharacterRoot.Length);
            int slash = tail.IndexOf('/');
            return slash > 0 ? tail.Substring(0, slash) : Path.GetFileNameWithoutExtension(tail);
        }

        private static bool IsCharacter(string path)
        {
            return path.StartsWith(CharacterRoot) && path.EndsWith(".fbx");
        }

        /// <summary>キャンパス（屋外）と屋内の FBX。どちらも静的な景色として同じ取り込み規則にする。</summary>
        private static bool IsCampus(string path)
        {
            return (path.StartsWith(CampusRoot) || path.StartsWith(InteriorRoot)) && path.EndsWith(".fbx");
        }
    }
}
