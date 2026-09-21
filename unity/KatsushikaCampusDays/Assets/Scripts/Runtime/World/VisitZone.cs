using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 「ここに来たら進む」場所。キャンパスモール・水盤前・公園などに置く見えない箱。
    /// 入ると QuestSystem に visit を報告する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VisitZone : MonoBehaviour
    {
        [SerializeField] private string _placeId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private bool _announce = true;

        private bool _reportedOnce;

        /// <summary>クエストの visit ステップの target と揃える id。</summary>
        public string PlaceId
        {
            get => _placeId;
            set => _placeId = value;
        }

        /// <summary>トーストに出す場所の名前。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(10f, 6f, 10f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (string.IsNullOrEmpty(_placeId) || !other.CompareTag("Player"))
            {
                return;
            }

            GameManager.Instance.Quests?.ReportVisit(_placeId);

            if (_announce && !_reportedOnce && !string.IsNullOrEmpty(_displayName))
            {
                _reportedOnce = true;
                HUD.Instance?.ShowToast(_displayName);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // 時刻条件つきのステップは、入った瞬間には条件を満たしていないことがある。
            // 留まっているあいだは一定間隔で報告し直す。
            if (Time.frameCount % 30 != 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_placeId) && other.CompareTag("Player"))
            {
                GameManager.Instance.Quests?.ReportVisit(_placeId);
            }
        }
    }
}
