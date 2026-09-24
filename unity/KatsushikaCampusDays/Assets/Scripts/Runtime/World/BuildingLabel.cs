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

            float alpha = AlphaAt(distance, _fadeInDistance, _fadeOutDistance);
            if (IsHidden(alpha))
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

            float scale = ScaleAt(distance, _referenceDistance, _minScale, _maxScale);
            transform.localScale = Vector3.one * scale;
            transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        /// <summary>カメラからの距離での不透明度。fadeIn m までは 1、fadeOut m で 0、その間は線形。</summary>
        public static float AlphaAt(float distance, float fadeInDistance, float fadeOutDistance)
        {
            return 1f - Mathf.InverseLerp(fadeInDistance, fadeOutDistance, distance);
        }

        /// <summary>ほぼ見えないので文字の描画ごと止めるか。</summary>
        public static bool IsHidden(float alpha)
        {
            return alpha <= 0.01f;
        }

        /// <summary>距離での大きさ。reference m で等倍、遠いほど大きくして読める大きさを保つ（min〜max に収める）。</summary>
        public static float ScaleAt(float distance, float referenceDistance, float minScale, float maxScale)
        {
            return Mathf.Clamp(distance / referenceDistance, minScale, maxScale);
        }
    }
}
