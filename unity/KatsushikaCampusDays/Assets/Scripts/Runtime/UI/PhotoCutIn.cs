using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 写真スポットで撮った 1 枚を、ポラロイド風の枠で画面の右に出す (#170)。
    /// 操作は止めない。HoldSeconds たつか、歩き出したら消える。出した写真の Texture は自分が持ち、消すときに捨てる。
    /// 枠は UIFactory.BuildPhotoOverlay が作る。
    /// </summary>
    public sealed class PhotoCutIn : MonoBehaviour
    {
        public const float HoldSeconds = 4.5f;
        private const float InSeconds = 0.25f;
        private const float OutSeconds = 0.4f;

        /// <summary>出てすぐの歩き出しでは消さない（走って三脚の前へ行った勢いの入力を拾わない）。</summary>
        private const float IgnoreMoveSeconds = 0.6f;

        [SerializeField] private RectTransform _frame;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RawImage _photo;
        [SerializeField] private TMP_Text _caption;

        private Texture2D _texture;
        private float _shownAt = -1f;
        private float _hideFrom = -1f;

        /// <summary>出ているか（消えかけも含む）。</summary>
        public bool IsShowing => _frame != null && _frame.gameObject.activeSelf;

        /// <summary>いま出している写真。テストが中身を見る。</summary>
        public Texture2D Texture => _texture;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(RectTransform frame, CanvasGroup group, RawImage photo, TMP_Text caption)
        {
            _frame = frame;
            _group = group;
            _photo = photo;
            _caption = caption;
        }

        private void Start()
        {
            if (_texture == null)
            {
                SetVisible(false);
            }
        }

        private void OnDestroy()
        {
            ReleaseTexture();
        }

        /// <summary>写真を出す。texture はこちらが引き取る（前に出していた写真は捨てる）。</summary>
        public void Show(Texture2D texture, string caption)
        {
            if (_frame == null || texture == null)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }

                return;
            }

            ReleaseTexture();
            _texture = texture;
            if (_photo != null)
            {
                _photo.texture = texture;
            }

            if (_caption != null)
            {
                _caption.text = caption ?? string.Empty;
            }

            _shownAt = Time.unscaledTime;
            _hideFrom = -1f;
            SetVisible(true);
            Apply(0f);
        }

        /// <summary>すぐに消す（フォトモードに入ったときや、次の 1 枚を撮り始めたとき）。</summary>
        public void Hide()
        {
            SetVisible(false);
            ReleaseTexture();
            _shownAt = -1f;
            _hideFrom = -1f;
        }

        /// <summary>
        /// 写真の下に書く 2 行目。「3日目 14:05」/「Day 3 14:05」。時刻は一日の時間（時、0〜24）から作る。
        /// </summary>
        public static string Stamp(int day, float hours, bool english)
        {
            float wrapped = Mathf.Repeat(hours, 24f);
            int totalMinutes = Mathf.FloorToInt(wrapped * 60f);
            string clock = (totalMinutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":"
                           + (totalMinutes % 60).ToString("00", CultureInfo.InvariantCulture);
            return english ? "Day " + day + "  " + clock : day + "日目  " + clock;
        }

        private void Update()
        {
            if (_shownAt < 0f || !IsShowing)
            {
                return;
            }

            float now = Time.unscaledTime;
            float shown = now - _shownAt;
            if (_hideFrom < 0f)
            {
                bool walking = shown > IgnoreMoveSeconds && KCDInput.Move.sqrMagnitude > 0.09f;
                if (shown >= HoldSeconds || walking || KCDInput.PhotoMode)
                {
                    _hideFrom = now;
                }

                Apply(Mathf.Clamp01(shown / InSeconds));
                return;
            }

            float fading = (now - _hideFrom) / OutSeconds;
            if (fading >= 1f)
            {
                Hide();
                return;
            }

            if (_group != null)
            {
                _group.alpha = 1f - fading;
            }
        }

        /// <summary>出てくる途中の見た目。t は 0〜1（1 で止まった状態）。少し大きい所から縮んで、ふわっと濃くなる。</summary>
        private void Apply(float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            if (_group != null)
            {
                _group.alpha = eased;
            }

            if (_frame != null)
            {
                _frame.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, eased);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_frame != null)
            {
                _frame.gameObject.SetActive(visible);
            }
        }

        private void ReleaseTexture()
        {
            if (_photo != null)
            {
                _photo.texture = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }
    }
}
