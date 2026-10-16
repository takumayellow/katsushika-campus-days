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
        private const float Distance = 0.8f;

        /// <summary>真正面から少し横へ回して撮る（度）。</summary>
        private const float Yaw = 20f;

        private const float FieldOfView = 26f;

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

        /// <summary>カメラの位置と向き。頭の少し上を、体の正面から yaw だけ回した向きの distance 先から見る。</summary>
        public static Pose CameraPose(Vector3 head, Vector3 bodyForward, float distance, float yaw)
        {
            Vector3 flat = Vector3.ProjectOnPlane(bodyForward, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.forward;
            }

            Vector3 forward = Quaternion.AngleAxis(yaw, Vector3.up) * flat.normalized;
            Vector3 eyes = head + Vector3.up * 0.05f;
            return new Pose(eyes + forward * distance, Quaternion.LookRotation(-forward, Vector3.up));
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
            Pose pose = CameraPose(_head.position, _body.forward, Distance, Yaw);
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
            _camera.fieldOfView = FieldOfView;
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
