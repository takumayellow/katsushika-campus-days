using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// Blender で付けた髪の揺れ（HairFront / HairBack）が Unity のクリップでも再生される（#218）。
    /// Humanoid のクリップはマスクに無い Humanoid 外の骨の曲線を捨てるので、取り込みでマスクに全部の骨を入れている
    /// （CharacterImporter.OnPreprocessAnimation）。落ちたら KCD/シーンを組み直す（SyncSkeletons が取り込み直す）。
    /// </summary>
    public sealed class CharacterHairSwayTests
    {
        private const string CharactersFolder = "Assets/Models/Characters";
        private static readonly string[] HairBones = { "HairFront", "HairBack" };

        [Test]
        public void EveryClip_KeepsTheHairCurves()
        {
            var missing = new List<string>();
            foreach (string path in CharacterModels())
            {
                foreach (AnimationClip clip in ClipsOf(path))
                {
                    var bound = new HashSet<string>(AnimationUtility.GetCurveBindings(clip)
                        .Where(b => b.propertyName.StartsWith("m_LocalRotation"))
                        .Select(b => b.path.Split('/').Last()));
                    foreach (string bone in HairBones.Where(b => !bound.Contains(b)))
                    {
                        missing.Add(path + " " + clip.name + " " + bone);
                    }
                }
            }

            Assert.IsEmpty(missing, "髪の揺れの曲線がクリップに無い:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Run_SwingsTheHair()
        {
            string path = CharacterModels().FirstOrDefault(p => p.EndsWith("/mirai.fbx"));
            if (path == null)
            {
                Assert.Ignore("mirai の FBX がまだ無い");
            }

            AnimationClip run = ClipsOf(path).FirstOrDefault(c => c.name.EndsWith("Run"));
            Assert.IsNotNull(run, "mirai に Run のクリップが無い");
            var body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                Transform hair = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "HairBack");
                Assert.IsNotNull(hair, "mirai に HairBack の骨が無い");
                float widest = 0f;
                run.SampleAnimation(body, 0f);
                Quaternion first = hair.localRotation;
                for (int i = 1; i <= 8; i++)
                {
                    run.SampleAnimation(body, run.length * i / 8f);
                    widest = Mathf.Max(widest, Quaternion.Angle(first, hair.localRotation));
                }

                // 曲線を捨てていたときは 0°。Blender の Run では十数度振れる。
                Assert.Greater(widest, 5f, "走っても後ろ髪が揺れない");
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }

        private static IEnumerable<string> CharacterModels()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharactersFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx")
                    && AssetImporter.GetAtPath(path) is ModelImporter importer
                    && importer.animationType == ModelImporterAnimationType.Human
                    && importer.importAnimation)
                {
                    yield return path;
                }
            }
        }

        private static IEnumerable<AnimationClip> ClipsOf(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"));
        }
    }
}
