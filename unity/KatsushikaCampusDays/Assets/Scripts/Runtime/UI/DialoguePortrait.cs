using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 会話ウィンドウの左上に話し手の顔を出す (#11)。会話のあいだだけ小さなカメラで頭のまわりを RenderTexture に描く。
    /// 話し手はプレイヤー（「自分」の行）か、話しかけた NPC。ほかの名前の行・紙に書かれた文・人の形をしていない相手
    /// （掲示板など）では枠を隠す。
    /// </summary>
    public sealed class DialoguePortrait : MonoBehaviour
    {
        /// <summary>行を話している人。</summary>
        public enum Subject
        {
            None,
            Player,
            Npc,
        }

        public const int TextureSize = 256;

        /// <summary>顔の正面からカメラまで（m）。話しかけた相手は 1 m ほど先にいるので、それより手前に置く。</summary>
        public const float Distance = 0.8f;

        /// <summary>真正面から少し横へ回して撮る（度）。</summary>
        public const float Yaw = 20f;

        /// <summary>みらいの目の高さ（Head ボーンから白目の真ん中まで、m）。この高さの人は、Head ボーンの 5 cm 上から縦 26 度で撮る。</summary>
        public const float ReferenceEyeHeight = 0.112f;

        private const float Lift = 0.05f;

        private const float FieldOfView = 26f;

        /// <summary>目の高さから決める倍率の範囲。測り違えても、顔が点になったり目だけが写ったりしない。</summary>
        private const float MinScale = 0.75f;

        private const float MaxScale = 2.5f;

        /// <summary>描く間隔（フレーム）。顔は小さく動きも少ないので、毎フレームは描かない。</summary>
        private const int FrameInterval = 3;

        private static readonly Color Background = new Color(0.12f, 0.2f, 0.28f, 1f);

        [SerializeField] private GameObject _frame;
        [SerializeField] private RawImage _image;
        [SerializeField] private Transform _player;

        private Camera _camera;
        private RenderTexture _texture;
        private DialogueLine _line;
        private Subject _subject;
        private string _dialogueId = string.Empty;
        private Transform _head;
        private Transform _body;

        /// <summary>Head ボーンからカメラまでの高さ（m）。話し手が替わるたびに、その人の目の高さから決める。</summary>
        private float _lift = Lift;

        /// <summary>話し手が替わった。次のフレームは間隔を待たずに描く（前の人の顔を残さない）。</summary>
        private bool _aimed;

        /// <summary>SceneBuilder から差し込む。frame は顔の枠ごとの親、player はプレイヤーの根。</summary>
        public void Bind(GameObject frame, RawImage image, Transform player)
        {
            _frame = frame;
            _image = image;
            _player = player;
        }

        /// <summary>
        /// 行を話している人。Speaker が空か「自分」の行はプレイヤー、話しかけた NPC 本人の名前の行はその NPC。
        /// dialogueId は話しかけた NPC の会話データの id（メモなど NPC の決まらない会話では空）。
        /// </summary>
        public static Subject SubjectOf(DialogueLine line, string dialogueId)
        {
            if (line == null || line.Written)
            {
                return Subject.None;
            }

            if (string.IsNullOrEmpty(line.Speaker) || line.SpeakerKey == DialogueData.SelfSpeakerKey)
            {
                return Subject.Player;
            }

            if (!string.IsNullOrEmpty(dialogueId) && line.SpeakerKey == DialogueData.SpeakerKeyPrefix + dialogueId)
            {
                return Subject.Npc;
            }

            return Subject.None;
        }

        /// <summary>カメラの位置と向き。Head ボーンの lift 上を、体の正面から yaw だけ回した向きの distance 先から水平に見る。</summary>
        public static Pose CameraPose(Vector3 head, Vector3 bodyForward, float distance, float yaw, float lift)
        {
            Vector3 flat = Vector3.ProjectOnPlane(bodyForward, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.forward;
            }

            Vector3 forward = Quaternion.AngleAxis(yaw, Vector3.up) * flat.normalized;
            Vector3 target = head + Vector3.up * lift;
            return new Pose(target + forward * distance, Quaternion.LookRotation(-forward, Vector3.up));
        }

        /// <summary>
        /// 話し手の目の高さに合わせた撮り方。目がみらいの何倍の高さにあるかで、Head ボーンからの持ち上げ量と画角の tan を
        /// 同じ倍率にする。こうすると頭の大きい人も、顎と目が枠の中のほぼ同じ高さに来る。eyeHeight が 0（測れなかった）なら、
        /// みらいと同じ撮り方。
        /// </summary>
        public static void Framing(float eyeHeight, out float lift, out float fieldOfView)
        {
            float scale = eyeHeight > 0f ? Mathf.Clamp(eyeHeight / ReferenceEyeHeight, MinScale, MaxScale) : 1f;
            lift = Lift * scale;
            float halfTan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * scale;
            fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 目の高さ（Head ボーンから白目の真ん中まで、m）。白目の材質のサブメッシュの範囲を、体を組んだときの姿勢（bind pose）の
        /// Head ボーンから測るので、今の姿勢には左右されない。頂点は読まない（キャラの FBX は isReadable: 0 で、製品版では頂点を
        /// 読めない）。白目が無い体では 0。
        /// </summary>
        public static float MeasureEyeHeight(GameObject body, Transform head)
        {
            if (body == null || head == null)
            {
                return 0f;
            }

            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer.sharedMesh;
                // 輪郭シェル（CharacterImporter が無効にして中身を空にしている）は数えない。
                if (!renderer.enabled || mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }

                int slot = EyeWhiteSlot(renderer.sharedMaterials);
                if (slot < 0 || slot >= mesh.subMeshCount)
                {
                    continue;
                }

                int bone = System.Array.IndexOf(renderer.bones, head);
                Matrix4x4[] bindposes = mesh.bindposes;
                if (bone < 0 || bone >= bindposes.Length)
                {
                    continue;
                }

                Bounds eyes = mesh.GetSubMesh(slot).bounds;
                if (eyes.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                // bind pose の逆行列で、体を組んだときの Head ボーンの根元をメッシュの座標に移す。
                Vector3 root = bindposes[bone].inverse.MultiplyPoint3x4(Vector3.zero);
                float height = renderer.transform.TransformVector(eyes.center - root).y;
                if (height > 0f)
                {
                    return height;
                }
            }

            return 0f;
        }

        /// <summary>白目の材質（&lt;id&gt;_eye_white）の番号。無ければ -1。</summary>
        private static int EyeWhiteSlot(Material[] materials)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.EndsWith("_eye_white", System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void LateUpdate()
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            if (dialogue == null || !dialogue.IsPlaying)
            {
                Clear();
                return;
            }

            // 選択肢を出している間は CurrentLine が null。直前の話し手のまま見せておく。
            DialogueLine line = dialogue.CurrentLine;
            if (line != null && line != _line)
            {
                _line = line;
                Subject subject = SubjectOf(line, dialogue.DialogueId);
                if (subject != _subject || dialogue.DialogueId != _dialogueId)
                {
                    _subject = subject;
                    _dialogueId = dialogue.DialogueId;
                    Aim(subject);
                }
            }

            if (_head == null || _body == null)
            {
                Show(false);
                return;
            }

            Show(true);
            Pose pose = CameraPose(_head.position, _body.forward, Distance, Yaw, _lift);
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _camera.enabled = _aimed || Time.frameCount % FrameInterval == 0;
            _aimed = false;
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            if (_camera != null)
            {
                Destroy(_camera.gameObject);
            }

            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void Clear()
        {
            _line = null;
            _subject = Subject.None;
            _dialogueId = string.Empty;
            _head = null;
            _body = null;
            Show(false);
        }

        private void Aim(Subject subject)
        {
            Animator animator = null;
            if (subject == Subject.Player && _player != null)
            {
                animator = _player.GetComponentInChildren<Animator>();
            }
            else if (subject == Subject.Npc)
            {
                NPCTalker talker = NearestTalker(_dialogueId);
                animator = talker != null ? talker.GetComponentInChildren<Animator>() : null;
            }

            _head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            _body = _head != null ? animator.transform : null;
            if (_head != null)
            {
                EnsureCamera();
                Framing(MeasureEyeHeight(animator.gameObject, _head), out _lift, out float fieldOfView);
                _camera.fieldOfView = fieldOfView;
                _aimed = true;
            }
        }

        /// <summary>その会話データを持つ NPC のうち、プレイヤーにいちばん近いもの（同じ id の人が別の場所にもいるとき）。</summary>
        private NPCTalker NearestTalker(string dialogueId)
        {
            NPCTalker nearest = null;
            float best = float.PositiveInfinity;
            Vector3 from = _player != null ? _player.position : Vector3.zero;
            foreach (NPCTalker talker in FindObjectsByType<NPCTalker>(FindObjectsInactive.Exclude))
            {
                if (!talker.isActiveAndEnabled || talker.NpcId != dialogueId)
                {
                    continue;
                }

                float distance = (talker.transform.position - from).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = talker;
                }
            }

            return nearest;
        }

        private void Show(bool visible)
        {
            if (_frame != null && _frame.activeSelf != visible)
            {
                _frame.SetActive(visible);
            }

            if (!visible && _camera != null)
            {
                _camera.enabled = false;
            }
        }

        /// <summary>顔を撮るカメラ。影・ポストプロセス・AA を切り、3 m より先は背景色で塗る。</summary>
        private void EnsureCamera()
        {
            if (_camera != null)
            {
                return;
            }

            _texture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "DialoguePortrait",
            };
            _texture.Create();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _texture;
            GL.Clear(true, true, Background);
            RenderTexture.active = previous;

            // Canvas の子にすると Canvas の大きな拡大率まで受け継ぐので、シーンの直下に置く。
            var rig = new GameObject("DialoguePortraitCamera");
            _camera = rig.AddComponent<Camera>();
            Camera main = Camera.main;
            if (main != null)
            {
                _camera.cullingMask = main.cullingMask;
            }

            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 3f;
            _camera.depth = -50f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.targetTexture = _texture;

            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderType = CameraRenderType.Base;
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }

            if (_image != null)
            {
                _image.texture = _texture;
            }
        }
    }
}
