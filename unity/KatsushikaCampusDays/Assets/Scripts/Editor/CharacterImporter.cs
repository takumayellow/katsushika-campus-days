using System.Collections.Generic;
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
            return 6;
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

            // 柄の有無は palette.json で決まるので、書き換わったら FBX も取り込み直す。
            string palette = Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/palette.json";
            context.DependsOnSourceAsset(palette);
            bool patterned = File.Exists(palette) && File.ReadAllText(palette).Contains("\"pattern\"");
            int bodies = 0;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bodies += renderer.name.EndsWith("_outline", System.StringComparison.Ordinal) ? 0 : 1;
            }

            if (patterned && bodies > 1)
            {
                // Generated は Blender の 1 オブジェクトの箱が基準。メッシュが分かれると箱が変わり柄の大きさがずれる。
                Debug.LogWarning("[KCD] " + assetPath + " は体のメッシュが " + bodies
                    + " 枚ある。和柄の Generated 座標はメッシュごとの箱で焼くので、Blender と柄の大きさがずれる");
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name.EndsWith("_outline", System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
                else if (patterned)
                {
                    BakeGeneratedCoordinates(root.transform, renderer);
                }
            }
        }

        /// <summary>和柄の座標を焼くチャンネル。KCD/Toon の TEXCOORD2 / TEXCOORD3。</summary>
        public const int GeneratedUvChannel = 2;
        public const int BindNormalUvChannel = 3;

        /// <summary>
        /// Blender の Generated 座標と bind 時の法線を UV2 / UV3 に焼く (#55)。
        ///
        /// Blender は絣・ハート柄を「Generated 座標 × 倍率 → 画像のボックス投影」で貼っている（kcd_chara/mats.py の
        /// make_material, uv=False）。Generated はオブジェクト（体 1 枚のメッシュ）の元の形の外接箱を
        /// 軸ごとに 0..1 にした座標で、FBX には入らない。そこで取り込み時に同じ箱で計算して頂点に持たせる。
        /// 動いても柄が服に付いてくるよう、座標も法線も bind 姿勢のものを焼く。
        /// 頂点ごとに 24 バイト増えるので（WebGL のダウンロードに効く）、palette.json に柄のあるキャラだけにする。
        ///
        /// 軸は Blender のオブジェクト軸（Z が上）に戻す。Blender の -Y（正面）が Unity の +Z、
        /// Blender の +X（キャラの左）が Unity の -X になる（axis_forward=-Z, axis_up=Y の書き出し）。
        /// </summary>
        private static void BakeGeneratedCoordinates(Transform root, SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0)
            {
                return;
            }

            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            var generated = new List<Vector3>(vertices.Length);
            var bindNormals = new List<Vector3>(vertices.Length);
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = ToBlenderAxes(toRoot.MultiplyPoint3x4(vertices[i]));
                generated.Add(p);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                Vector3 n = normals.Length == vertices.Length ? toRoot.MultiplyVector(normals[i]) : Vector3.up;
                bindNormals.Add(ToBlenderAxes(n).normalized);
            }

            Vector3 size = max - min;
            for (int i = 0; i < generated.Count; i++)
            {
                Vector3 p = generated[i] - min;
                generated[i] = new Vector3(
                    size.x > 1e-6f ? p.x / size.x : 0.5f,
                    size.y > 1e-6f ? p.y / size.y : 0.5f,
                    size.z > 1e-6f ? p.z / size.z : 0.5f);
            }

            mesh.SetUVs(GeneratedUvChannel, generated);
            mesh.SetUVs(BindNormalUvChannel, bindNormals);
        }

        /// <summary>Unity のモデル空間（Y 上, +Z 正面）→ Blender のオブジェクト空間（Z 上, -Y 正面）。</summary>
        public static Vector3 ToBlenderAxes(Vector3 unity)
        {
            return new Vector3(-unity.x, -unity.z, unity.y);
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
        /// .fbx.meta に残っている Humanoid の骨格（humanDescription.skeleton）を、取り込んだモデルの骨に合わせる。
        /// 直した FBX の数を返す。SceneBuilder.BuildAll から呼ぶ。
        ///
        /// Humanoid はポーズを付けるとき、骨の長さを FBX ではなくこの骨格から取る。骨格は最初に取り込んだときに
        /// meta へ書かれたきり更新されないので、Blender で等身を変えると古い骨の長さで組まれる。
        /// 坊っちゃん（2.8 頭身）は腰の高さが FBX で 0.23 m なのに meta では 0.80 m のままで、
        /// Idle にすると全高が 1.15 m から 1.86 m に伸び、足が地面から 0.64 m 沈んでいた。
        ///
        /// 骨格を空にして Unity に作り直させると、骨の割り当て（human）まで自動で付け直され、
        /// 髪の揺れのボーン（HairFront / HairBack）が目にされてしまう。なので骨格だけを書き換える。
        /// </summary>
        public static int SyncSkeletons()
        {
            int fixedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models/Characters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsCharacter(path)
                    || AssetImporter.GetAtPath(path) is not ModelImporter importer
                    || importer.animationType != ModelImporterAnimationType.Human)
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                HumanDescription human = importer.humanDescription;
                if (model == null || SkeletonMatches(human.skeleton, model))
                {
                    continue;
                }

                human.skeleton = SkeletonOf(model, human.skeleton);
                importer.humanDescription = human;
                importer.SaveAndReimport();
                fixedCount++;
            }

            return fixedCount;
        }

        /// <summary>モデルの骨（根元以外のすべての Transform）が、骨格に同じ名前・同じ位置と向きで載っているか。</summary>
        private static bool SkeletonMatches(SkeletonBone[] skeleton, GameObject model)
        {
            if (skeleton == null || skeleton.Length == 0)
            {
                return false;
            }

            var byName = new Dictionary<string, SkeletonBone>();
            foreach (SkeletonBone bone in skeleton)
            {
                byName[bone.name] = bone;
            }

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == model.transform)
                {
                    continue;
                }

                if (!byName.TryGetValue(t.name, out SkeletonBone bone)
                    || (bone.position - t.localPosition).sqrMagnitude > 1e-8f
                    || Quaternion.Angle(bone.rotation, t.localRotation) > 0.05f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>SkeletonBone.parentName は public でないので、Unity が書く meta と同じ形にするためだけに使う。</summary>
        private static readonly System.Reflection.FieldInfo ParentNameField = typeof(SkeletonBone).GetField(
            "parentName",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        /// <summary>モデルの今の骨から骨格を作る。先頭（モデルの根元）は元の骨格のものを残す。</summary>
        private static SkeletonBone[] SkeletonOf(GameObject model, SkeletonBone[] previous)
        {
            var bones = new List<SkeletonBone>();
            bones.Add(previous != null && previous.Length > 0
                ? previous[0]
                : new SkeletonBone
                {
                    name = model.name + "(Clone)",
                    rotation = Quaternion.identity,
                    scale = Vector3.one,
                });

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == model.transform)
                {
                    continue;
                }

                object bone = new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                };
                ParentNameField?.SetValue(bone, t.parent == model.transform ? bones[0].name : t.parent.name);
                bones.Add((SkeletonBone)bone);
            }

            return bones.ToArray();
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
