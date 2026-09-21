using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 待機クリップを 1 フレーム当ててみて、手がどこへ来るかを記録する。
    /// 腰から見た手の高さが肩と同じなら T ポーズのまま（腕のカーブが無い）と分かる。
    /// </summary>
    public static class PoseProbe
    {
        public static void Report()
        {
            foreach (string characterId in GameManager.PlayableCharacterIds)
            {
                Probe(characterId);
            }
        }

        private static void Probe(string characterId)
        {
            string fbxPath = ActorFactory.FbxPathOf(characterId);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
            {
                return;
            }

            AnimationClip idle = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && clip.name.ToLowerInvariant().Contains("idle"))
                {
                    idle = clip;
                }
            }

            GameObject instance = Object.Instantiate(source);
            try
            {
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    EditorPaths.Report("PoseProbe " + characterId + ": Humanoid ではありません");
                    return;
                }

                Describe(characterId + " bind", animator);
                if (idle != null)
                {
                    EditorPaths.Report("PoseProbe " + characterId + " idle curves=" + AnimationUtility.GetCurveBindings(idle).Length
                        + " human=" + idle.isHumanMotion + " len=" + idle.length.ToString("F2"));
                    idle.SampleAnimation(instance, 0.4f);
                    Describe(characterId + " idle@0.4", animator);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void Describe(string tag, Animator animator)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (hips == null || hand == null || shoulder == null)
            {
                EditorPaths.Report("PoseProbe " + tag + ": 骨が足りません");
                return;
            }

            Vector3 handRel = hand.position - hips.position;
            Vector3 shoulderRel = shoulder.position - hips.position;
            EditorPaths.Report(string.Format(
                "PoseProbe {0}: shoulder=({1:F2},{2:F2},{3:F2}) hand=({4:F2},{5:F2},{6:F2}) head.y={7:F2}",
                tag, shoulderRel.x, shoulderRel.y, shoulderRel.z, handRel.x, handRel.y, handRel.z,
                head != null ? head.position.y - hips.position.y : -1f));
        }
    }
}
