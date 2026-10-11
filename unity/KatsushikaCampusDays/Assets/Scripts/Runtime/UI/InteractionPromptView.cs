using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 「[E] 話す」「[Q] 手を振る」のような操作案内。対象が居るときだけ出し、切り替わりをふわっと見せる。
    /// </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private float _fadeSpeed = 9f;

        /// <summary>2 つ並べて枠に収まらないときに縮める下限の文字の大きさ。</summary>
        public const float MinFontSize = 18f;

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

            // 「[E] 撮影する　[Q] 手を振る」や英語の長い文言は 1 行の枠からはみ出すので、折り返さずに縮める。
            if (_label != null)
            {
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                _label.fontSizeMax = _label.fontSize;
                _label.fontSizeMin = Mathf.Min(MinFontSize, _label.fontSize);
                _label.enableAutoSizing = true;
            }
        }

        /// <summary>対象を差し替える。対象が null で手も振れなければ消える。</summary>
        public void SetTarget(Interactable target, bool canWave)
        {
            bool visible = IsVisible(target != null || canWave, KCDInput.GameplayBlocked);
            _targetAlpha = visible ? 1f : 0f;

            if (visible && _label != null)
            {
                string text = LabelFor(target != null ? target.PromptLabel : null,
                    canWave ? L.Get("ui.interact.wave", "手を振る") : null);
                if (_label.text != text)
                {
                    _label.text = text;
                }
            }
        }

        /// <summary>案内を出すか。対象がいて、会話やメニューで操作が封鎖されていないときだけ（押しても調べられない案内は出さない）。</summary>
        public static bool IsVisible(bool hasTarget, bool gameplayBlocked)
        {
            return hasTarget && !gameplayBlocked;
        }

        /// <summary>案内の文言。「[E] 話す」の形。</summary>
        public static string LabelFor(string promptLabel)
        {
            return "[E] " + promptLabel;
        }

        /// <summary>
        /// E と Q の案内を 1 行に並べる。「[E] 話す　[Q] 手を振る」の形で、null の方は出さない。
        /// </summary>
        public static string LabelFor(string promptLabel, string waveLabel)
        {
            if (waveLabel == null)
            {
                return LabelFor(promptLabel);
            }

            string wave = "[Q] " + waveLabel;
            return promptLabel == null ? wave : LabelFor(promptLabel) + "　" + wave;
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
