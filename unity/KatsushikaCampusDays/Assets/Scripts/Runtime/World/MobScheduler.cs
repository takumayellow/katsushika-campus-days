using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace KCD
{
    /// <summary>
    /// モブの学生を時計に合わせて出し入れする (#22)。
    ///
    /// SceneBuilder（MobStage）が非アクティブの人を <see cref="MaxActive"/> 人ぶん焼き込み、ここはその枠を
    /// 貸し借りするだけで、実行中に Instantiate / Destroy をしない。時間帯（Resources/KCD/Mobs/schedule）の
    /// 人数に届くまで 1 人ずつ出し、多すぎれば見えていない人から帰す。
    ///
    /// 描き方はカメラからの近さの順で決める（<see cref="PickTier"/>）。近い 4 人だけ影と輪郭まで描き、
    /// 次の 4 人は影も輪郭も無しで骨の更新を間引き、残りは描かない。
    /// </summary>
    public sealed class MobScheduler : MonoBehaviour
    {
        /// <summary>同時に出す人数の上限。WebGL の描画予算から決めた値。</summary>
        public const int MaxActive = 24;

        public const int DefaultNearCount = 4;
        public const float DefaultNearDistance = 25f;
        public const int DefaultDrawnCount = 8;
        public const float DefaultDrawnDistance = 45f;

        /// <summary>描き方を決め直す間隔（秒）。</summary>
        public const float TierInterval = 0.25f;

        /// <summary>人数が目標の半分に満たないとき、補充を試す間隔（秒）。</summary>
        public const float FillInterval = 0.2f;

        /// <summary>見えている場所に出すときの、カメラからの最短距離（m）。</summary>
        public const float MinVisibleSpawnDistance = 6f;

        /// <summary>画面の外の人を順位づけで後ろへ回す距離（m）。</summary>
        public const float OutOfViewPenalty = 30f;

        /// <summary>段の境目でちらつかないよう、いま描いている人に足す余裕（m）。</summary>
        public const float Hysteresis = 2f;

        /// <summary>道の途中に出すとき、区間を選び直す回数。</summary>
        public const int FillAttempts = 3;

        private static readonly Vector3 BodyBoundsSize = new Vector3(0.8f, 1.9f, 0.8f);

        [SerializeField] private MobWalker[] _pool = Array.Empty<MobWalker>();
        [SerializeField] private string[] _waypointKeys = Array.Empty<string>();
        [SerializeField] private Vector3[] _waypointPositions = Array.Empty<Vector3>();
        [SerializeField] private int _nearCount = DefaultNearCount;
        [SerializeField] private float _nearDistance = DefaultNearDistance;
        [SerializeField] private int _drawnCount = DefaultDrawnCount;
        [SerializeField] private float _drawnDistance = DefaultDrawnDistance;
        [SerializeField] private bool _steppedMidAnimation = true;
        [SerializeField] private int _midAnimationStep = 3;

        private readonly Dictionary<string, Vector3> _waypoints = new Dictionary<string, Vector3>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly Vector3[] _corners = new Vector3[32];

        private MobSchedule _schedule;
        private MobPool _ledger;
        private NavMeshPath _path;
        private int[] _order = Array.Empty<int>();
        private float[] _keys = Array.Empty<float>();
        private float[] _distances = Array.Empty<float>();

        private MobSchedule.Band _band;
        private int _spawnedThisBand;
        private float _nextSpawnAt;
        private float _nextTierAt;
        private bool _tierDue;

        private Camera _camera;
        private bool _hasPlanes;
        private Transform _player;
        private float _nextPlayerLookupAt;

        /// <summary>焼き込んである人数。</summary>
        public int PoolSize => _pool.Length;

        public MobWalker PoolAt(int index)
        {
            return index >= 0 && index < _pool.Length ? _pool[index] : null;
        }

        /// <summary>SceneBuilder が位置を解決した道のりの点の数。</summary>
        public int WaypointCount => Mathf.Min(_waypointKeys.Length, _waypointPositions.Length);

        public string WaypointKeyAt(int index)
        {
            return index >= 0 && index < WaypointCount ? _waypointKeys[index] : null;
        }

        /// <summary>いま出ている人数。</summary>
        public int ActiveCount => _ledger != null ? _ledger.ActiveCount : 0;

        /// <summary>いまの時間帯で出したい人数（上限で切ったあと）。</summary>
        public int TargetCount { get; private set; }

        /// <summary>プレイヤーからこれより近づかない距離（m）。</summary>
        public float AvoidPlayerRadius => _schedule != null ? _schedule.AvoidPlayerRadius : 1.2f;

        /// <summary>SceneBuilder が焼き込むときに呼ぶ。</summary>
        public void Configure(MobWalker[] pool, string[] waypointKeys, Vector3[] waypointPositions)
        {
            _pool = pool ?? Array.Empty<MobWalker>();
            _waypointKeys = waypointKeys ?? Array.Empty<string>();
            _waypointPositions = waypointPositions ?? Array.Empty<Vector3>();
        }

        /// <summary>道のりの点の位置。SceneBuilder が NavMesh の上に解決してある。</summary>
        public bool TryGetWaypoint(string key, out Vector3 position)
        {
            if (_waypoints.Count == 0)
            {
                IndexWaypoints();
            }

            if (key != null && _waypoints.TryGetValue(key, out position))
            {
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        public bool TryGetPlayerPosition(out Vector3 position)
        {
            if (_player != null)
            {
                position = _player.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>次の Update で描き方を決め直す（話しかけられた人を近い段に上げるため）。</summary>
        public void RequestTierUpdate()
        {
            _tierDue = true;
        }

        /// <summary>道のりを歩き終えた人の枠を返す。</summary>
        public void OnRouteFinished(MobWalker walker)
        {
            if (walker == null || _ledger == null)
            {
                return;
            }

            int slot = walker.Slot;
            walker.End();
            _ledger.Release(slot, Time.time, _schedule != null ? _schedule.RespawnDelaySeconds : 0f);
        }

        /// <summary>
        /// 順位 rank（0 がいちばん優先）と距離から描き方を決める。話しかけられている人は必ず Near。
        /// current はいまの段で、境目でちらつかないよう、いまの段にとどまる側へ <see cref="Hysteresis"/> m 余裕を持たせる。
        /// </summary>
        public static MobTier PickTier(
            int rank, float distance, bool held, MobTier current,
            int nearCount, float nearDistance, int drawnCount, float drawnDistance)
        {
            if (held)
            {
                return MobTier.Near;
            }

            float nearLimit = nearDistance + (current == MobTier.Near ? Hysteresis : 0f);
            if (rank < nearCount && distance <= nearLimit)
            {
                return MobTier.Near;
            }

            float drawnLimit = drawnDistance + (current != MobTier.Hidden ? Hysteresis : 0f);
            if (rank < drawnCount && distance <= drawnLimit)
            {
                return MobTier.Mid;
            }

            return MobTier.Hidden;
        }

        private void Awake()
        {
            _schedule = MobSchedule.LoadFromResources();
            _ledger = new MobPool(_pool.Length, MaxActive);
            _path = new NavMeshPath();
            _order = new int[_pool.Length];
            _keys = new float[_pool.Length];
            _distances = new float[_pool.Length];
            IndexWaypoints();

            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] != null)
                {
                    _pool[i].Attach(this, i);
                    _pool[i].gameObject.SetActive(false);
                }
            }
        }

        private void IndexWaypoints()
        {
            _waypoints.Clear();
            int count = WaypointCount;
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(_waypointKeys[i]))
                {
                    _waypoints[_waypointKeys[i]] = _waypointPositions[i];
                }
            }
        }

        private void Update()
        {
            if (_schedule == null || _ledger == null || _ledger.Capacity == 0)
            {
                return;
            }

            float now = Time.time;
            ResolveViewers(now);

            MobSchedule.Band band = _schedule.BandAt(GameManager.Instance.GameTimeHours);
            if (!ReferenceEquals(band, _band))
            {
                _band = band;
                _spawnedThisBand = 0;
            }

            int target = band != null ? MobSchedule.ClampTarget(band.Count, _ledger.Capacity, _ledger.MaxActive) : 0;
            TargetCount = target;

            if (band != null && _ledger.ActiveCount < target && now >= _nextSpawnAt)
            {
                TrySpawn(band, target, now);
            }

            if (_tierDue || now >= _nextTierAt)
            {
                UpdateTiers(now);
            }
        }

        private void ResolveViewers(float now)
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }

            _hasPlanes = _camera != null;
            if (_hasPlanes)
            {
                GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            }

            if (_player == null && now >= _nextPlayerLookupAt)
            {
                _nextPlayerLookupAt = now + 1f;
                GameObject tagged = GameObject.FindWithTag("Player");
                _player = tagged != null ? tagged.transform : null;
            }
        }

        private Vector3 ViewerPosition()
        {
            if (_camera != null)
            {
                return _camera.transform.position;
            }

            return _player != null ? _player.position : Vector3.zero;
        }

        private bool InView(Vector3 feet)
        {
            return _hasPlanes && GeometryUtility.TestPlanesAABB(_planes, new Bounds(feet + Vector3.up * 0.9f, BodyBoundsSize));
        }

        /// <summary>そこに人が現れてもプレイヤーに気づかれないか（描く距離の外か、画面の外）。</summary>
        private bool IsHiddenSpot(Vector3 feet)
        {
            float distance = Vector3.Distance(feet, ViewerPosition());
            return distance > _drawnDistance || !InView(feet);
        }

        private void TrySpawn(MobSchedule.Band band, int target, float now)
        {
            _nextSpawnAt = now + FillInterval;
            if (!band.Loop && _spawnedThisBand >= band.Count)
            {
                return;
            }

            MobSchedule.Route route = MobSchedule.PickRoute(band, UnityEngine.Random.value);
            if (route == null || !IsResolved(route))
            {
                return;
            }

            // 目標の半分に満たないうち（時間帯の変わり目・読み込み直後）は、見えていない道の途中に出して早く埋める。
            // 途中に見えていない点が無ければ、ふだんどおり道のりの最初の点から出す。
            bool fill = _ledger.ActiveCount * 2 < target;
            Vector3 start = Vector3.zero;
            int next = 1;
            bool midRoute = fill && TryPickMidRoute(route, out start, out next);
            if (!midRoute && !TryPickRouteStart(route, out start, out next))
            {
                return;
            }

            int slot = _ledger.Acquire(now);
            if (slot < 0)
            {
                return;
            }

            MobWalker walker = _pool[slot];
            float speed = band.WalkSpeed * UnityEngine.Random.Range(0.9f, 1.1f);
            if (walker == null || !walker.Begin(route, band.Id, speed, next, start))
            {
                _ledger.Release(slot, now, _schedule.RespawnDelaySeconds);
                return;
            }

            _spawnedThisBand++;
            _tierDue = true;

            // 最初の点（門や入口）から出した人は、続けて湧かないよう間をあける。
            if (!midRoute)
            {
                _nextSpawnAt = now + _schedule.SpawnStaggerSeconds;
            }
        }

        private bool IsResolved(MobSchedule.Route route)
        {
            foreach (MobSchedule.Waypoint waypoint in route.Waypoints)
            {
                if (!TryGetWaypoint(waypoint.Key, out Vector3 _))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 道のりの最初の点から出す。建物の入口と門は、出てきたように見えるので 6 m より遠ければ見えていてもよい。
        /// 座標の点は、何も無いところから湧いて見えるので見えていないときだけ。
        /// </summary>
        private bool TryPickRouteStart(MobSchedule.Route route, out Vector3 start, out int next)
        {
            next = 1;
            MobSchedule.Waypoint first = route.Waypoints[0];
            TryGetWaypoint(first.Key, out start);
            float distance = Vector3.Distance(start, ViewerPosition());
            if (first.IsXz)
            {
                return IsHiddenSpot(start);
            }

            return distance >= MinVisibleSpawnDistance || !InView(start);
        }

        /// <summary>道のりのどこかの区間を選び、NavMesh の道筋の上で見えていない点に出す。次に向かうのはその区間の終点。</summary>
        private bool TryPickMidRoute(MobSchedule.Route route, out Vector3 start, out int next)
        {
            start = Vector3.zero;
            next = 1;
            int segments = route.Waypoints.Count - 1;
            Vector3 viewer = ViewerPosition();
            for (int attempt = 0; attempt < FillAttempts; attempt++)
            {
                int k = UnityEngine.Random.Range(0, segments);
                TryGetWaypoint(route.Waypoints[k].Key, out Vector3 a);
                TryGetWaypoint(route.Waypoints[k + 1].Key, out Vector3 b);
                if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                int count = _path.GetCornersNonAlloc(_corners);
                if (count < 2)
                {
                    continue;
                }

                Vector3 point = PointAlong(_corners, count, UnityEngine.Random.value);
                if (Vector3.Distance(point, viewer) < MinVisibleSpawnDistance || !IsHiddenSpot(point))
                {
                    continue;
                }

                start = point;
                next = k + 1;
                return true;
            }

            return false;
        }

        /// <summary>折れ線 corners[0..count) を長さで t01 の割合だけ進んだ点。</summary>
        public static Vector3 PointAlong(Vector3[] corners, int count, float t01)
        {
            if (corners == null || count <= 0)
            {
                return Vector3.zero;
            }

            float total = 0f;
            for (int i = 1; i < count; i++)
            {
                total += Vector3.Distance(corners[i - 1], corners[i]);
            }

            float remaining = Mathf.Clamp01(t01) * total;
            for (int i = 1; i < count; i++)
            {
                float length = Vector3.Distance(corners[i - 1], corners[i]);
                if (remaining <= length && length > 1e-5f)
                {
                    return Vector3.Lerp(corners[i - 1], corners[i], remaining / length);
                }

                remaining -= length;
            }

            return corners[count - 1];
        }

        /// <summary>
        /// 出ている人をカメラからの近さ（画面の外は +30 m、いま描いている人は -2 m、話し中は最優先）で並べ、
        /// 上から Near / Mid / Hidden を割り振る。人数が目標より多ければ、描いていない人を 1 人ずつ帰す。
        /// </summary>
        private void UpdateTiers(float now)
        {
            _tierDue = false;
            _nextTierAt = now + TierInterval;

            Vector3 viewer = ViewerPosition();
            int n = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                MobWalker walker = _pool[i];
                if (walker == null || !_ledger.IsActive(i))
                {
                    continue;
                }

                Vector3 p = walker.transform.position;
                float distance = Vector3.Distance(p, viewer);
                float key;
                if (walker.IsHeld)
                {
                    key = -1f;
                }
                else
                {
                    key = distance + (InView(p) ? 0f : OutOfViewPenalty);
                    if (walker.Tier != MobTier.Hidden)
                    {
                        key -= Hysteresis;
                    }
                }

                // 挿入ソート（最大 24 人なので十分速く、確保も起きない）。
                int j = n;
                while (j > 0 && _keys[j - 1] > key)
                {
                    _keys[j] = _keys[j - 1];
                    _order[j] = _order[j - 1];
                    _distances[j] = _distances[j - 1];
                    j--;
                }

                _keys[j] = key;
                _order[j] = i;
                _distances[j] = distance;
                n++;
            }

            for (int rank = 0; rank < n; rank++)
            {
                MobWalker walker = _pool[_order[rank]];
                MobTier tier = PickTier(
                    rank, _distances[rank], walker.IsHeld, walker.Tier,
                    _nearCount, _nearDistance, _drawnCount, _drawnDistance);
                walker.SetTier(tier, _steppedMidAnimation, _midAnimationStep);
            }

            if (_ledger.ActiveCount <= TargetCount)
            {
                return;
            }

            for (int rank = n - 1; rank >= 0; rank--)
            {
                MobWalker walker = _pool[_order[rank]];
                if (walker.Tier == MobTier.Hidden && !walker.IsHeld)
                {
                    OnRouteFinished(walker);
                    return;
                }
            }
        }
    }
}
