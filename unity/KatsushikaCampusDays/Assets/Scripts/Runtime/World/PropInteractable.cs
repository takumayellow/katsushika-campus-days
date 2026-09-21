using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 調べるとひとこと出してフラグを立てるだけのもの。
    /// ファミマのレジ、図書館のカウンター、掲示板などに付ける。
    /// </summary>
    public sealed class PropInteractable : Interactable
    {
        [SerializeField] private string _flagId = string.Empty;
        [SerializeField] private string _message = string.Empty;
        [SerializeField] private bool _once = true;

        private bool _used;

        public override bool CanInteract => (!_once || !_used) && isActiveAndEnabled;

        /// <summary>クエストの flag ステップの target と揃える id。</summary>
        public string FlagId => _flagId;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Configure(string promptLabel, string flagId, string message, bool once = true)
        {
            PromptLabel = promptLabel;
            _flagId = flagId;
            _message = message;
            _once = once;
        }

        public override void Interact(GameObject interactor)
        {
            if (_once && _used)
            {
                return;
            }

            _used = true;

            if (!string.IsNullOrEmpty(_message))
            {
                HUD.Instance?.ShowToast(_message);
            }

            if (!string.IsNullOrEmpty(_flagId))
            {
                GameManager.Instance.Quests?.ReportFlag(_flagId);
            }
        }
    }
}
