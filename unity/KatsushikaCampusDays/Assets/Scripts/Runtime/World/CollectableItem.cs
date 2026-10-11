using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 拾えるもの。公園に散らばる「理科大グリーンの葉」などに付ける。
    /// E で拾うと QuestSystem に collect を報告して、自分を非アクティブにして隠す。
    /// 隠しアイテムは、キャンパスの中からのロードで DayStats が拾う前に戻ったとき
    /// <see cref="SyncAllWithDayStats()"/> で出し直す (#106)。
    /// </summary>
    public sealed class CollectableItem : Interactable
    {
        [SerializeField] private string _itemId = "leaf";
        [SerializeField] private string _displayName = "理科大グリーンの葉";
        [SerializeField] private float _bobHeight = 0.12f;
        [SerializeField] private float _spinSpeed = 45f;
        [SerializeField] private bool _floats = true;
        [SerializeField] private Transform _groundMark;

        private Vector3 _basePosition;
        private Vector3 _groundMarkOffset;
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

        /// <summary>
        /// 宙で上下に揺れて回るか。宝石や葉のような目印は浮かせ、牛乳のような売り物は台の上に置いたまま動かさない (#186)。
        /// </summary>
        public bool Floats
        {
            get => _floats;
            set => _floats = value;
        }

        /// <summary>
        /// 床に留めておく子（隠しアイテムの足元の光る輪, #167）。物が上下に揺れても、これは置いた高さのまま動かさない。
        /// </summary>
        public Transform GroundMark
        {
            get => _groundMark;
            set
            {
                _groundMark = value;
                if (value != null)
                {
                    // 揺れている最中に差しても、本体からの位置の差で覚える（揺れの分は含まない）。
                    _groundMarkOffset = value.position - transform.position;
                }
            }
        }

        public override bool CanInteract => !_taken && isActiveAndEnabled;

        /// <summary>拾われたか（隠しアイテムなら、もう拾ってあるので隠しているか）。</summary>
        public bool IsTaken => _taken;

        private void Awake()
        {
            _basePosition = transform.position;
            if (_groundMark != null)
            {
                _groundMarkOffset = _groundMark.position - _basePosition;
            }
        }

        private void OnEnable()
        {
            L.LocaleChanged += RefreshPrompt;
            RefreshPrompt();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= RefreshPrompt;
        }

        /// <summary>いまの言語で「拾う」を出す。言語を切り替えたら出し直す。</summary>
        private void RefreshPrompt()
        {
            PromptLabel = L.Get("ui.interact.pick_up", "拾う");
        }

        private void Start()
        {
            // 隠しアイテムは 1 日に 1 度だけ。タイトルへ戻らずにシーンを読み直したとき、
            // 拾った物が同じ場所に出てこないようにする（DayStats はシーンをまたいで残る）。
            SyncWithDayStats(CollectibleCatalog.Instance);
        }

        /// <summary>
        /// そろえ直したあとに「拾われた」でいるか。
        /// 隠しアイテムは id ごとに 1 個だけなので、DayStats に入っているかどうかで決める
        /// （ロードで拾う前に戻ったら出し直し、拾ったことになっていれば隠す）。
        /// 牛乳や葉のようなクエストの拾い物は同じ id の物が何個もあり、DayStats からはどれを拾ったか分からないので、
        /// 今の状態のままにする（ロードで戻ったクエストの進みは QuestSystem が拾った数から進め直す）。
        /// </summary>
        public static bool TakenAfterSync(bool isHiddenItem, bool collected, bool taken)
        {
            return isHiddenItem ? collected : taken;
        }

        /// <summary>
        /// シーンにある拾い物（拾って隠した物も含む）を今の DayStats にそろえる。
        /// キャンパスの中からのロード（F9 / ポーズの「ロード」）はシーンを読み直さないので Start が走らない。
        /// セーブのあとに拾った隠しアイテムを出し直すため、SaveSystem がロードで呼ぶ (#106)。
        /// </summary>
        public static void SyncAllWithDayStats()
        {
            SyncAllWithDayStats(CollectibleCatalog.Instance);
        }

        /// <summary>隠しアイテムかどうかを catalog で見て、シーンにある拾い物を全部そろえる（テストから一覧を渡す）。</summary>
        public static void SyncAllWithDayStats(CollectibleCatalog catalog)
        {
            // 拾った物は隠してある（非アクティブ）ので、非アクティブも含めて集める。
            foreach (CollectableItem item in FindObjectsByType<CollectableItem>(FindObjectsInactive.Include))
            {
                if (item != null)
                {
                    item.SyncWithDayStats(catalog);
                }
            }
        }

        /// <summary>この物を今の DayStats にそろえる（<see cref="TakenAfterSync"/>）。</summary>
        public void SyncWithDayStats(CollectibleCatalog catalog)
        {
            bool hidden = catalog != null && catalog.IsHidden(_itemId);
            SetTaken(TakenAfterSync(hidden, DayStats.HasCollected(_itemId), _taken));
        }

        /// <summary>拾われた物は隠し、拾える物は出す。</summary>
        private void SetTaken(bool taken)
        {
            _taken = taken;
            if (gameObject.activeSelf == taken)
            {
                gameObject.SetActive(!taken);
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
            if (!_floats)
            {
                return;
            }

            float bob = Mathf.Sin(Time.time * 1.8f) * _bobHeight;
            transform.position = _basePosition + Vector3.up * bob;
            transform.Rotate(Vector3.up, _spinSpeed * Time.deltaTime, Space.World);
        }

        private void LateUpdate()
        {
            // 揺れて回った後に、足元の輪だけ置いた高さに戻す。
            if (_groundMark != null)
            {
                _groundMark.position = _basePosition + _groundMarkOffset;
            }
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
            HiddenNote.ShowFor(_itemId);

            // Destroy すると、キャンパスの中でロードして DayStats が拾う前に戻っても出し直せない (#106)。
            SetTaken(true);
        }
    }
}
