using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace KCD
{
    /// <summary>モブの描き方の段。Near は影と輪郭まで、Mid は色だけ、Hidden は描かない。</summary>
    public enum MobTier
    {
        Hidden = 0,
        Mid = 1,
        Near = 2
    }

    /// <summary>
    /// モブの学生 1 人。MobScheduler が枠を貸したときだけ有効になり、道のりを歩き終えたら枠を返す。
    ///
    /// 当たり判定は trigger なのでプレイヤーを押さない。さらに、プレイヤーがすぐ前にいれば横へよけ、
    /// 近づかれたら一歩下がる（<see cref="AvoidanceStep"/>）。
    /// 描き方は MobScheduler が 0.25 秒ごとに決める段（<see cref="SetTier"/>）に従う。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class MobWalker : MonoBehaviour
    {
        /// <summary>1 つの Renderer の、近いとき / 遠いときのマテリアル。</summary>
        [Serializable]
        public sealed class RendererLod
        {
            [SerializeField] private Renderer _renderer;
            [SerializeField] private Material[] _near = Array.Empty<Material>();
            [SerializeField] private Material[] _far = Array.Empty<Material>();
            [SerializeField] private ShadowCastingMode _nearShadows = ShadowCastingMode.On;

            public Renderer Renderer => _renderer;
            public Material[] Near => _near;
            public Material[] Far => _far;
            public ShadowCastingMode NearShadows => _nearShadows;

            public static RendererLod Create(Renderer renderer, Material[] near, Material[] far)
            {
                return new RendererLod
                {
                    _renderer = renderer,
                    _near = near ?? Array.Empty<Material>(),
                    _far = far ?? Array.Empty<Material>(),
                    _nearShadows = renderer != null ? renderer.shadowCastingMode : ShadowCastingMode.On
                };
            }
        }

        /// <summary>道のりの点に着いたとみなす残り距離（m）。</summary>
        public const float ArriveDistance = 0.6f;

        /// <summary>道のりの終わり（入口以外）で、見えなくなるのを待つ最長の秒数。</summary>
        public const float LingerSeconds = 20f;

        /// <summary>1 区間の締め切り = 直線距離 / 速さ × この倍率 + <see cref="LegSlackSeconds"/>。</summary>
        public const float LegTimeFactor = 2f;

        public const float LegSlackSeconds = 8f;

        /// <summary>他の NPC（既定 50）より道を譲る側にする。</summary>
        public const int AvoidancePriority = 70;

        /// <summary>前にいるプレイヤーを横へよけ始める距離（よける半径に掛ける）。</summary>
        public const float LookAheadFactor = 2.5f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GaitRateHash = Animator.StringToHash("GaitRate");
        private static readonly int TalkHash = Animator.StringToHash("Talk");

        [SerializeField] private Animator _animator;
        [SerializeField] private string _variantId = string.Empty;
        [SerializeField] private RendererLod[] _renderers = Array.Empty<RendererLod>();

        private MobScheduler _owner;
        private NavMeshAgent _agent;
        private int _slot = -1;

        private MobSchedule.Route _route;
        private string _bandId = string.Empty;
        private int _next;
        private bool _running;
        private bool _finishing;
        private float _waitUntil = -1f;
        private float _lingerUntil;
        private float _legDeadline;
        private float _heldUntil = -1f;
        private bool _wasHeld;
        private Vector3 _holdToward;

        private MobTier _tier = MobTier.Near;
        private bool _tierDirty = true;
        private bool _stepped;
        private int _step = 1;
        private float _stepAccum;

        private RuntimeAnimatorController _knownController;
        private bool _animatorPrimed;
        private bool _hasGaitRate;
        private bool _hasTalk;
        private bool _talking;

        /// <summary>見た目の組み合わせの id（mob_a など）。</summary>
        public string VariantId => _variantId;

        /// <summary>MobScheduler の枠の番号。</summary>
        public int Slot => _slot;

        /// <summary>いま歩いている時間帯の id（台詞を選ぶのに使う）。</summary>
        public string BandId => _bandId;

        public bool IsRunning => _running;

        public MobTier Tier => _tier;

        /// <summary>話しかけられて立ち止まっている最中か。</summary>
        public bool IsHeld => Time.time < _heldUntil;

        /// <summary>SceneBuilder が見た目を焼き込むときに呼ぶ。</summary>
        public void Configure(Animator animator, string variantId, RendererLod[] renderers)
        {
            _animator = animator;
            _variantId = variantId ?? string.Empty;
            _renderers = renderers ?? Array.Empty<RendererLod>();
        }

        /// <summary>シーンに焼き込んだ Renderer の数（テスト用）。</summary>
        public int RendererCount => _renderers.Length;

        /// <summary>index 番目の Renderer と、近い / 遠いときのマテリアル（テスト用）。</summary>
        public RendererLod RendererAt(int index)
        {
            return index >= 0 && index < _renderers.Length ? _renderers[index] : null;
        }

        /// <summary>MobScheduler が起動時に枠を割り当てる。GameObject は非アクティブのままでよい。</summary>
        public void Attach(MobScheduler owner, int slot)
        {
            _owner = owner;
            _slot = slot;
            _agent = GetComponent<NavMeshAgent>();
            _step = 1;
        }

        /// <summary>
        /// 道のりの nextIndex 番目の点へ向けて、start から歩き出す。
        /// start の近くに NavMesh が無い / agent を乗せられないときは何もせず false。
        /// </summary>
        public bool Begin(MobSchedule.Route route, string bandId, float speed, int nextIndex, Vector3 start)
        {
            if (_owner == null || _agent == null || route == null || nextIndex <= 0 || nextIndex >= route.Waypoints.Count)
            {
                return false;
            }

            if (!NavMesh.SamplePosition(start, out NavMeshHit hit, NPCWander.NavMeshSnapDistance, NavMesh.AllAreas))
            {
                return false;
            }

            _route = route;
            _bandId = bandId ?? string.Empty;
            _next = nextIndex;
            _finishing = false;
            _waitUntil = -1f;
            _heldUntil = -1f;
            _wasHeld = false;
            _stepAccum = 0f;

            transform.position = hit.position;
            if (_owner.TryGetWaypoint(route.Waypoints[nextIndex].Key, out Vector3 target))
            {
                Vector3 look = target - hit.position;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                {
                    transform.rotation = Quaternion.LookRotation(look);
                }
            }

            gameObject.SetActive(true);

            // シーンでは agent を止めてあるので (#63)、足もとの NavMesh を確かめてから有効にして Warp する。
            _agent.enabled = false;
            _agent.enabled = true;
            if (!_agent.isOnNavMesh && !_agent.Warp(hit.position))
            {
                _agent.enabled = false;
                gameObject.SetActive(false);
                return false;
            }

            _agent.speed = Mathf.Max(0.3f, speed);
            _agent.avoidancePriority = AvoidancePriority;
            _agent.isStopped = false;

            // 最初の 1 フレームで Animator を初期化させる。描き方は同じフレームのうちに MobScheduler が決める。
            if (_animator != null)
            {
                _animator.enabled = true;
            }

            // 間引き更新（Animator を止めて手で進める）は、有効な Animator が一度初期化されてから。
            // 出てくるたびに確かめ直すので、非アクティブの間に初期化が解けていても止めたまま進めない。
            _animatorPrimed = false;
            _talking = false;
            if (IsAnimatorReady())
            {
                RefreshAnimatorParameters();
                _animator.SetFloat(SpeedHash, 0f);
                if (_hasTalk)
                {
                    _animator.SetBool(TalkHash, false);
                }
            }

            _tierDirty = true;
            _running = true;
            StartLeg(Time.time);
            return true;
        }

        /// <summary>枠を返す前に呼ぶ。agent を止めて非アクティブにする。</summary>
        public void End()
        {
            _running = false;
            _route = null;
            _finishing = false;
            _waitUntil = -1f;
            _heldUntil = -1f;
            _wasHeld = false;
            ApplyTier(MobTier.Hidden, false);
            _tier = MobTier.Hidden;
            _tierDirty = true;

            if (_agent != null)
            {
                _agent.enabled = false;
            }

            gameObject.SetActive(false);
        }

        /// <summary>話しかけられたとき、seconds 秒だけ立ち止まって toward の方を向く。</summary>
        public void Hold(float seconds, Vector3 toward)
        {
            if (!_running)
            {
                return;
            }

            _heldUntil = Time.time + Mathf.Max(0f, seconds);
            _holdToward = toward;
            if (IsAgentReady())
            {
                _agent.isStopped = true;
            }

            if (_owner != null)
            {
                _owner.RequestTierUpdate();
            }
        }

        /// <summary>
        /// 描き方を切り替える。Near は元の影の設定と輪郭つきのマテリアル、Mid は影なし・輪郭なしのマテリアル、
        /// Hidden は Renderer と Animator を止める。steppedMid が true なら Mid の Animator を止めて
        /// step フレームに 1 回だけ手で進める（遠い人の骨の計算を 1/step にする）。
        /// </summary>
        public void SetTier(MobTier tier, bool steppedMid, int step)
        {
            // isInitialized は Animator を止めると当てにならないので、ここでは一度初期化されたかどうかで決める
            // （止めた Animator で調べると、間引き ↔ 通常の更新を 0.25 秒ごとに行き来しかねない）。
            bool stepped = tier == MobTier.Mid && steppedMid && step > 1 && _animatorPrimed;
            if (tier == _tier && stepped == _stepped && !_tierDirty)
            {
                return;
            }

            _step = Mathf.Max(1, step);
            ApplyTier(tier, stepped);
            _tier = tier;
            _tierDirty = false;
        }

        private void ApplyTier(MobTier tier, bool stepped)
        {
            foreach (RendererLod lod in _renderers)
            {
                if (lod == null || lod.Renderer == null)
                {
                    continue;
                }

                Renderer renderer = lod.Renderer;
                switch (tier)
                {
                    case MobTier.Near:
                        renderer.enabled = true;
                        renderer.shadowCastingMode = lod.NearShadows;
                        if (lod.Near.Length > 0)
                        {
                            renderer.sharedMaterials = lod.Near;
                        }

                        break;
                    case MobTier.Mid:
                        renderer.enabled = true;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        Material[] far = lod.Far.Length == lod.Near.Length && lod.Far.Length > 0 ? lod.Far : lod.Near;
                        if (far.Length > 0)
                        {
                            renderer.sharedMaterials = far;
                        }

                        break;
                    default:
                        renderer.enabled = false;
                        break;
                }
            }

            _stepped = stepped;
            _stepAccum = 0f;
            if (_animator == null)
            {
                return;
            }

            switch (tier)
            {
                case MobTier.Near:
                    _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    _animator.enabled = true;
                    break;
                case MobTier.Mid when !stepped:
                    _animator.cullingMode = AnimatorCullingMode.CullCompletely;
                    _animator.enabled = true;
                    break;
                default:
                    // Mid の間引き更新と Hidden。keepAnimatorStateOnDisable（SceneBuilder が立てる）で状態は残る。
                    _animator.enabled = false;
                    break;
            }
        }

        private void Update()
        {
            if (!_running || _owner == null || !IsAgentReady())
            {
                return;
            }

            float now = Time.time;
            float dt = Time.deltaTime;
            bool held = IsHeld;
            if (held)
            {
                FaceToward(_holdToward, dt);
            }
            else if (_wasHeld)
            {
                _agent.isStopped = _finishing || _waitUntil >= 0f;
            }

            _wasHeld = held;
            if (!held)
            {
                Walk(now);
                if (_running)
                {
                    StepAsideFromPlayer(dt);
                }
            }

            if (_running)
            {
                UpdateAnimator(dt, held);
            }
        }

        private void Walk(float now)
        {
            if (_finishing)
            {
                if (_tier == MobTier.Hidden || now >= _lingerUntil)
                {
                    _owner.OnRouteFinished(this);
                }

                return;
            }

            if (_waitUntil >= 0f)
            {
                if (now >= _waitUntil)
                {
                    _waitUntil = -1f;
                    _agent.isStopped = false;
                    Advance(now);
                }
            }
            else
            {
                bool arrived = !_agent.pathPending && _agent.remainingDistance <= ArriveDistance;
                if (arrived || now >= _legDeadline)
                {
                    Reached(now, arrived);
                }
            }
        }

        /// <summary>歩いていても立ち止まっていても、プレイヤーの行く手と足もとを空ける。</summary>
        private void StepAsideFromPlayer(float dt)
        {
            if (!_owner.TryGetPlayerPosition(out Vector3 player))
            {
                return;
            }

            Vector3 heading = _agent.velocity.sqrMagnitude > 0.01f ? _agent.velocity : transform.forward;
            Vector3 offset = AvoidanceStep(
                transform.position, heading, player, _owner.AvoidPlayerRadius, Mathf.Max(_agent.speed, 1f), dt);
            if (offset.sqrMagnitude > 1e-8f)
            {
                _agent.Move(offset);
            }
        }

        private void Reached(float now, bool arrived)
        {
            MobSchedule.Waypoint waypoint = _route.Waypoints[_next];
            if (arrived && waypoint.Wait > 0f)
            {
                _waitUntil = now + waypoint.Wait;
                _agent.isStopped = true;
                return;
            }

            Advance(now);
        }

        private void Advance(float now)
        {
            _next++;
            if (_next >= _route.Waypoints.Count)
            {
                Finish(now);
                return;
            }

            StartLeg(now);
        }

        private void StartLeg(float now)
        {
            if (!_owner.TryGetWaypoint(_route.Waypoints[_next].Key, out Vector3 target) || !_agent.SetDestination(target))
            {
                // 行き先が引けない / 道が作れない区間は飛ばす（次のフレームで次の点へ）。
                _legDeadline = now;
                return;
            }

            float distance = Vector3.Distance(transform.position, target);
            _legDeadline = now + distance / Mathf.Max(0.3f, _agent.speed) * LegTimeFactor + LegSlackSeconds;
        }

        /// <summary>
        /// 道のりの終わり。建物の入口なら中へ入ったことにしてすぐ消える。
        /// それ以外（門など）は立ち止まり、見えなくなるか <see cref="LingerSeconds"/> 秒たってから消える。
        /// </summary>
        private void Finish(float now)
        {
            MobSchedule.Waypoint last = _route.Waypoints[_route.Waypoints.Count - 1];
            if (last.Kind == MobSchedule.KindEntrance)
            {
                _owner.OnRouteFinished(this);
                return;
            }

            _finishing = true;
            _lingerUntil = now + LingerSeconds;
            _agent.isStopped = true;
        }

        private void UpdateAnimator(float dt, bool held)
        {
            if (_tier == MobTier.Hidden || _animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            if (!_stepped)
            {
                // 有効な Animator は初期化を待つ。初期化できたら、次に段を決め直すときから間引いてよい。
                if (!_animator.isInitialized)
                {
                    return;
                }

                if (!_animatorPrimed)
                {
                    _animatorPrimed = true;
                    _owner.RequestTierUpdate();
                }
            }

            RefreshAnimatorParameters();
            float speed = held ? 0f : _agent.velocity.magnitude;
            PlayerAnimatorDriver.SolveGait(speed, out float blend, out float rate);
            if (_hasGaitRate)
            {
                _animator.SetFloat(GaitRateHash, rate, 0.15f, dt);
            }
            else
            {
                blend = speed;
            }

            _animator.SetFloat(SpeedHash, blend, 0.15f, dt);
            if (_hasTalk && held != _talking)
            {
                _animator.SetBool(TalkHash, held);
                _talking = held;
            }

            if (!_stepped)
            {
                return;
            }

            _stepAccum += dt;
            if ((Time.frameCount + _slot) % _step == 0)
            {
                _animator.Update(_stepAccum);
                _stepAccum = 0f;
            }
        }

        /// <summary>
        /// プレイヤーをよけるための 1 フレームぶんの移動量（xz 平面）。
        /// radius より内側に入られたら真っすぐ離れる。進む先（前方）の radius × <see cref="LookAheadFactor"/> 以内で、
        /// 横のずれが radius 未満にプレイヤーがいれば、プレイヤーと反対の側へ横にずれる。それ以外は 0。
        /// </summary>
        public static Vector3 AvoidanceStep(Vector3 self, Vector3 heading, Vector3 player, float radius, float speed, float dt)
        {
            if (radius <= 0f || speed <= 0f || dt <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 away = self - player;
            away.y = 0f;
            heading.y = 0f;
            float distance = away.magnitude;
            float step = speed * dt;

            if (distance < radius)
            {
                Vector3 direction;
                if (distance > 1e-4f)
                {
                    direction = away / distance;
                }
                else if (heading.sqrMagnitude > 1e-6f)
                {
                    Vector3 h = heading.normalized;
                    direction = new Vector3(h.z, 0f, -h.x);
                }
                else
                {
                    direction = Vector3.right;
                }

                return direction * Mathf.Min(step, radius - distance);
            }

            if (distance > radius * LookAheadFactor || heading.sqrMagnitude < 1e-6f)
            {
                return Vector3.zero;
            }

            Vector3 forward = heading.normalized;
            Vector3 toPlayer = -away;
            if (Vector3.Dot(toPlayer, forward) <= 0f)
            {
                return Vector3.zero;
            }

            var right = new Vector3(forward.z, 0f, -forward.x);
            float lateral = Vector3.Dot(toPlayer, right);
            if (Mathf.Abs(lateral) >= radius)
            {
                return Vector3.zero;
            }

            // プレイヤーが右寄りなら左へ、左寄り（または真正面）なら右へ。
            float side = lateral > 0f ? -1f : 1f;
            return right * (side * step * 0.6f);
        }

        private bool IsAgentReady()
        {
            return _agent != null && _agent.enabled && _agent.isOnNavMesh;
        }

        private bool IsAnimatorReady()
        {
            return _animator != null && _animator.runtimeAnimatorController != null && _animator.isInitialized;
        }

        private void FaceToward(Vector3 target, float dt)
        {
            Vector3 d = target - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * dt);
        }

        /// <summary>コントローラが差し替わったときだけパラメータの有無を調べ直す（NPCWander と同じ）。</summary>
        private void RefreshAnimatorParameters()
        {
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            if (ReferenceEquals(controller, _knownController))
            {
                return;
            }

            _knownController = controller;
            _hasGaitRate = false;
            _hasTalk = false;
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                _hasGaitRate |= parameter.name == "GaitRate";
                _hasTalk |= parameter.name == "Talk";
            }
        }
    }
}
