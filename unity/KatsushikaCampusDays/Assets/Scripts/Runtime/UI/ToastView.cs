using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面中央下に一行だけ流れる通知。連続して来たものは順番待ちさせる。
    /// </summary>
    public sealed class ToastView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private float _holdSeconds = 2.4f;
        [SerializeField] private float _fadeSeconds = 0.35f;

        private readonly Queue<string> _pending = new Queue<string>();
        private float _elapsed;
        private bool _showing;

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

        /// <summary>通知を追加する。</summary>
        public void Push(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _pending.Enqueue(message);
            }
        }

        private void Update()
        {
            if (_group == null || _label == null)
            {
                return;
            }

            if (!_showing)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                _label.text = _pending.Dequeue();
                _elapsed = 0f;
                _showing = true;
            }

            _elapsed += Time.unscaledDeltaTime;
            float total = _fadeSeconds * 2f + _holdSeconds;

            if (_elapsed >= total)
            {
                _group.alpha = 0f;
                _showing = false;
                return;
            }

            _group.alpha = _elapsed < _fadeSeconds
                ? _elapsed / _fadeSeconds
                : _elapsed > _fadeSeconds + _holdSeconds
                    ? 1f - (_elapsed - _fadeSeconds - _holdSeconds) / _fadeSeconds
                    : 1f;
        }
    }
}
