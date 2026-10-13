using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;

namespace KCD
{
    /// <summary>
    /// 移動のスティック (#187)。受け持ちの範囲（画面の左側）のどこでも、指を置いたところが中心になる。
    /// 倒した量を仮想のゲームパッドの左スティックに書くので、<see cref="KCDInput.Move"/> はゲームパッドと同じ読み方で動く。
    /// Input System の OnScreenStick の動的な中心は、置いた位置のまわりの円の中しか受けないので、四角の範囲を受け持つために作った。
    /// </summary>
    public sealed class TouchStick : OnScreenControl,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        private const int NoPointer = int.MinValue;

        [InputControl(layout = "Vector2")]
        [SerializeField] private string _controlPath = "<Gamepad>/leftStick";

        /// <summary>いっぱいに倒したことになる指の動き（受け持ちの範囲の単位）。</summary>
        [SerializeField] private float _range = 90f;

        [SerializeField] private RectTransform _base;
        [SerializeField] private RectTransform _knob;

        private int _pointerId = NoPointer;
        private Vector2 _origin;

        /// <summary>今の倒し具合。長さは 0〜1。</summary>
        public Vector2 Value { get; private set; }

        /// <summary>指が触れているか。</summary>
        public bool Held => _pointerId != NoPointer;

        protected override string controlPathInternal
        {
            get => _controlPath;
            set => _controlPath = value;
        }

        /// <summary>SceneBuilder から差し込む。base と knob は指を置いたところに出す丸。</summary>
        public void Bind(RectTransform baseCircle, RectTransform knob, float range)
        {
            _base = baseCircle;
            _knob = knob;
            _range = range;
        }

        /// <summary>中心から offset だけ動かしたときの倒し具合。range で 1、それより先は 1 のまま。</summary>
        public static Vector2 StickValue(Vector2 offset, float range)
        {
            return range <= 0f ? Vector2.zero : Vector2.ClampMagnitude(offset / range, 1f);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // 既定のなぞりの遊び（10 画素）があると、指を少し動かしてもスティックが倒れない。
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Held || TouchInput.IsEmulatedMouse(eventData) || !TryLocal(eventData, out Vector2 local))
            {
                return;
            }

            _pointerId = eventData.pointerId;
            _origin = local;
            ShowVisuals(true);
            Apply(local);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId || !TryLocal(eventData, out Vector2 local))
            {
                return;
            }

            Apply(local);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId)
            {
                Release();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ShowVisuals(false);
        }

        protected override void OnDisable()
        {
            Release();
            base.OnDisable();
        }

        private bool TryLocal(PointerEventData eventData, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out local);
        }

        private void Apply(Vector2 local)
        {
            Value = StickValue(local - _origin, _range);
            SendValueToControl(Value);
            if (_knob != null)
            {
                _knob.anchoredPosition = _origin + Value * _range;
            }
        }

        private void Release()
        {
            bool wasHeld = Held;
            _pointerId = NoPointer;
            Value = Vector2.zero;
            ShowVisuals(false);
            if (wasHeld)
            {
                SendValueToControl(Vector2.zero);
            }
        }

        private void ShowVisuals(bool show)
        {
            if (_base != null)
            {
                _base.anchoredPosition = _origin;
                _base.gameObject.SetActive(show);
            }

            if (_knob != null)
            {
                _knob.anchoredPosition = _origin;
                _knob.gameObject.SetActive(show);
            }
        }
    }
}
