using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// キャラクター FBX に入っているクリップから AnimatorController を組み立てる。
    /// パラメータ名は PlayerAnimatorDriver / NPCWander が参照するものに合わせる。
    /// </summary>
    public static class AnimatorFactory
    {
        public const string AnimatorFolder = "Assets/Generated/Animators";

        private const float WalkSpeed = 2.6f;
        private const float RunSpeed = 5.4f;

        /// <summary>FBX からコントローラを作る。クリップが 1 つも無ければ null。</summary>
        public static AnimatorController EnsureForCharacter(string characterId, string fbxPath)
        {
            string path = AnimatorFolder + "/" + characterId + ".controller";
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                Upgrade(existing);
                return existing;
            }

            Dictionary<string, AnimationClip> clips = CollectClips(fbxPath);
            if (clips.Count == 0)
            {
                return null;
            }

            EditorPaths.EnsureFolder(AnimatorFolder);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AddBool(controller, "Grounded", true);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            AddBool(controller, "Talk", false);
            controller.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
            AddBool(controller, "Sit", false);
            EnableIkPass(controller);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState locomotion = BuildLocomotion(controller, clips);
            machine.defaultState = locomotion;

            AddOneShot(machine, locomotion, clips, "jump", "Jump", 0.85f);
            AddOneShot(machine, locomotion, clips, "wave", "Wave", 0.9f);
            AddTalk(machine, locomotion, clips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            EditorPaths.Report("AnimatorController 生成: " + path + " クリップ " + clips.Count + " 本");
            return controller;
        }

        private static AnimatorState BuildLocomotion(
            AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            AnimationClip idle = Pick(clips, "idle");
            AnimationClip walk = Pick(clips, "walk");
            AnimationClip run = Pick(clips, "run");

            if (idle != null)
            {
                tree.AddChild(idle, 0f);
            }

            if (walk != null)
            {
                tree.AddChild(walk, WalkSpeed);
            }

            if (run != null)
            {
                tree.AddChild(run, RunSpeed);
            }

            if (tree.children.Length == 0 && clips.Count > 0)
            {
                foreach (KeyValuePair<string, AnimationClip> entry in clips)
                {
                    tree.AddChild(entry.Value, 0f);
                    break;
                }
            }

            return state;
        }

        /// <summary>Jump / Wave のような 1 回きりの動作を、トリガーで抜き差しできる形で足す。</summary>
        private static void AddOneShot(
            AnimatorStateMachine machine,
            AnimatorState locomotion,
            Dictionary<string, AnimationClip> clips,
            string clipKey,
            string trigger,
            float exitTime)
        {
            AnimationClip clip = Pick(clips, clipKey);
            if (clip == null)
            {
                return;
            }

            AnimatorState state = machine.AddState(trigger);
            state.motion = clip;

            AnimatorStateTransition enter = locomotion.AddTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0.08f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            AnimatorStateTransition exit = state.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime = exitTime;
            exit.duration = 0.16f;
        }

        /// <summary>会話中は Talk ステートに留まる。</summary>
        private static void AddTalk(
            AnimatorStateMachine machine, AnimatorState locomotion, Dictionary<string, AnimationClip> clips)
        {
            AnimationClip clip = Pick(clips, "talk");
            if (clip == null)
            {
                return;
            }

            AnimatorState state = machine.AddState("Talk");
            state.motion = clip;

            AnimatorStateTransition enter = locomotion.AddTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0.18f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Talk");

            AnimatorStateTransition exit = state.AddTransition(locomotion);
            exit.hasExitTime = false;
            exit.duration = 0.18f;
            exit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Talk");
        }

        /// <summary>既存のコントローラに、後から増えたパラメータと IK パスを足す。</summary>
        private static void Upgrade(AnimatorController controller)
        {
            bool changed = false;
            if (!HasParameter(controller, "Sit"))
            {
                AddBool(controller, "Sit", false);
                changed = true;
            }

            if (EnableIkPass(controller))
            {
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
        }

        private static bool HasParameter(AnimatorController controller, string name)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>SitPose の OnAnimatorIK が呼ばれるように、ベースレイヤの IK Pass を立てる。</summary>
        private static bool EnableIkPass(AnimatorController controller)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length == 0 || layers[0].iKPass)
            {
                return false;
            }

            layers[0].iKPass = true;
            controller.layers = layers;
            return true;
        }

        private static void AddBool(AnimatorController controller, string name, bool defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = defaultValue
            });
        }

        private static AnimationClip Pick(Dictionary<string, AnimationClip> clips, string key)
        {
            foreach (KeyValuePair<string, AnimationClip> entry in clips)
            {
                if (entry.Key.Contains(key))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private static Dictionary<string, AnimationClip> CollectClips(string fbxPath)
        {
            var clips = new Dictionary<string, AnimationClip>();
            if (string.IsNullOrEmpty(fbxPath) || AssetDatabase.LoadAssetAtPath<Object>(fbxPath) == null)
            {
                return clips;
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                string key = clip.name.ToLowerInvariant();
                if (!clips.ContainsKey(key))
                {
                    clips[key] = clip;
                }
            }

            return clips;
        }
    }
}
