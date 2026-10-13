using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 屋内のエレベーターの乗り場。EV の扉の前で E を押すと <see cref="ElevatorMenu"/> を開き、選んだ階の乗り場へ移る (#275)。
    /// 乗り場は FBX の ev_&lt;id&gt;_&lt;階&gt; Empty（扉から 1.4 m 前の床）で、InteriorStage が階ごとに 1 つずつ置く。
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
        }

        [SerializeField] private string _buildingId = string.Empty;
        [SerializeField] private string _floor = string.Empty;
        [SerializeField] private Stop[] _stops = new Stop[0];

        /// <summary>この乗り場がある建物の id。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        /// <summary>この乗り場の階。</summary>
        public string Floor
        {
            get => _floor;
            set => _floor = value;
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
            PromptLabel = L.Pick("エレベーターに乗る", "Take the elevator");
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

        /// <summary>この乗り場の階が <see cref="Stops"/> の何番目か。無ければ -1。</summary>
        public int CurrentIndex
        {
            get
            {
                for (int i = 0; i < _stops.Length; i++)
                {
                    if (_stops[i].Floor == _floor)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>
        /// i 番目の階へ移る。今の階・範囲の外・乗り場の無い階なら何もせず false。
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
            return loader.MoveWithin(landing.position, landing.eulerAngles.y);
        }
    }
}
