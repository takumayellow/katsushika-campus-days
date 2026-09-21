using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// スモークテストの記録係。プレイヤーログは強制終了で途切れるので、自前のファイルに追記する。
    /// -kcd-log にはファイル名だけを渡す（ディレクトリは Application.persistentDataPath に固定）。
    /// 未指定なら Debug.Log だけ。
    /// </summary>
    public static class SmokeProbe
    {
        private static string _path;

        public static void Open(string fileName)
        {
            _path = null;
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            // 起動引数で任意の場所を上書きさせない: パス区切りを含む名前は拒否する
            if (fileName != System.IO.Path.GetFileName(fileName) || fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                Debug.LogWarning("SmokeProbe: -kcd-log はファイル名だけを受け付けます: " + fileName);
                return;
            }

            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                File.WriteAllText(path, string.Empty);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            _path = path;

            Application.logMessageReceived -= OnLogMessage;
            Application.logMessageReceived += OnLogMessage;
        }

        /// <summary>エラーと例外だけファイルへ残す。プレイヤーログは強制終了で途切れるため。</summary>
        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || string.IsNullOrEmpty(_path))
            {
                return;
            }

            try
            {
                File.AppendAllText(_path, "[" + type + "] " + condition + "\n" + stackTrace + "\n");
            }
            catch (IOException)
            {
                _path = null;
            }
        }

        public static void Log(string message)
        {
            Debug.Log("[KCD] " + message);
            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            try
            {
                File.AppendAllText(_path, message + "\n");
            }
            catch (IOException)
            {
                _path = null;
            }
        }

        /// <summary>Animator と描画の状態を書き出す。T ポーズや真っ黒な顔の原因切り分け用。</summary>
        public static void Dump(string tag)
        {
            var sb = new StringBuilder();
            sb.Append("--- dump ").Append(tag).Append('\n');

            foreach (Animator animator in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                AppendAnimator(sb, animator);
            }

            foreach (SkinnedMeshRenderer renderer in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                AppendRenderer(sb, renderer);
            }

            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                sb.Append("light ").Append(Path(light.transform)).Append(' ').Append(light.type)
                    .Append(" intensity=").Append(light.intensity.ToString("F2"))
                    .Append(" color=").Append(light.color.ToString("F2"))
                    .Append(" dir=").Append(light.transform.forward.ToString("F2"))
                    .Append('\n');
            }

            sb.Append("ambient=").Append(RenderSettings.ambientMode).Append(' ')
                .Append(RenderSettings.ambientLight.ToString("F2")).Append('\n');
            AppendPlacement(sb);
            Log(sb.ToString());
        }

        /// <summary>プレイヤーとカメラの位置、その周りの当たり、UI の有無。</summary>
        private static void AppendPlacement(StringBuilder sb)
        {
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                sb.Append("player pos=").Append(player.transform.position.ToString("F2"))
                    .Append(" yaw=").Append(player.transform.eulerAngles.y.ToString("F0")).Append('\n');
                AppendOverlaps(sb, "nearPlayer", player.transform.position + Vector3.up, 2f);
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                sb.Append("camera pos=").Append(camera.transform.position.ToString("F2"))
                    .Append(" fwd=").Append(camera.transform.forward.ToString("F2")).Append('\n');
                AppendOverlaps(sb, "nearCamera", camera.transform.position, 2f);
                if (Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit,
                        200f, ~0, QueryTriggerInteraction.Ignore))
                {
                    sb.Append("cameraRay hit=").Append(Path(hit.transform))
                        .Append(" dist=").Append(hit.distance.ToString("F2")).Append('\n');
                }

                AppendRenderersOnRay(sb, new Ray(camera.transform.position, camera.transform.forward));
            }

            Minimap minimap = Minimap.Instance;
            sb.Append("minimap=").Append(minimap != null ? Path(minimap.transform) + " active=" + minimap.isActiveAndEnabled : "none")
                .Append('\n');

            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                sb.Append("canvas ").Append(Path(canvas.transform)).Append(" enabled=").Append(canvas.enabled).Append(" children=");
                foreach (Transform child in canvas.transform)
                {
                    sb.Append(child.name).Append(child.gameObject.activeSelf ? "" : "(off)").Append(',');
                }

                sb.Append('\n');
            }
        }

        /// <summary>視線の上にある描画物を近い順に 8 つ。当たり判定が無い物が何かを突き止める用。</summary>
        private static void AppendRenderersOnRay(StringBuilder sb, Ray ray)
        {
            var hits = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, Renderer>>();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.enabled && renderer.bounds.IntersectRay(ray, out float distance) && distance < 300f)
                {
                    hits.Add(new System.Collections.Generic.KeyValuePair<float, Renderer>(distance, renderer));
                }
            }

            hits.Sort((a, b) => a.Key.CompareTo(b.Key));
            sb.Append("renderersOnRay:\n");
            for (int i = 0; i < hits.Count && i < 8; i++)
            {
                Renderer renderer = hits[i].Value;
                sb.Append("  ").Append(hits[i].Key.ToString("F1")).Append(' ').Append(Path(renderer.transform))
                    .Append(" center=").Append(renderer.bounds.center.ToString("F1"))
                    .Append(" size=").Append(renderer.bounds.size.ToString("F1"))
                    .Append(" layer=").Append(LayerMask.LayerToName(renderer.gameObject.layer))
                    .Append(" mat=").Append(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none")
                    .Append('\n');
            }
        }

        private static void AppendOverlaps(StringBuilder sb, string tag, Vector3 center, float radius)
        {
            Collider[] hits = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
            sb.Append(tag).Append(" r=").Append(radius).Append(" :");
            foreach (Collider collider in hits)
            {
                sb.Append(' ').Append(Path(collider.transform))
                    .Append('[').Append(LayerMask.LayerToName(collider.gameObject.layer))
                    .Append(collider.isTrigger ? ",trigger]" : "]");
            }

            sb.Append('\n');
        }

        private static void AppendAnimator(StringBuilder sb, Animator animator)
        {
            bool hasController = animator.runtimeAnimatorController != null;
            AnimatorStateInfo info = hasController ? animator.GetCurrentAnimatorStateInfo(0) : default;
            Transform arm = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            sb.Append("animator ").Append(Path(animator.transform))
                .Append(" enabled=").Append(animator.enabled)
                .Append(" human=").Append(animator.isHuman)
                .Append(" avatarValid=").Append(animator.avatar != null && animator.avatar.isValid)
                .Append(" controller=").Append(hasController ? animator.runtimeAnimatorController.name : "none")
                .Append(" bound=").Append(animator.hasBoundPlayables)
                .Append(" stateLen=").Append(info.length.ToString("F2"))
                .Append(" t=").Append(info.normalizedTime.ToString("F2"))
                .Append(" speedParam=").Append(hasController ? animator.GetFloat("Speed").ToString("F2") : "-")
                .Append(" leftArm=").Append(arm != null ? arm.localEulerAngles.ToString("F0") : "none")
                .Append('\n');
        }

        private static void AppendRenderer(StringBuilder sb, SkinnedMeshRenderer renderer)
        {
            sb.Append("skinned ").Append(Path(renderer.transform))
                .Append(" visible=").Append(renderer.isVisible)
                .Append(" bounds=").Append(renderer.bounds.size.ToString("F2"))
                .Append('\n');

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    sb.Append("  [").Append(i).Append("] null\n");
                    continue;
                }

                bool hasTexture = material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null;
                sb.Append("  [").Append(i).Append("] ").Append(material.name)
                    .Append(" shader=").Append(material.shader.name)
                    .Append(" supported=").Append(material.shader.isSupported)
                    .Append(" queue=").Append(material.renderQueue)
                    .Append(" tex=").Append(hasTexture)
                    .Append(" cull=").Append(material.HasProperty("_Cull") ? material.GetFloat("_Cull").ToString("F0") : "-")
                    .Append(" base=").Append(material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").ToString("F2") : "-")
                    .Append('\n');
            }
        }

        public static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }
    }
}
