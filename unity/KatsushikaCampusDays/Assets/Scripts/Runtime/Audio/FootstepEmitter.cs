using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 足音・ジャンプ・着地。歩いた距離で足音を刻み、足元のマテリアル名から床の種類を決める。
    /// AudioManager がプレイヤーに付ける。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        [SerializeField] private float _walkStride = 1.15f;
        [SerializeField] private float _runStride = 1.55f;
        [SerializeField] private float _probeHeight = 0.5f;
        [SerializeField] private float _probeDepth = 1.6f;

        private PlayerController _player;
        private float _travelled;
        private string _surface = "concrete";
        private float _nextProbeAt;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            _player.Jumped += OnJumped;
            _player.Landed += OnLanded;
        }

        private void OnDisable()
        {
            _player.Jumped -= OnJumped;
            _player.Landed -= OnLanded;
        }

        private void Update()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio == null || !_player.IsGrounded)
            {
                return;
            }

            float speed = _player.PlanarSpeed;
            if (speed < 0.4f)
            {
                _travelled = 0f;
                return;
            }

            if (Time.time >= _nextProbeAt)
            {
                _surface = ProbeSurface();
                _nextProbeAt = Time.time + 0.4f;
            }

            _travelled += speed * Time.deltaTime;
            float stride = _player.IsRunning ? _runStride : _walkStride;
            if (_travelled >= stride)
            {
                _travelled -= stride;
                audio.PlayFootstep(_surface, _player.IsRunning);
            }
        }

        private void OnJumped()
        {
            AudioManager.Instance?.PlaySe("jump", 0.7f);
        }

        private void OnLanded()
        {
            AudioManager.Instance?.PlaySe("land", 0.8f);
            _travelled = 0f;
        }

        /// <summary>足元の描画物のマテリアル名から床を分類する。</summary>
        private string ProbeSurface()
        {
            Vector3 origin = transform.position + Vector3.up * _probeHeight;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _probeDepth, ~0, QueryTriggerInteraction.Ignore))
            {
                return _surface;
            }

            Renderer renderer = hit.collider.GetComponent<Renderer>();
            string material = renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial.name.ToLowerInvariant()
                : string.Empty;
            string objectName = hit.collider.name.ToLowerInvariant();
            return Classify(material, objectName);
        }

        /// <summary>マテリアル名と物の名前から床の種類（concrete / grass / tile / wood）。</summary>
        public static string Classify(string material, string objectName)
        {
            if (material.Contains("grass") || material.Contains("sand") || objectName.Contains("lawn"))
            {
                return "grass";
            }

            if (material.Contains("wood") || material.Contains("parquet") || material.Contains("gym_floor")
                || objectName.Contains("floor_gym"))
            {
                return "wood";
            }

            if (material.Contains("tile") || material.Contains("carpet") || material.Contains("floor")
                || material.Contains("lino") || objectName.StartsWith("floor_"))
            {
                return "tile";
            }

            return "concrete";
        }
    }
}
