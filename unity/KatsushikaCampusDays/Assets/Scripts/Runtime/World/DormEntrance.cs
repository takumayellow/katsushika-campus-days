using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 葛飾コミュニティハウス（寮）の玄関 (#41)。扉の前で E を押すか、扉へ向かって歩くと中へ入る。
    ///
    /// <see cref="EntranceTrigger"/> と作りはほぼ同じだが、わざと別のクラスにしてある。
    /// EntranceTrigger は入ったことを <c>Quests.ReportEnter</c> と <c>DayStats.NoteEnter</c> へ必ず報告するので、
    /// 使い回すと寮に入っただけで探索率と「入った建物」が伸びてしまう。寮は裏の場所で、
    /// 本編の集計には一切混ぜない約束なので、報告しない入口をここに用意した。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DormEntrance : Interactable
    {
        /// <summary>E に反応する距離（扉の面からの水平距離, m）。EntranceTrigger と同じ。</summary>
        public const float DefaultRange = 4.5f;

        /// <summary>判定箱の中心（ローカル）。扉の面から外へ 1.6 m、中へ 0.4 m。</summary>
        public static readonly Vector3 BoxCenter = new Vector3(0f, 1.5f, 0.6f);

        /// <summary>判定箱の大きさ（ローカル）。</summary>
        public static readonly Vector3 BoxSize = new Vector3(4f, 3f, 2f);

        /// <summary>歩いて入る範囲の奥行き（扉の面から外へ, m）。</summary>
        public const float WalkInDepth = 1.0f;

        /// <summary>歩いて入る範囲の半幅（m）。ふつうの入口と同じ。</summary>
        public const float WalkInHalfWidth = 1.2f;

        /// <summary>出てから次に入れるまでの間（秒）。EntranceTrigger の 3 秒に合わせる。</summary>
        public const float ReenterGuard = 3f;

        [SerializeField] private string _buildingId = DormRoute.Id;
        [SerializeField] private string _displayName = DormRoute.DisplayName;
        [SerializeField] private float _reentryCooldown = 2f;
        [SerializeField] private float _walkInHalfWidth = WalkInHalfWidth;

        [Tooltip("歩いて入るとき、扉の方をどれだけ向いていればよいか（内積）。横切るだけでは入らない。")]
        [SerializeField] private float _walkInFacing = 0.5f;

        private float _lastEnteredAt = -999f;
        private Transform _visitor;
        private bool _walkInArmed = true;
        private BoxCollider _box;

        /// <summary>建物 id。既定は <see cref="DormRoute.Id"/>。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        /// <summary>辞書に無いときの表示名。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>建物の外を指す水平な向き（この Transform の正面）。</summary>
        public Vector3 Outward
        {
            get
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            }
        }

        /// <summary>いまの言語での建物名。</summary>
        public string BuildingName
        {
            get
            {
                string fallback = string.IsNullOrEmpty(_displayName) ? _buildingId : _displayName;
                return L.Get("ui.building." + _buildingId, fallback);
            }
        }

        /// <summary>プロンプトは扉の上に出す。</summary>
        public override Vector3 PromptAnchor => transform.position + Vector3.up * 2.0f;

        /// <summary>屋内にいる間・出た直後・入った直後は反応しない。</summary>
        public override bool CanInteract => isActiveAndEnabled && IsOpen();

        // ---- 純関数（Unity を起動せずにテストする）----

        /// <summary>
        /// 入れる状態か。屋内にいる間は入れず、外へ出た直後も <see cref="ReenterGuard"/> 秒は入れない。
        /// insideNow は InteriorLoader.IsInside、secondsSinceExit は Time.time - LastExitAt。
        /// </summary>
        public static bool CanEnter(bool insideNow, float secondsSinceExit)
        {
            return !insideNow && secondsSinceExit >= ReenterGuard;
        }

        /// <summary>扉のローカル座標で、歩いて入る範囲の中にいるか。</summary>
        public static bool InWalkInZone(Vector3 local, float halfWidth)
        {
            return halfWidth > 0f && local.z < WalkInDepth && Mathf.Abs(local.x) <= halfWidth;
        }

        /// <summary>扉の方を向いているか。outward は建物から外を指す水平な向き。</summary>
        public static bool FacesDoor(Vector3 facing, Vector3 outward, float minDot)
        {
            facing.y = 0f;
            outward.y = 0f;
            if (facing.sqrMagnitude < 1e-4f || outward.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            return Vector3.Dot(facing.normalized, -outward.normalized) >= minDot;
        }

        /// <summary>入ったあとに立つ向き（度）。扉の正面（建物の中）を向く。</summary>
        public static float InwardYaw(Vector3 outward)
        {
            Vector3 inward = -outward;
            inward.y = 0f;
            if (inward.sqrMagnitude < 1e-4f)
            {
                return 0f;
            }

            return Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        }

        // ---- MonoBehaviour ----

        /// <summary>E が押された。</summary>
        public override void Interact(GameObject interactor)
        {
            TryEnter(interactor);
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = BoxCenter;
            box.size = BoxSize;
            InteractionRange = DefaultRange;
        }

        private void OnEnable()
        {
            RefreshLabel();
            L.LocaleChanged += RefreshLabel;
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshLabel;
            _visitor = null;
        }

        /// <summary>プロンプトの文言を「◯◯に入る」に作り直す。言語を切り替えたときにも呼ばれる。</summary>
        public void RefreshLabel()
        {
            string name = BuildingName;
            PromptLabel = L.Pick(name + "に入る", "Enter " + name);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _visitor = other.transform;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_visitor == other.transform)
            {
                _visitor = null;
                _walkInArmed = true;
            }
        }

        /// <summary>
        /// 歩いて入る判定。トリガーは「近くに来た」ことを知るためだけに使い、
        /// 入るかどうかは扉からの位置と向きで決める（テレポートで OnTriggerExit が来なくても崩れない）。
        /// </summary>
        private void Update()
        {
            if (_visitor == null)
            {
                return;
            }

            if (_box == null)
            {
                _box = GetComponent<BoxCollider>();
            }

            Vector3 size = _box != null ? _box.size : BoxSize;
            Vector3 local = transform.InverseTransformPoint(_visitor.position);
            bool near = local.z > -1f && local.z < size.z + 2f && Mathf.Abs(local.x) < size.x;
            if (!near)
            {
                _visitor = null;
                _walkInArmed = true;
                return;
            }

            if (!InWalkInZone(local, _walkInHalfWidth))
            {
                _walkInArmed = true;
                return;
            }

            if (!_walkInArmed || KCDInput.GameplayBlocked || KCDInput.MovementLocked || !IsOpen())
            {
                return;
            }

            if (!FacesDoor(_visitor.forward, Outward, _walkInFacing))
            {
                return;
            }

            TryEnter(_visitor.gameObject);
        }

        private bool IsOpen()
        {
            if (Time.time - _lastEnteredAt < _reentryCooldown)
            {
                return false;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null)
            {
                return false;
            }

            return CanEnter(loader.IsInside, Time.time - loader.LastExitAt);
        }

        private void TryEnter(GameObject interactor)
        {
            if (!IsOpen())
            {
                return;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null || !loader.HasInterior(_buildingId))
            {
                // 寮の屋内が組まれていない（route.fbx / dorm.fbx 未着）。黙って何もしないと理由が分からないので一言出す。
                HUD.Instance?.ShowToast(L.Pick("鍵が掛かっている。", "The door is locked."));
                return;
            }

            _lastEnteredAt = Time.time;
            _walkInArmed = false;

            // 扉の正面を向かせてから入る。InteriorLoader は「入ったときの向きの 2 m 後ろ」に戻すので、
            // 横を向いたまま E を押しても、出てきたときに扉の真正面へ戻る。
            PlayerController player = interactor != null ? interactor.GetComponentInParent<PlayerController>() : null;
            if (player != null)
            {
                player.Teleport(player.transform.position, InwardYaw(Outward));
            }

            // クエストと DayStats へは報告しない。寮は本編の集計に混ぜない (#41)。
            loader.Enter(_buildingId);
        }
    }
}
