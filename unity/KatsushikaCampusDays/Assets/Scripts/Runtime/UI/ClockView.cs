using TMPro;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 画面左上の時計。DayNightCycle の時刻と、時間帯の呼び名を出す。
    /// </summary>
    public sealed class ClockView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _timeLabel;
        [SerializeField] private TMP_Text _phaseLabel;

        private DayNightCycle _cycle;
        private string _lastTime = string.Empty;
        private int _textKey = -1;
        private float _phaseBaseSize;

        /// <summary>時間帯の行の高さ。組み立てたときは 44、文字を大きくするときは時計の枠（高さ 108）の下端までの 46。</summary>
        public const float PhaseRowHeight = 44f;
        public const float PhaseRowHeightLarge = 46f;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(TMP_Text timeLabel, TMP_Text phaseLabel)
        {
            _timeLabel = timeLabel;
            _phaseLabel = phaseLabel;
        }

        private void Start()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            L.LocaleChanged += OnLocaleChanged;
        }

        private void OnDestroy()
        {
            L.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            _lastTime = null;
        }

        private void Update()
        {
            ApplyTextSize();

            if (_cycle == null || _timeLabel == null)
            {
                return;
            }

            string text = _cycle.TimeText;
            if (text == _lastTime)
            {
                return;
            }

            _lastTime = text;
            _timeLabel.text = text;

            if (_phaseLabel != null)
            {
                _phaseLabel.text = PhaseName(_cycle.Hours);
            }
        }

        /// <summary>時間帯の文字の大きさを今の向きに合わせる。縦横や指とマウスの切り替えにも追従する。</summary>
        private void ApplyTextSize()
        {
            if (_phaseLabel == null)
            {
                return;
            }

            bool touch = InputMode.IsTouch;
            bool portrait = ScreenShape.IsPortrait;
            int key = HudTextScale.Key(touch, portrait);
            if (key == _textKey)
            {
                return;
            }

            if (_textKey < 0)
            {
                _phaseBaseSize = _phaseLabel.fontSize;
            }

            _textKey = key;
            float height = HudTextScale.Shrunk(touch, portrait) ? PhaseRowHeightLarge : PhaseRowHeight;
            HudTextScale.FitRow(_phaseLabel, HudTextScale.Size(_phaseBaseSize, touch, portrait), height);
        }

        /// <summary>時間帯の呼び名。5 / 9 / 12 / 15 / 17 / 19 時で区切り、区切りの時刻ちょうどは後ろの帯に入る。</summary>
        public static string PhaseName(float hours)
        {
            if (hours < 5f) { return L.Get("ui.hud.time_midnight", "深夜"); }
            if (hours < 9f) { return L.Get("ui.hud.time_morning", "朝"); }
            if (hours < 12f) { return L.Get("ui.hud.time_forenoon", "午前"); }
            if (hours < 15f) { return L.Get("ui.hud.time_afternoon", "昼下がり"); }
            if (hours < 17f) { return L.Get("ui.hud.time_late_afternoon", "夕方まえ"); }
            if (hours < 19f) { return L.Get("ui.hud.time_dusk", "夕暮れ"); }
            return L.Get("ui.hud.time_night", "夜");
        }
    }
}
