using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 建物の入口。FBX の entrance_&lt;id&gt; Empty、または footprint から推定した位置に置かれる。
    /// プレイヤーが踏むと「入った」ことをクエストへ報告し、HUD に一言出す。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class EntranceTrigger : MonoBehaviour
    {
        [SerializeField] private string _buildingId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private float _reentryCooldown = 2f;

        private float _lastEnteredAt = -999f;

        /// <summary>campus.json の建物 id。</summary>
        public string BuildingId
        {
            get => _buildingId;
            set => _buildingId = value;
        }

        /// <summary>トーストに出す表示名。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3.5f, 3f, 2.5f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player") || Time.time - _lastEnteredAt < _reentryCooldown)
            {
                return;
            }

            InteriorLoader loader = InteriorLoader.Instance;
            if (loader != null && (loader.IsInside || Time.time - loader.LastExitAt < 3f))
            {
                return;
            }

            _lastEnteredAt = Time.time;

            GameManager.Instance.Quests?.ReportEnter(_buildingId);
            DayStats.NoteEnter(_buildingId);

            if (loader != null && loader.HasInterior(_buildingId))
            {
                loader.Enter(_buildingId);
                return;
            }

            HUD hud = HUD.Instance;
            if (hud != null && !string.IsNullOrEmpty(_displayName))
            {
                hud.ShowToast(L.Format("ui.hud.entered", L.Get("ui.building." + _buildingId, _displayName)));
            }
        }
    }
}
