using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 建物の上に浮かぶ名札。常にカメラを向き、遠いと薄くなる。
    /// 大きさは距離に比例させ、画面の上の字の高さをおよそ一定に保つ（20〜30 m 先で読めること。#181）。
    /// SceneBuilder が sign_&lt;id&gt; / 建物の重心にぶら下げる。
    /// 文字は辞書の ui.building.&lt;id&gt; から引き、言語を切り替えるたびに引き直す。
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class BuildingLabel : MonoBehaviour
    {
        /// <summary>文字の大きさ（TextMeshPro の 3D 文字なので 1 = 0.1 m）。CampusProps が名札を作るときに使う。</summary>
        public const float FontSize = 3.2f;

        /// <summary>等倍になる距離（m）。これより遠いほど大きくする。</summary>
        public const float DefaultReferenceDistance = 11f;

        /// <summary>近くで小さくしすぎない下限。</summary>
        public const float DefaultMinScale = 0.5f;

        /// <summary>遠くで大きくしすぎない上限。大きすぎると斜めから見たときに建物の壁へめり込む。</summary>
        public const float DefaultMaxScale = 6f;

        [SerializeField] private float _fadeInDistance = 120f;
        [SerializeField] private float _fadeOutDistance = 220f;
        [SerializeField] private float _referenceDistance = DefaultReferenceDistance;
        [SerializeField] private float _minScale = DefaultMinScale;
        [SerializeField] private float _maxScale = DefaultMaxScale;

        [SerializeField] private string _key;
        [SerializeField] private string _fallback;

        private TextMeshPro _text;
        private Transform _camera;
        private Color _baseColor;

        /// <summary>辞書の鍵（ui.building.&lt;id&gt;）。</summary>
        public string Key => _key;

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

        /// <summary>辞書の鍵（ui.building.&lt;id&gt;）と、鍵が無いときに出す名前。</summary>
        public void Bind(string key, string fallback)
        {
            _key = key;
            _fallback = fallback;
            Label = Resolve(key, fallback);
        }

        /// <summary>今の言語での表示名。鍵が空なら fallback をそのまま出す。</summary>
        public static string Resolve(string key, string fallback)
        {
            return string.IsNullOrEmpty(key) ? fallback : L.Get(key, fallback);
        }

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            _baseColor = _text.color;
        }

        private void OnEnable()
        {
            L.LocaleChanged += OnLocaleChanged;
            OnLocaleChanged();
        }

        private void OnDisable()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            if (!string.IsNullOrEmpty(_key))
            {
                Label = Resolve(_key, _fallback);
            }
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
