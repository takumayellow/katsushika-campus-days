using UnityEngine;
using UnityEngine.AI;

namespace KCD
{
    /// <summary>
    /// NPC をホーム地点のまわりでのんびり歩かせる。
    /// NavMesh が焼かれていない状況（FBX 未着など）では自動的に立ち止まるだけになり、例外を出さない。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NPCWander : MonoBehaviour
    {
        [SerializeField] private float _radius = 9f;
        [SerializeField] private float _minIdleSeconds = 2.5f;
        [SerializeField] private float _maxIdleSeconds = 7f;
        [SerializeField] private float _walkSpeed = 1.1f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private NavMeshAgent _agent;
        private Animator _animator;
        private Vector3 _home;
        private float _nextDecisionAt;
        private bool _navMeshReady;
        private bool _paused;
        private float _retryUntil;

        /// <summary>NavMesh が後から登録される場合に備えて乗り直しを試みる秒数。</summary>
        public const float NavMeshRetrySeconds = 5f;

        /// <summary>配置位置から NavMesh を探す距離（m）。段差や縁石の上に置かれても拾えるように少し広め。</summary>
        public const float NavMeshSnapDistance = 3f;

        /// <summary>会話中など、歩みを止めたいとき true にする。</summary>
        public bool Paused
        {
            get => _paused;
            set
            {
                _paused = value;
                if (_navMeshReady)
                {
                    _agent.isStopped = value;
                }
            }
        }

        /// <summary>徘徊の中心。SceneBuilder が配置直後に設定する。</summary>
        public Vector3 Home
        {
            get => _home;
            set => _home = value;
        }

        /// <summary>徘徊半径（m）。</summary>
        public float Radius
        {
            get => _radius;
            set => _radius = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.speed = _walkSpeed;
            _agent.angularSpeed = 220f;
            _agent.acceleration = 6f;
            _agent.stoppingDistance = 0.4f;
            _animator = GetComponentInChildren<Animator>();
            _home = transform.position;
        }

        private void Start()
        {
            _retryUntil = Time.time + NavMeshRetrySeconds;
            TryAttachToNavMesh();
            _nextDecisionAt = Time.time + Random.Range(0f, _maxIdleSeconds);
        }

        /// <summary>
        /// NavMesh に乗り直す。NavMeshSurface が OnEnable でデータを登録するより先に
        /// NavMeshAgent の OnEnable が走ると agent が作られないので、作り直して足元へ Warp する。
        /// </summary>
        private void TryAttachToNavMesh()
        {
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                _navMeshReady = true;
                return;
            }

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, NavMeshSnapDistance, NavMesh.AllAreas))
            {
                _agent.enabled = false;
                return;
            }

            _agent.enabled = false;
            _agent.enabled = true;
            _navMeshReady = _agent.isOnNavMesh || _agent.Warp(hit.position);
            if (!_navMeshReady)
            {
                _agent.enabled = false;
                return;
            }

            _home = transform.position;
            _agent.isStopped = _paused;
        }

        private void Update()
        {
            if (!_navMeshReady && Time.time < _retryUntil)
            {
                TryAttachToNavMesh();
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                float speed = _navMeshReady ? _agent.velocity.magnitude : 0f;
                _animator.SetFloat(SpeedHash, speed, 0.15f, Time.deltaTime);
            }

            if (!_navMeshReady || _paused || _radius <= 0.1f)
            {
                return;
            }

            bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance;
            if (!arrived || Time.time < _nextDecisionAt)
            {
                return;
            }

            if (TryPickDestination(out Vector3 destination))
            {
                _agent.SetDestination(destination);
            }

            _nextDecisionAt = Time.time + Random.Range(_minIdleSeconds, _maxIdleSeconds);
        }

        private bool TryPickDestination(out Vector3 destination)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * _radius;
                var candidate = new Vector3(_home.x + offset.x, _home.y, _home.z + offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    destination = hit.position;
                    return true;
                }
            }

            destination = _home;
            return false;
        }
    }
}
