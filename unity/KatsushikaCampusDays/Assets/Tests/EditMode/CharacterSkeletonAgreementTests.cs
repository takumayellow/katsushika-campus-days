using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// .fbx.meta の Humanoid の骨格（humanDescription.skeleton）と、シーンに置いた体の骨が、FBX の骨と一致して
    /// いることを守る。
    ///
    /// Humanoid はポーズを付けるとき、骨の長さをこの骨格から取る。骨格は最初に取り込んだときに meta へ
    /// 書かれたきり更新されず、坊っちゃんを 2.8 頭身に作り直したあとも腰の高さが 0.80 m（FBX では 0.23 m）の
    /// ままだった。そのせいでゲームの中では脚が 0.7 m 伸び、足が地面から 0.64 m 沈んでいた。
    /// 落ちたら KCD/シーンを組み直す（CharacterImporter.SyncSkeletons）で直す。
    /// </summary>
    public sealed class CharacterSkeletonAgreementTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";
        private const string ScenesFolder = "Assets/Scenes";

        /// <summary>骨の位置と境界の、許すずれ（m）。</summary>
        private const float Tolerance = 0.001f;

        /// <summary>ポーズの組み立てに効く骨。髪の揺れの骨も、無いと揺れが止まるので見る。</summary>
        private static readonly string[] Watched =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
            "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        };

        [Test]
        public void 骨格とFBXの骨の位置が一致する()
        {
            var stale = new List<string>();
            int checkedCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx")
                    || AssetImporter.GetAtPath(path) is not ModelImporter importer
                    || importer.animationType != ModelImporterAnimationType.Human)
                {
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path + " を読めない");
                var skeleton = new Dictionary<string, SkeletonBone>();
                foreach (SkeletonBone bone in importer.humanDescription.skeleton)
                {
                    skeleton[bone.name] = bone;
                }

                var bones = new Dictionary<string, Transform>();
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    bones[t.name] = t;
                }

                foreach (string name in Watched)
                {
                    if (!bones.TryGetValue(name, out Transform t))
                    {
                        continue;
                    }

                    checkedCount++;
                    if (!skeleton.TryGetValue(name, out SkeletonBone bone))
                    {
                        stale.Add(model.name + "/" + name + ": 骨格に無い");
                        continue;
                    }

                    float gap = Vector3.Distance(bone.position, t.localPosition);
                    if (gap > Tolerance)
                    {
                        stale.Add(string.Format(
                            "{0}/{1}: 骨格 {2} / FBX {3}（差 {4:F3} m）",
                            model.name, name, bone.position.ToString("F3"), t.localPosition.ToString("F3"), gap));
                    }
                }
            }

            Assert.Greater(checkedCount, 50, "突き合わせた骨が少なすぎる。読み取りが壊れている");
            Assert.IsEmpty(
                stale,
                "meta の Humanoid の骨格が FBX と違う。ゲームで体が伸び縮みする。KCD/シーンを組み直す で直す:\n"
                + string.Join("\n", stale));
        }

        [Test]
        public void 髪の揺れの骨が目に割り当てられていない()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer
                    || importer.animationType != ModelImporterAnimationType.Human)
                {
                    continue;
                }

                foreach (HumanBone bone in importer.humanDescription.human)
                {
                    Assert.IsFalse(
                        bone.boneName.StartsWith("Hair"),
                        path + ": " + bone.boneName + " が " + bone.humanName + " に割り当てられている。髪が揺れなくなる");
                }
            }
        }

        /// <summary>
        /// シーンに置いた体は、組み直したときに FBX を unpack して写したもので、骨の位置と描画の境界をシーンが
        /// 持っている。FBX を書き出し直すと、メッシュ（bindpose を含む）は新しい FBX から読まれるが、骨と境界は
        /// 古い写しのまま残り、ゲームの中で体が崩れる。
        /// </summary>
        [Test]
        public void シーンに置いた体の骨と境界がFBXと一致する()
        {
            var stale = new List<string>();
            var models = new Dictionary<string, ModelParts>();
            int checkedCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder }))
            {
                Scene scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid), OpenSceneMode.Additive);
                try
                {
                    var seen = new HashSet<Transform>();
                    foreach (SkinnedMeshRenderer smr in RenderersIn(scene))
                    {
                        ModelParts model = ModelOf(smr, models);
                        if (model != null)
                        {
                            model.CompareBounds(scene.name, smr, stale);
                            checkedCount += model.CompareBones(scene.name, smr, seen, stale);
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            Assert.Greater(checkedCount, 400, "突き合わせた骨が少なすぎる。読み取りが壊れている");
            Assert.IsEmpty(
                stale,
                "シーンに置いた体の骨か境界が FBX と違う。ゲームで体が崩れる。KCD/シーンを組み直す で直す:\n"
                + string.Join("\n", stale));
        }

        private static List<SkinnedMeshRenderer> RenderersIn(Scene scene)
        {
            var renderers = new List<SkinnedMeshRenderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                renderers.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            }

            return renderers;
        }

        /// <summary>キャラの FBX のメッシュを使う smr なら、その FBX の部品を返す。ほかは null。</summary>
        private static ModelParts ModelOf(SkinnedMeshRenderer smr, Dictionary<string, ModelParts> models)
        {
            string fbx = smr.sharedMesh != null ? AssetDatabase.GetAssetPath(smr.sharedMesh) : string.Empty;
            if (!fbx.StartsWith(CharactersFolder + "/") || !fbx.EndsWith(".fbx"))
            {
                return null;
            }

            if (!models.TryGetValue(fbx, out ModelParts model))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                Assert.IsNotNull(asset, fbx + " を読めない");
                model = new ModelParts(asset);
                models[fbx] = model;
            }

            return model;
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform parent = t.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        /// <summary>FBX の Transform と SkinnedMeshRenderer を、名前で引けるようにしたもの。</summary>
        private sealed class ModelParts
        {
            private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
            private readonly Dictionary<string, SkinnedMeshRenderer> _renderers =
                new Dictionary<string, SkinnedMeshRenderer>();

            public ModelParts(GameObject model)
            {
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    _bones[t.name] = t;
                }

                foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    _renderers[smr.name] = smr;
                }
            }

            /// <summary>描画の境界（中心と半分の大きさ）を、FBX の同じ名前のメッシュと比べる。</summary>
            public void CompareBounds(string scene, SkinnedMeshRenderer smr, List<string> stale)
            {
                if (!_renderers.TryGetValue(smr.name, out SkinnedMeshRenderer source))
                {
                    stale.Add(scene + " " + PathOf(smr.transform) + ": FBX に同じ名前のメッシュが無い");
                    return;
                }

                Bounds here = smr.localBounds;
                Bounds there = source.localBounds;
                if (Vector3.Distance(here.center, there.center) > Tolerance
                    || Vector3.Distance(here.extents, there.extents) > Tolerance)
                {
                    stale.Add(string.Format(
                        "{0} {1}: 境界 シーン {2} / FBX {3}",
                        scene, PathOf(smr.transform), here.ToString("F3"), there.ToString("F3")));
                }
            }

            /// <summary>まだ見ていない骨の位置を FBX の同じ名前の骨と比べ、比べた骨の数を返す。</summary>
            public int CompareBones(string scene, SkinnedMeshRenderer smr, HashSet<Transform> seen, List<string> stale)
            {
                int count = 0;
                foreach (Transform bone in smr.bones)
                {
                    if (bone == null)
                    {
                        stale.Add(scene + " " + PathOf(smr.transform) + ": 骨が欠けている");
                        continue;
                    }

                    if (!seen.Add(bone))
                    {
                        continue;
                    }

                    count++;
                    if (!_bones.TryGetValue(bone.name, out Transform source))
                    {
                        stale.Add(scene + " " + PathOf(bone) + ": FBX に無い骨");
                        continue;
                    }

                    float gap = Vector3.Distance(bone.localPosition, source.localPosition);
                    if (gap > Tolerance)
                    {
                        stale.Add(string.Format(
                            "{0} {1}: シーン {2} / FBX {3}（差 {4:F3} m）",
                            scene,
                            PathOf(bone),
                            bone.localPosition.ToString("F3"),
                            source.localPosition.ToString("F3"),
                            gap));
                    }
                }

                return count;
            }
        }
    }
}
