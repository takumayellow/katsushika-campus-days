using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 屋内のエレベーターの乗り場。EV の扉の前で E を押すと <see cref="ElevatorMenu"/> を開き、選んだ階の乗り場へ移る (#275)。
    /// 乗り場は FBX の ev_&lt;id&gt;_&lt;階&gt; Empty（扉から 1.4 m 前の床）で、InteriorStage が階ごとに 1 つずつ置く。
    /// 階段室の扉も同じ仕組みで階を移る (#303)。<see cref="Stairs"/> のときは文言だけ「階段」にする。
    /// 第2研究棟の西の端の階段は食堂の 1F・2F にも下りるので、行き先の階は別の建物の屋内でもよい（<see cref="Stop.BuildingId"/>）。
    /// </summary>
    public sealed class ElevatorPanel : Interactable
    {
        /// <summary>行き先の 1 階分。</summary>
        [System.Serializable]
        public struct Stop
        {
            /// <summary>階の名前（"1F"、"3F" など）。</summary>
            public string Floor;

            /// <summary>その階にあるもの（日本語）。無ければ空。</summary>
            public string CaptionJa;

            /// <summary>その階にあるもの（英語）。無ければ空。</summary>
            public string CaptionEn;

            /// <summary>着いたときに立つ位置と向き。</summary>
            public Transform Landing;

            /// <summary>その階がある建物の id。空なら乗り場と同じ建物。</summary>
            public string BuildingId;
        }

        /// <summary>stop の階がある建物の id。<see cref="Stop.BuildingId"/> が空なら here（乗り場の建物）。</summary>
        public static string BuildingOf(Stop stop, string here)
        {
            return string.IsNullOrEmpty(stop.BuildingId) ? here : stop.BuildingId;
        }

        [SerializeField] private string _buildingId = string.Empty;
        [SerializeField] private string _floor = string.Empty;
        [SerializeField] private Stop[] _stops = new Stop[0];
        [SerializeField] private float _doorOffset;
        [SerializeField] private float _doorHalfWidth;
        [SerializeField] private bool _stairs;

        /// <summary>この乗り場がある建物の id。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        /// <summary>階段室の扉か（false ならエレベーター）。</summary>
        public bool Stairs
        {
            get => _stairs;
            set
            {
                _stairs = value;
                RefreshLabel();
            }
        }

        /// <summary>この乗り場の階。</summary>
        public string Floor
        {
            get => _floor;
            set => _floor = value;
        }

        /// <summary>
        /// 扉の面の位置。判定の中心から扉の側（-forward）へ offset（m）、横に ±halfWidth（m）の線分。
        /// 「調べる」の候補は、この線分の上でプレイヤーに最も近い点で距離と向きを測る（#303）。
        /// 判定の中心で測ると、中心より扉に寄ったときに中心が背中側になって乗れなかった。
        /// </summary>
        public void SetDoor(float offset, float halfWidth)
        {
            _doorOffset = Mathf.Max(0f, offset);
            _doorHalfWidth = Mathf.Max(0f, halfWidth);
        }

        /// <summary>扉の面の上で from に最も近い点。</summary>
        public override Vector3 AimPoint(Vector3 from)
        {
            Vector3 local = transform.InverseTransformPoint(from);
            float x = Mathf.Clamp(local.x, -_doorHalfWidth, _doorHalfWidth);
            return transform.TransformPoint(new Vector3(x, 0f, -_doorOffset));
        }

        /// <summary>止まる階。下の階から順に並べる。</summary>
        public Stop[] Stops
        {
            get => _stops;
            set => _stops = value ?? new Stop[0];
        }

        /// <summary>
        /// 乗り場の判定の中心からプレイヤーの足までの高さの差がこれ以内なら、その階にいるとみなす（m）。
        /// 判定の中心は床から 1 m 上で、階の高さは 4 m。
        /// </summary>
        public const float FloorReach = 2f;

        private Transform _player;

        /// <summary>
        /// 建物の中にいて、出入りや階の移動の暗転の途中でなく、プレイヤーがこの乗り場の階にいるときだけ乗れる。
        /// 「調べる」の候補は高さを見ないので、真下の階の乗り場を拾わないようにここで断る。
        /// 有効かどうかは OnEnable を経たかを含む isActiveAndEnabled ではなく、EditMode のテストでも同じに決まる形で見る。
        /// </summary>
        public override bool CanInteract
        {
            get
            {
                InteriorLoader loader = InteriorLoader.Instance;
                return enabled && gameObject.activeInHierarchy && _stops.Length > 1
                    && (loader == null || (loader.IsInside && !loader.IsBusy))
                    && PlayerOnThisFloor();
            }
        }

        private bool PlayerOnThisFloor()
        {
            if (_player == null)
            {
                PlayerController player = FindAnyObjectByType<PlayerController>();
                if (player == null)
                {
                    return true;
                }

                _player = player.transform;
            }

            return Mathf.Abs(_player.position.y - transform.position.y) <= FloorReach;
        }

        /// <summary>プロンプトは扉の高さに出す。</summary>
        public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.2f;

        private void OnEnable()
        {
            RefreshLabel();
            L.LocaleChanged += RefreshLabel;
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshLabel;
        }

        private void RefreshLabel()
        {
            PromptLabel = _stairs
                ? L.Pick("階段で別の階へ", "Take the stairs")
                : L.Pick("エレベーターに乗る", "Take the elevator");
        }

        /// <summary>E が押された。行き先を選ぶ画面を開く。</summary>
        public override void Interact(GameObject interactor)
        {
            ElevatorMenu menu = ElevatorMenu.Instance;
            if (menu != null)
            {
                menu.Open(this);
            }
        }

        /// <summary>この乗り場の階が <see cref="Stops"/> の何番目か。建物も比べる。無ければ -1。</summary>
        public int CurrentIndex
        {
            get
            {
                for (int i = 0; i < _stops.Length; i++)
                {
                    if (_stops[i].Floor == _floor && BuildingOf(_stops[i], _buildingId) == _buildingId)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>
        /// i 番目の階へ移る。別の建物の階なら、その建物の中に入ったことにして移る（<see cref="InteriorLoader.MoveAcross"/>）。
        /// 今の階・範囲の外・乗り場の無い階なら何もせず false。
        /// </summary>
        public bool GoTo(int i)
        {
            if (i < 0 || i >= _stops.Length || i == CurrentIndex || _stops[i].Landing == null)
            {
                return false;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null)
            {
                return false;
            }

            Transform landing = _stops[i].Landing;
            string building = BuildingOf(_stops[i], _buildingId);
            return building == _buildingId
                ? loader.MoveWithin(landing.position, landing.eulerAngles.y)
                : loader.MoveAcross(building, landing.position, landing.eulerAngles.y);
        }
    }
}
