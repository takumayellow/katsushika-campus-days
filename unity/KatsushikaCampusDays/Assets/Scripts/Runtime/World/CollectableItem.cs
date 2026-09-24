using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 拾えるもの。公園に散らばる「理科大グリーンの葉」などに付ける。
    /// E で拾うと QuestSystem に collect を報告して自分を消す。
    /// </summary>
    public sealed class CollectableItem : Interactable
    {
        [SerializeField] private string _itemId = "leaf";
        [SerializeField] private string _displayName = "理科大グリーンの葉";
        [SerializeField] private float _bobHeight = 0.12f;
        [SerializeField] private float _spinSpeed = 45f;

        private Vector3 _basePosition;
        private bool _taken;

        /// <summary>クエストの collect ステップの target と揃える id。</summary>
        public string ItemId
        {
            get => _itemId;
            set => _itemId = value;
        }

        /// <summary>拾ったときのトーストに出す名前。</summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        public override bool CanInteract => !_taken && isActiveAndEnabled;

        private void Awake()
        {
            PromptLabel = L.Get("ui.interact.pick_up", "拾う");
            _basePosition = transform.position;
        }

        private void Start()
        {
            // 隠しアイテムは 1 日に 1 度だけ。タイトルへ戻らずにシーンを読み直したとき、
            // 拾った物が同じ場所に出てこないようにする（DayStats はシーンをまたいで残る）。
            if (CollectibleCatalog.Instance.IsHidden(_itemId) && DayStats.HasCollected(_itemId))
            {
                _taken = true;
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 拾ったときのトースト。隠しアイテムは「隠しアイテム発見: ◯◯」、
        /// 牛乳や葉のようなクエストの拾い物は「◯◯を拾った」。
        /// </summary>
        public static string ToastFor(string itemId, string displayName)
        {
            CatalogItem item = CollectibleCatalog.Instance.FindItem(itemId);
            if (item != null && item.IsHidden)
            {
                return L.Format("ui.hud.collectible_found", item.DisplayName);
            }

            return L.Format("ui.hud.picked_up", L.Get("ui.item." + itemId, displayName));
        }

        private void Update()
        {
            float bob = Mathf.Sin(Time.time * 1.8f) * _bobHeight;
            transform.position = _basePosition + Vector3.up * bob;
            transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.World);
        }

        public override void Interact(GameObject interactor)
        {
            if (_taken)
            {
                return;
            }

            _taken = true;
            AudioManager.Instance?.PlaySe("item_get");
            GameManager.Instance.Quests?.ReportCollect(_itemId);
            DayStats.NoteCollect(_itemId);
            HUD.Instance?.ShowToast(ToastFor(_itemId, _displayName));
            Destroy(gameObject);
        }
    }
}
