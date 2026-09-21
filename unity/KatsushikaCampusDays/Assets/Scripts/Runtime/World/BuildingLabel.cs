using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 建物の上に浮かぶ名札。常にカメラを向き、遠いと薄く小さくなる。
    /// SceneBuilder が sign_&lt;id&gt; / 建物の重心にぶら下げる。
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class BuildingLabel : MonoBehaviour
    {
        [SerializeField] private float _fadeInDistance = 120f;
        [SerializeField] private float _fadeOutDistance = 220f;
        [SerializeField] private float _referenceDistance = 40f;
        [SerializeField] private float _minScale = 0.6f;
        [SerializeField] private float _maxScale = 3.5f;

        private TextMeshPro _text;
        private Transform _camera;
        private Color _baseColor;

        /// <summary>表示する名前。</summary>
        public string Label
        {
            get => _text != null ? _text.text : string.Empty;
            set
            {
                if (_text == null)
                {
                    _text = GetComponent<TextMeshPro>();
                }

                _text.text = value;
            }
        }

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            _baseColor = _text.color;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    return;
                }

                _camera = main.transform;
            }

            Vector3 delta = transform.position - _camera.position;
            float distance = delta.magnitude;

            float alpha = 1f - Mathf.InverseLerp(_fadeInDistance, _fadeOutDistance, distance);
            if (alpha <= 0.01f)
            {
                if (_text.enabled)
                {
                    _text.enabled = false;
                }

                return;
            }

            if (!_text.enabled)
            {
                _text.enabled = true;
            }

            _text.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, _baseColor.a * alpha);

            float scale = Mathf.Clamp(distance / _referenceDistance, _minScale, _maxScale);
            transform.localScale = Vector3.one * scale;
            transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }
    }
}
