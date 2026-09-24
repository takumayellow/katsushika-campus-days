using UnityEngine;

namespace KCD
{
    /// <summary>
    /// スモーク用の顔クローズアップ。-kcd-face-cam &lt;距離m&gt; があれば、画面中央に最も近い
    /// Humanoid の頭の正面へメインカメラを置いて固定する（Cinemachine は止める）。
    /// </summary>
    public static class SmokeFaceCam
    {
        public static void Apply(float distance)
        {
            if (float.IsNaN(distance) || distance <= 0f)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                SmokeProbe.Log("FaceCam: no main camera");
                return;
            }

            Animator target = null;
            Transform head = null;
            float bestScore = float.MaxValue;
            foreach (Animator animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
            {
                if (!animator.isHuman || !animator.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Transform bone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (bone == null)
                {
                    continue;
                }

                Vector3 toHead = bone.position - camera.transform.position;
                float score = Vector3.Angle(camera.transform.forward, toHead) + toHead.magnitude * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    target = animator;
                    head = bone;
                }
            }

            if (head == null)
            {
                SmokeProbe.Log("FaceCam: no humanoid head");
                return;
            }

            foreach (Behaviour behaviour in camera.GetComponents<Behaviour>())
            {
                if (behaviour != camera && behaviour.GetType().Name.Contains("Cinemachine"))
                {
                    behaviour.enabled = false;
                }
            }

            Vector3 forward = target.transform.forward;
            Vector3 eyes = head.position + Vector3.up * 0.05f;
            camera.transform.SetPositionAndRotation(
                eyes + forward * distance,
                Quaternion.LookRotation(-forward, Vector3.up));
            camera.fieldOfView = 24f;
            camera.nearClipPlane = 0.02f;
            SmokeProbe.Log("FaceCam: " + target.name + " dist=" + distance);
        }
    }
}
