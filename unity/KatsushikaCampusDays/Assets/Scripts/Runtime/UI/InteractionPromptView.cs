using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 「E: 話す」のような操作案内。対象が居るときだけ出し、切り替わりをふわっと見せる。
    /// </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private float _fadeSpeed = 9f;

        private float _targetAlpha;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(CanvasGroup group, TMP_Text label)
        {
            _group = group;
            _label = label;
        }

        private void Awake()
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
            }

            if (_group != null)
            {
                _group.alpha = 0f;
            }
        }

        /// <summary>対象を差し替える。null なら消える。</summary>
        public void SetTarget(Interactable target)
        {
            bool visible = target != null && !KCDInput.GameplayBlocked;
            _targetAlpha = visible ? 1f : 0f;

            if (visible && _label != null)
            {
                string text = "[E] " + target.PromptLabel;
                if (_label.text != text)
                {
                    _label.text = text;
                }
            }
        }

        private void Update()
        {
            if (_group == null)
            {
                return;
            }

            _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, _fadeSpeed * Time.unscaledDeltaTime);
        }
    }
}
