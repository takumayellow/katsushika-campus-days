using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 階のある屋内で、プレイヤーのいる階の明かりだけを点ける (#275)。
    /// 上下の階の天井の明かりは床の板から 1.4 m しか離れておらず、点けたままだと Forward（Mobile_Renderer）では
    /// 1 つの物体に届く光源が上限（4）を越えて、いる階の明かりが落ちる。階はプレイヤーの足の高さで決めるので、エレベーターでも
    /// セーブを読んで戻ったときでも同じように切り替わる。ミニマップの見出しの階（InteriorLoader.ShowFloor）もここで合わせる。
    /// </summary>
    public sealed class InteriorFloorLights : MonoBehaviour
    {
        [SerializeField] private string _buildingId = string.Empty;
        [SerializeField] private string[] _floorNames = new string[0];
        [SerializeField] private float[] _floorBottoms = new float[0];
        [SerializeField] private Light[] _lights = new Light[0];
        [SerializeField] private int[] _lightFloors = new int[0];

        private Transform _player;
        private int _active = -1;

        /// <summary>
        /// InteriorStage から。buildingId はこの屋内の建物、floors は階の名前（"1F" など）、
        /// bottoms は各階の下端の高さ（ワールドの Y、下の階から順。floors と同じ並び）。
        /// 明かりはそれぞれ自分の高さで階に振り分ける。
        /// </summary>
        public void Configure(string buildingId, string[] floors, float[] bottoms, Light[] lights)
        {
            _buildingId = buildingId ?? string.Empty;
            _floorNames = floors ?? new string[0];
            _floorBottoms = bottoms ?? new float[0];
            _lights = lights ?? new Light[0];
            _lightFloors = new int[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
            {
                _lightFloors[i] = _lights[i] != null ? FloorOf(_floorBottoms, _lights[i].transform.position.y) : 0;
            }

            _active = -1;
        }

        /// <summary>いま点けている階の番号。まだ決めていなければ -1。</summary>
        public int ActiveFloor => _active;

        /// <summary>
        /// 高さ y が何番目の階か。bottoms[i] 以上の最後の i。いちばん下の階より下なら 0。
        /// </summary>
        public static int FloorOf(float[] bottoms, float y)
        {
            int floor = 0;
            for (int i = 1; i < bottoms.Length; i++)
            {
                if (y >= bottoms[i])
                {
                    floor = i;
                }
            }

            return floor;
        }

        private void Update()
        {
            InteriorLoader loader = InteriorLoader.Instance;
            if (loader == null || !loader.IsInside || loader.CurrentId != _buildingId)
            {
                return;
            }

            if (_player == null)
            {
                PlayerController player = FindAnyObjectByType<PlayerController>();
                if (player == null)
                {
                    return;
                }

                _player = player.transform;
            }

            int floor = FloorOf(_floorBottoms, _player.position.y);
            Apply(floor);
            string name = floor < _floorNames.Length ? _floorNames[floor] : string.Empty;
            if (loader.CurrentFloor != name)
            {
                loader.ShowFloor(name);
            }
        }

        /// <summary>floor の階の明かりだけを点ける。同じ階のままなら何もしない。</summary>
        public void Apply(int floor)
        {
            if (floor == _active)
            {
                return;
            }

            _active = floor;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].enabled = _lightFloors[i] == floor;
                }
            }
        }
    }
}
