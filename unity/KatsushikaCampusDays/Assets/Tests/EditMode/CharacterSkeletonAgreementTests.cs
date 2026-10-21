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

        /// <summary>骨の向きの、許すずれ（度）。CharacterImporter.SkeletonMatches と同じ。</summary>
        private const float AngleTolerance = 0.05f;

        /// <summary>骨の拡大率の、許すずれ（拡大率の差の大きさ）。</summary>
        private const float ScaleTolerance = 0.001f;

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
        /// シーンに置いた体は、組み直したときに FBX を unpack して写したもので、骨の位置・向き・拡大率と描画の境界を
        /// シーンが持っている。FBX を書き出し直すと、メッシュ（bindpose を含む）は新しい FBX から読まれるが、骨と
        /// 境界は古い写しのまま残り、ゲームの中で体が崩れる。
        /// </summary>
        [Test]
        public void シーンに置いた体の骨と境界がFBXと一致する()
        {
            var stale = new List<string>();
            var models = new Dictionary<string, ModelParts>();

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder }))
            {
                // エディタで開いているシーンは、開いているものをそのまま読む。閉じると、保存していない変更が消える。
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                try
                {
                    var seen = new HashSet<Transform>();
                    foreach (SkinnedMeshRenderer smr in RenderersIn(scene))
                    {
                        ModelParts model = ModelOf(smr, models);
                        if (model != null)
                        {
                            model.CompareBounds(scene.name, smr, stale);
                            model.CompareBones(scene.name, smr, seen, stale);
                        }
                    }
                }
                finally
                {
                    if (opened)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            // 主人公の 3 人の体は、どれもシーンに置いてある。見つからない人がいれば、その体は比べないまま通ってしまう。
            foreach (string id in GameManager.PlayableCharacterIds)
            {
                string fbx = CharactersFolder + "/" + id + "/" + id + ".fbx";
                Assert.IsTrue(
                    models.ContainsKey(fbx),
                    ScenesFolder + " のどのシーンにも、" + fbx + " のメッシュを使う体が無い。シーンに体を置かなくなったのなら、"
                    + "このテストの前提を見直す。置いてあるなら、体のメッシュの参照が切れているか、メッシュから FBX を引く "
                    + "ModelOf が壊れている");
            }

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
            private readonly Transform _root;
            private readonly PartsByName<Transform> _bones = new PartsByName<Transform>("骨");
            private readonly PartsByName<SkinnedMeshRenderer> _renderers =
                new PartsByName<SkinnedMeshRenderer>("メッシュ");

            public ModelParts(GameObject model)
            {
                // 根元は、シーンが体を置いた場所なので比べない。根元はメッシュと同じ名前（mirai など）なので、
                // 骨の表にも入れない。
                _root = model.transform;
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t != _root)
                    {
                        _bones.Add(t);
                    }
                }

                foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    _renderers.Add(smr);
                }
            }

            /// <summary>描画の境界（中心と半分の大きさ）と、境界の基準の骨を、FBX の同じ名前のメッシュと比べる。</summary>
            public void CompareBounds(string scene, SkinnedMeshRenderer smr, List<string> stale)
            {
                string where = scene + " " + PathOf(smr.transform);
                if (!_renderers.TryFind(smr.name, where, stale, out SkinnedMeshRenderer source))
                {
                    return;
                }

                string rootHere = smr.rootBone != null ? smr.rootBone.name : "（無し）";
                string rootThere = source.rootBone != null ? source.rootBone.name : "（無し）";
                if (rootHere != rootThere)
                {
                    stale.Add(where + ": 境界の基準の骨 シーン " + rootHere + " / FBX " + rootThere);
                }

                Bounds here = smr.localBounds;
                Bounds there = source.localBounds;
                if (Vector3.Distance(here.center, there.center) > Tolerance
                    || Vector3.Distance(here.extents, there.extents) > Tolerance)
                {
                    stale.Add(string.Format(
                        "{0}: 境界 シーン {1} / FBX {2}",
                        where, here.ToString("F3"), there.ToString("F3")));
                }
            }

            /// <summary>
            /// まだ見ていない骨の位置・向き・拡大率を、FBX の同じ名前の骨と比べる。境界の基準の骨（rootBone）と、
            /// 骨から FBX の根元の手前（Armature）までの親も比べる。体の根元は、シーンが置いた場所なので比べない。
            /// </summary>
            public void CompareBones(string scene, SkinnedMeshRenderer smr, HashSet<Transform> seen, List<string> stale)
            {
                var bones = new List<Transform>(smr.bones);
                if (smr.rootBone != null)
                {
                    bones.Add(smr.rootBone);
                }

                foreach (Transform bone in bones)
                {
                    if (bone == null)
                    {
                        stale.Add(scene + " " + PathOf(smr.transform) + ": 骨が欠けている");
                        continue;
                    }

                    if (seen.Contains(bone)
                        || !_bones.TryFind(bone.name, scene + " " + PathOf(bone), stale, out Transform source))
                    {
                        continue;
                    }

                    Transform here = bone;
                    Transform there = source;
                    while (there != _root && seen.Add(here))
                    {
                        CompareTransform(scene, here, there, stale);
                        here = here.parent;
                        there = there.parent;
                        if (there != _root && (here == null || here.name != there.name))
                        {
                            stale.Add(scene + " " + PathOf(bone) + ": 親が FBX と違う（FBX では " + there.name + "）");
                            break;
                        }
                    }
                }
            }

            /// <summary>位置・向き・拡大率のどれかが FBX とずれていたら、ずれとして出す。</summary>
            private static void CompareTransform(string scene, Transform here, Transform there, List<string> stale)
            {
                float gap = Vector3.Distance(here.localPosition, there.localPosition);
                float angle = Quaternion.Angle(here.localRotation, there.localRotation);
                float scale = Vector3.Distance(here.localScale, there.localScale);
                if (gap > Tolerance || angle > AngleTolerance || scale > ScaleTolerance)
                {
                    stale.Add(string.Format(
                        "{0} {1}: 位置 シーン {2} / FBX {3}（差 {4:F3} m）、向きの差 {5:F2} 度、拡大率 シーン {6} / FBX {7}",
                        scene,
                        PathOf(here),
                        here.localPosition.ToString("F3"),
                        there.localPosition.ToString("F3"),
                        gap,
                        angle,
                        here.localScale.ToString("F3"),
                        there.localScale.ToString("F3")));
                }
            }
        }

        /// <summary>FBX の部品を名前で引く表。同じ名前が 2 つ以上あるものは、どれと比べるか決まらないので覚えておく。</summary>
        private sealed class PartsByName<T> where T : Component
        {
            private readonly string _what;
            private readonly Dictionary<string, T> _parts = new Dictionary<string, T>();
            private readonly HashSet<string> _duplicates = new HashSet<string>();

            public PartsByName(string what)
            {
                _what = what;
            }

            public void Add(T part)
            {
                if (_parts.ContainsKey(part.name))
                {
                    _duplicates.Add(part.name);
                }

                _parts[part.name] = part;
            }

            /// <summary>名前で部品を引く。無い名前と、2 つ以上ある名前は、ずれとして出す。</summary>
            public bool TryFind(string name, string where, List<string> stale, out T part)
            {
                part = null;
                if (_duplicates.Contains(name))
                {
                    stale.Add(where + ": FBX に同じ名前の" + _what + "が 2 つ以上あり、比べられない");
                    return false;
                }

                if (!_parts.TryGetValue(name, out part))
                {
                    stale.Add(where + ": FBX に同じ名前の" + _what + "が無い");
                    return false;
                }

                return true;
            }
        }
    }
}
