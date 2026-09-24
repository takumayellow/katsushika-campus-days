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
        private static readonly int GaitRateHash = Animator.StringToHash("GaitRate");
        private static readonly int WaveHash = Animator.StringToHash("Wave");

        /// <summary>手を振り返すあいだ立ち止まっている時間（秒）。Wave のクリップとほぼ同じ長さ。</summary>
        public const float WaveBackSeconds = 2f;

        /// <summary>プレイヤーが手を振ったとき、振り返してくれる人までの最大距離（m）。</summary>
        public const float WaveReach = 7f;

        private NavMeshAgent _agent;
        private Animator _animator;
        private RuntimeAnimatorController _knownController;
        private bool _hasGaitRate;
        private bool _hasWave;
        private float _waveUntil = -1f;
        private Vector3 _waveToward;
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
                    _agent.isStopped = value || IsWaving;
                }
            }
        }

        /// <summary>手を振り返している最中か。</summary>
        public bool IsWaving => Time.time < _waveUntil;

        /// <summary>
        /// プレイヤーの Q（手を振る）への返事。立ち止まって from の方を向き、手を振り返す。
        /// 会話中（Paused）や、もう振っている最中なら何もしない。
        /// </summary>
        public bool WaveBack(Vector3 from)
        {
            if (_paused || IsWaving)
            {
                return false;
            }

            _waveUntil = Time.time + WaveBackSeconds;
            _waveToward = from;
            if (_navMeshReady)
            {
                _agent.isStopped = true;
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                RefreshAnimatorParameters();
                if (_hasWave)
                {
                    _animator.SetTrigger(WaveHash);
                }
            }

            return true;
        }

        /// <summary>
        /// 手を振ったプレイヤーから見て、振り返す相手を選ぶ。
        /// 前方（左右 90° 以内）で reach 以内のうち、いちばん近い人。いなければ -1。
        /// </summary>
        public static int PickWaveTarget(Vector3 origin, Vector3 forward, System.Collections.Generic.IList<Vector3> others, float reach)
        {
            forward.y = 0f;
            int best = -1;
            float bestSq = reach * reach;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 d = others[i] - origin;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq > bestSq || (sq > 1e-4f && Vector3.Dot(d, forward) <= 0f))
                {
                    continue;
                }

                best = i;
                bestSq = sq;
            }

            return best;
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
        /// NavMesh に乗る。agent はシーンでは止めてあり（NavMeshSurface がデータを登録するより先に
        /// OnEnable が走ると警告が出る, #63）、足もとに NavMesh があるのを確かめてから有効にして Warp する。
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
                RefreshAnimatorParameters();
                float speed = _navMeshReady ? _agent.velocity.magnitude : 0f;
                PlayerAnimatorDriver.SolveGait(speed, out float blend, out float rate);
                if (_hasGaitRate)
                {
                    _animator.SetFloat(GaitRateHash, rate, 0.15f, Time.deltaTime);
                }
                else
                {
                    // GaitRate の無い古いコントローラでは再生速度を掛けられないので、
                    // 歩幅だけ伸ばすと足が前に滑る。素の速さに戻す。
                    blend = speed;
                }

                _animator.SetFloat(SpeedHash, blend, 0.15f, Time.deltaTime);
            }

            if (_waveUntil >= 0f)
            {
                if (IsWaving)
                {
                    FaceToward(_waveToward);
                    return;
                }

                _waveUntil = -1f;
                if (_navMeshReady)
                {
                    _agent.isStopped = _paused;
                }
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

        /// <summary>
        /// Animator の Speed に渡す値。歩きのクリップは 2.6 m/s（PlayerController.DefaultWalkSpeed）に置いてある。
        /// 1.1 m/s の NPC に 2.6 を渡すと脚は振り切れるが足が毎秒 1.5 m 滑るので（#13 の対処の副作用）、
        /// 歩幅と歩調を同じ割合で落とす PlayerAnimatorDriver.SolveGait に一本化した（#43）。
        /// </summary>
        public static float AnimatorSpeed(float agentSpeed, float walkSpeed)
        {
            PlayerAnimatorDriver.SolveGait(agentSpeed, out float blend, out float _);
            return blend;
        }

        private void FaceToward(Vector3 target)
        {
            Vector3 d = target - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
        }

        /// <summary>コントローラが差し替わったときだけパラメータの有無を調べ直す。</summary>
        private void RefreshAnimatorParameters()
        {
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            if (ReferenceEquals(controller, _knownController))
            {
                return;
            }

            _knownController = controller;
            _hasGaitRate = false;
            _hasWave = false;
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                _hasGaitRate |= parameter.name == "GaitRate";
                _hasWave |= parameter.name == "Wave";
            }
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
