using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// .fbx.meta の Humanoid の骨格（humanDescription.skeleton）が FBX の骨と一致していることを守る。
    ///
    /// Humanoid はポーズを付けるとき、骨の長さをこの骨格から取る。骨格は最初に取り込んだときに meta へ
    /// 書かれたきり更新されず、坊っちゃんを 2.8 頭身に作り直したあとも腰の高さが 0.80 m（FBX では 0.23 m）の
    /// ままだった。そのせいでゲームの中では脚が 0.7 m 伸び、足が地面から 0.64 m 沈んでいた。
    /// 落ちたら KCD/シーンを組み直す（CharacterImporter.SyncSkeletons）で直す。
    /// </summary>
    public sealed class CharacterSkeletonAgreementTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";

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
                    if (gap > 0.001f)
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
    }
}
