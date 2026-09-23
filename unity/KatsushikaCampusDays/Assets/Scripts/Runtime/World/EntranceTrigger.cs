using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 建物の入口。FBX の door_&lt;id&gt; Empty（玄関の扉の外面の中心）に置き、正面（forward）を建物の外へ向ける。
    /// 扉の前で E を押す（プロンプトは「◯◯に入る」）か、扉へ向かって歩いて扉の前
    /// （奥行き <see cref="WalkInDepth"/> m 以内）まで来ると屋内へ入る (#39)。
    /// 入ったことはクエストと DayStats へ報告する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class EntranceTrigger : Interactable
    {
        /// <summary>E に反応する距離（扉の面からの水平距離, m）。</summary>
        public const float DefaultRange = 4.5f;

        /// <summary>判定箱の中心（ローカル）。扉の面から外へ 1.6 m、中へ 0.4 m。</summary>
        public static readonly Vector3 BoxCenter = new Vector3(0f, 1.5f, 0.6f);

        /// <summary>判定箱の大きさ（ローカル）。幅 4 m は風除室の外形（4.8 m）より狭い。ふつうの入口の値。</summary>
        public static readonly Vector3 BoxSize = new Vector3(4f, 3f, 2f);

        /// <summary>歩いて入る範囲の奥行き（扉の面から外へ, m）。ガラス扉に当たって止まる位置より少し外まで。</summary>
        public const float WalkInDepth = 1.0f;

        // 入口の寸法（blender/kcd_lib/entrances.py の STANDARD / SMALL と同じ, 半幅 m）。
        // Opening は扉の開口の半幅 (W)、Outer は風除室の外形の半幅 (WO)。

        /// <summary>ふつうの入口の開口の半幅（開口 3.2 m）。</summary>
        public const float StandardOpeningHalfWidth = 1.6f;

        /// <summary>ふつうの入口の風除室の外形の半幅（外形 4.8 m）。</summary>
        public const float StandardOuterHalfWidth = 2.4f;

        /// <summary>小さい入口（温室）の開口の半幅（開口 1.8 m）。</summary>
        public const float SmallOpeningHalfWidth = 0.9f;

        /// <summary>小さい入口（温室）の風除室の外形の半幅（外形 3.0 m）。</summary>
        public const float SmallOuterHalfWidth = 1.5f;

        /// <summary>歩き入りの範囲と判定箱を、開口・外形の端からどれだけ内側に寄せるか（m）。</summary>
        public const float EdgeMargin = 0.4f;

        /// <summary>小さい入口（entrances.py の SMALL）を使う建物か。いまは温室だけ。</summary>
        public static bool IsSmallDoor(string buildingId)
        {
            return buildingId == "greenhouse";
        }

        /// <summary>
        /// 建物ごとの歩き入りの半幅。開口の半幅から <see cref="EdgeMargin"/> を引く。
        /// ふつうは 1.2 m、温室は 0.5 m（プレイヤーの半径 0.28 m を足しても開口 0.9 m に収まる）。
        /// </summary>
        public static float WalkInHalfWidthFor(string buildingId)
        {
            float opening = IsSmallDoor(buildingId) ? SmallOpeningHalfWidth : StandardOpeningHalfWidth;
            return opening - EdgeMargin;
        }

        /// <summary>
        /// 建物ごとの判定箱の大きさ。幅は風除室の外形から両側 <see cref="EdgeMargin"/> ずつ狭める
        /// （ふつうは 4.0 m、温室は 2.2 m）。高さと奥行きは共通。
        /// </summary>
        public static Vector3 BoxSizeFor(string buildingId)
        {
            float outer = IsSmallDoor(buildingId) ? SmallOuterHalfWidth : StandardOuterHalfWidth;
            return new Vector3(2f * (outer - EdgeMargin), BoxSize.y, BoxSize.z);
        }

        [SerializeField] private string _buildingId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private float _reentryCooldown = 2f;

        [Tooltip("歩いて入る範囲の半幅（m）。扉の開口より内側にしておく。0 以下で歩き入りを切る。")]
        [SerializeField] private float _walkInHalfWidth = 1.2f;

        [Tooltip("歩いて入るとき、扉の方をどれだけ向いていればよいか（内積）。横切るだけでは入らない。")]
        [SerializeField] private float _walkInFacing = 0.5f;

        private float _lastEnteredAt = -999f;
        private Transform _visitor;
        private bool _walkInArmed = true;
        private BoxCollider _box;

        /// <summary>campus.json の建物 id。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        /// <summary>トーストとプロンプトに出す表示名（辞書に無いときの控え）。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>歩いて入る範囲の半幅（m）。シーンを作るときに <see cref="WalkInHalfWidthFor"/> で入口ごとに決める。</summary>
        public float WalkInHalfWidth
        {
            get => _walkInHalfWidth;
            set => _walkInHalfWidth = value;
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

        /// <summary>プロンプトは扉の上（庇の下）に出す。</summary>
        public override Vector3 PromptAnchor => transform.position + Vector3.up * 2.0f;

        /// <summary>屋内にいる間・出た直後・入った直後は反応しない。</summary>
        public override bool CanInteract => isActiveAndEnabled && IsOpen();

        /// <summary>プロンプトの文言を「◯◯に入る」に作り直す。言語を切り替えたときにも呼ばれる。</summary>
        public void RefreshLabel()
        {
            string name = BuildingName;
            PromptLabel = L.Pick(name + "に入る", "Enter " + name);
        }

        /// <summary>E が押された。</summary>
        public override void Interact(GameObject interactor)
        {
            TryEnter(interactor);
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            // すでに大きさが入っている箱は触らない。Reset() は AddComponent した瞬間にも呼ばれるので、
            // 無条件に代入すると CampusProps が入口ごとに入れた幅（温室は開口 1.8 m ぶんに狭めている）を
            // 巻き戻してしまう（#54）。
            if (VisitZone.ShouldApplyDefaultSize(box.size))
            {
                box.center = BoxCenter;
                box.size = BoxSize;
            }

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
        /// 歩いて入る判定。物理のトリガーは「近くに来た」ことを知るだけに使い、
        /// 入るかどうかは扉からの位置と向きで決める（テレポートで OnTriggerExit が来なくても崩れない）。
        /// </summary>
        private void Update()
        {
            if (_visitor == null)
            {
                return;
            }

            // 箱の大きさは入口ごとに違う（温室は狭い）ので、置かれた BoxCollider の値を使う。
            if (_box == null)
            {
                _box = GetComponent<BoxCollider>();
            }

            Vector3 size = _box != null ? _box.size : BoxSize;
            Vector3 local = transform.InverseTransformPoint(_visitor.position);
            bool near = local.z > -1f && local.z < size.z + 2f && Mathf.Abs(local.x) < size.x;
            if (!near)
            {
                // 屋内への移動などで箱の外へ飛んだ。OnTriggerExit の代わり。
                _visitor = null;
                _walkInArmed = true;
                return;
            }

            bool inZone = local.z < WalkInDepth && Mathf.Abs(local.x) <= _walkInHalfWidth;
            if (!inZone)
            {
                _walkInArmed = true;
                return;
            }

            if (!_walkInArmed || _walkInHalfWidth <= 0f || KCDInput.GameplayBlocked || KCDInput.MovementLocked
                || !IsOpen())
            {
                return;
            }

            Vector3 facing = _visitor.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f || Vector3.Dot(facing.normalized, -Outward) < _walkInFacing)
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
            return loader == null || (!loader.IsInside && Time.time - loader.LastExitAt >= 3f);
        }

        private void TryEnter(GameObject interactor)
        {
            if (!IsOpen())
            {
                return;
            }

            _lastEnteredAt = Time.time;
            _walkInArmed = false;

            // 扉の正面を向かせてから入る。InteriorLoader は「入ったときの向きの 2 m 後ろ」に戻すので、
            // 横を向いたまま E を押しても、出てきたときに自販機や壁に埋まらず扉の真正面へ戻る。
            PlayerController player = interactor != null ? interactor.GetComponentInParent<PlayerController>() : null;
            if (player != null)
            {
                Vector3 inward = -Outward;
                player.Teleport(player.transform.position, Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg);
            }

            GameManager.Instance.Quests?.ReportEnter(_buildingId);
            DayStats.NoteEnter(_buildingId);

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null && loader.HasInterior(_buildingId))
            {
                loader.Enter(_buildingId);
                return;
            }

            HUD hud = HUD.Instance;
            if (hud != null)
            {
                hud.ShowToast(L.Format("ui.hud.entered", BuildingName));
            }
        }
    }
}
