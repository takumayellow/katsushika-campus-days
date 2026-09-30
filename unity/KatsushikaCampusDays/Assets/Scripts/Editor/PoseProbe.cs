using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 待機クリップを何か所かの時刻で当ててみて、手がどこへ来るかと腕の角度を記録する。
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
                    foreach (float time in new[] { 0f, 0.4f, 1.25f })
                    {
                        idle.SampleAnimation(instance, time);
                        Describe(characterId + " idle@" + time.ToString("F2"), animator);
                    }
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
            EditorPaths.Report("PoseProbe " + tag + ": left " + ArmAngles(animator, hips, true)
                + " / right " + ArmAngles(animator, hips, false));
        }

        /// <summary>
        /// 上腕・前腕・肩から手首が鉛直から何度開くか（正面から見て外向きが正）と、
        /// 横から見て何度前へ出るか。
        /// </summary>
        private static string ArmAngles(Animator animator, Transform hips, bool left)
        {
            Transform upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null)
            {
                return "骨が足りません";
            }

            Transform body = animator.transform;
            float outSign = Mathf.Sign(Vector3.Dot(upper.position - hips.position, body.right));
            string Angles(Vector3 from, Vector3 to)
            {
                Vector3 d = to - from;
                float down = -Vector3.Dot(d, body.up);
                float side = Mathf.Atan2(outSign * Vector3.Dot(d, body.right), down) * Mathf.Rad2Deg;
                float front = Mathf.Atan2(Vector3.Dot(d, body.forward), down) * Mathf.Rad2Deg;
                return string.Format("{0:F1}/{1:F1}", side, front);
            }

            return "upper=" + Angles(upper.position, lower.position)
                + " fore=" + Angles(lower.position, hand.position)
                + " shoulder-wrist=" + Angles(upper.position, hand.position);
        }
    }
}
