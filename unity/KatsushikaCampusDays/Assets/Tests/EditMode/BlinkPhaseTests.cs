using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// まばたきがキャラクターどうしで揃わない（#193 タイトルの 3 人が揃って目をつむった）。
    /// ずらし幅の決め方、生成したコントローラの配線、タイトルに焼き込んだ値を見る。
    /// </summary>
    public sealed class BlinkPhaseTests
    {
        private const string AnimatorFolder = "Assets/Generated/Animators";
        private const string TitleScenePath = "Assets/Scenes/Title.unity";

        [Test]
        public void TitleCharacters_NeverBlinkTogether()
        {
            // 並んで見比べられるので、まばたきの幅の 2 倍は離す。
            AssertSpread(GameManager.PlayableCharacterIds, BlinkPhase.BlinkWidth * 2f);
        }

        [Test]
        public void EveryCharacter_BlinksAtItsOwnTime()
        {
            AssertSpread(BlinkPhase.CharacterOrder, BlinkPhase.BlinkWidth);
        }

        [Test]
        public void CharacterOrder_StartsWithTheTitleCharacters()
        {
            string[] title = GameManager.PlayableCharacterIds;
            for (int i = 0; i < title.Length; i++)
            {
                Assert.AreEqual(title[i], BlinkPhase.CharacterOrder[i]);
            }
        }

        [Test]
        public void OffsetFor_StaysInOneCycleAndIsStable()
        {
            foreach (string id in new[] { "mirai", "sora", "someone_new", string.Empty, null })
            {
                float offset = BlinkPhase.OffsetFor(id);
                Assert.That(offset, Is.InRange(0f, 1f), id);
                Assert.Less(offset, 1f, id);
                Assert.AreEqual(offset, BlinkPhase.OffsetFor(id), id + " の値が呼ぶたびに変わる");
            }
        }

        [Test]
        public void CircularDistance_WrapsAroundTheCycle()
        {
            Assert.AreEqual(0.1f, BlinkPhase.CircularDistance(0.95f, 0.05f), 1e-5f);
            Assert.AreEqual(0.5f, BlinkPhase.CircularDistance(0f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, BlinkPhase.CircularDistance(0.3f, 0.3f), 1e-5f);
        }

        [Test]
        public void Offset_WrapsIntoOneCycle()
        {
            var go = new GameObject("BlinkPhaseTest", typeof(Animator));
            try
            {
                var phase = go.AddComponent<BlinkPhase>();
                phase.Offset = 1.25f;
                Assert.AreEqual(0.25f, phase.Offset, 1e-5f);
                phase.Offset = -0.25f;
                Assert.AreEqual(0.75f, phase.Offset, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Apply_PutsTheOffsetIntoTheAnimatorAndPutsItBackAfterAReset()
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base Layer");
            controller.AddParameter(BlinkPhase.ParameterName, AnimatorControllerParameterType.Float);
            controller.layers[0].stateMachine.AddState("Locomotion");

            var go = new GameObject("BlinkPhaseApplyTest");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                if (!animator.isInitialized)
                {
                    Assert.Ignore("EditMode で Animator が初期化されない");
                }

                var phase = go.AddComponent<BlinkPhase>();
                phase.Offset = 0.42f;
                Assert.IsTrue(phase.Apply(animator));
                Assert.AreEqual(0.42f, animator.GetFloat(BlinkPhase.ParameterName), 1e-5f);

                // 無効にしたときと同じく既定値に戻っても、次に確かめたときに入れ直す。
                animator.SetFloat(BlinkPhase.ParameterName, 0f);
                Assert.IsTrue(phase.Apply(animator));
                Assert.AreEqual(0.42f, animator.GetFloat(BlinkPhase.ParameterName), 1e-5f, "戻った値を入れ直さない");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void Apply_SkipsAControllerWithoutTheParameter()
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base Layer");
            controller.layers[0].stateMachine.AddState("Locomotion");

            var go = new GameObject("BlinkPhaseNoParameterTest");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                var phase = go.AddComponent<BlinkPhase>();
                Assert.IsFalse(phase.Apply(animator), "パラメータの無いコントローラに入れようとした");
                Assert.IsFalse(phase.Apply(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void BlinkWidth_MatchesTheIdleClip()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..",
                "blender", "kcd_chara", "anim.py"));
            if (!File.Exists(path))
            {
                Assert.Ignore(path + " が無い");
            }

            Match track = Regex.Match(File.ReadAllText(path), @"""blink_L"":\s*\[(?<keys>[^\]]*)\]");
            Assert.IsTrue(track.Success, "anim.py に blink_L のキーが無い");

            var keys = new List<Vector2>();
            foreach (Match key in Regex.Matches(track.Groups["keys"].Value,
                         @"\(\s*(?<t>[0-9.]+)\s*,\s*(?<v>[0-9.]+)\s*\)"))
            {
                keys.Add(new Vector2(
                    float.Parse(key.Groups["t"].Value, CultureInfo.InvariantCulture),
                    float.Parse(key.Groups["v"].Value, CultureInfo.InvariantCulture)));
            }

            float start = -1f;
            float end = -1f;
            for (int i = 1; i < keys.Count; i++)
            {
                if (start < 0f && keys[i].y > 0f)
                {
                    start = keys[i - 1].x;
                }

                if (start >= 0f && keys[i].y <= 0f)
                {
                    end = keys[i].x;
                    break;
                }
            }

            Assert.Greater(end, start, "まばたきの区間が読めない");
            Assert.LessOrEqual(end - start, BlinkPhase.BlinkWidth + 1e-4f,
                "Idle のまばたきが BlinkWidth より長い（ずらし幅の検査が甘くなる）");
        }

        [Test]
        public void GeneratedControllers_DriveLocomotionCycleOffset()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { AnimatorFolder });
            if (guids.Length == 0)
            {
                Assert.Ignore(AnimatorFolder + " にコントローラがまだ無い");
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                string id = Path.GetFileNameWithoutExtension(path);
                CollectionAssert.Contains(BlinkPhase.CharacterOrder, id, id + " がずらす順番に入っていない");

                bool hasParameter = false;
                foreach (AnimatorControllerParameter parameter in controller.parameters)
                {
                    hasParameter |= parameter.name == BlinkPhase.ParameterName
                                    && parameter.type == AnimatorControllerParameterType.Float;
                }

                Assert.IsTrue(hasParameter, path + " に " + BlinkPhase.ParameterName + " が無い");

                AnimatorState locomotion = FindState(controller, "Locomotion");
                Assert.IsNotNull(locomotion, path + " に Locomotion が無い");
                Assert.IsTrue(locomotion.cycleOffsetParameterActive, path + " の Locomotion の Cycle Offset がパラメータにつながっていない");
                Assert.AreEqual(BlinkPhase.ParameterName, locomotion.cycleOffsetParameter, path);
            }
        }

        [Test]
        public void TitleStands_CarryTheirOwnPhase()
        {
            Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);
            try
            {
                var found = new Dictionary<string, BlinkPhase>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (BlinkPhase phase in root.GetComponentsInChildren<BlinkPhase>(true))
                    {
                        string id = StandIdOf(phase.transform);
                        Assert.IsNotNull(id, phase.name + " が Stand_ の下にない");
                        found[id] = phase;
                    }
                }

                if (found.Count == 0)
                {
                    Assert.Ignore("タイトルのキャラクターが代役（FBX がまだ無い）");
                }

                foreach (string id in GameManager.PlayableCharacterIds)
                {
                    Assert.IsTrue(found.ContainsKey(id), id + " に BlinkPhase が無い（SceneBuilder で組み直す）");
                    Assert.AreEqual(BlinkPhase.OffsetFor(id), found[id].Offset, 1e-5f, id);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertSpread(string[] ids, float minDistance)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                for (int j = i + 1; j < ids.Length; j++)
                {
                    float distance = BlinkPhase.CircularDistance(
                        BlinkPhase.OffsetFor(ids[i]), BlinkPhase.OffsetFor(ids[j]));
                    Assert.GreaterOrEqual(distance, minDistance, ids[i] + " と " + ids[j] + " のまばたきが近い");
                }
            }
        }

        private static string StandIdOf(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith("Stand_"))
                {
                    return t.name.Substring("Stand_".Length);
                }
            }

            return null;
        }

        private static AnimatorState FindState(AnimatorController controller, string name)
        {
            if (controller.layers.Length == 0)
            {
                return null;
            }

            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state != null && child.state.name == name)
                {
                    return child.state;
                }
            }

            return null;
        }
    }
}
