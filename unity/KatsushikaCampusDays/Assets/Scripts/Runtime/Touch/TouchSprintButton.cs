using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// 走るの切り替えボタン (#187)。押すと走り続け、スティックを離して止まると歩きに戻る。
    /// 走りながら親指 2 本をスティックとボタンに置き続けるのはつらいので、押しっぱなしにはしない。
    /// </summary>
    public sealed class TouchSprintButton : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] private TouchStick _stick;
        [SerializeField] private Image _face;
        [SerializeField] private Color _offColor = new Color(0.06f, 0.08f, 0.12f, 0.55f);
        [SerializeField] private Color _onColor = new Color(0.31f, 0.82f, 0.85f, 0.85f);

        private bool _wasMoving;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(TouchStick stick, Image face)
        {
            _stick = stick;
            _face = face;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (TouchInput.IsEmulatedMouse(eventData))
            {
                return;
            }

            TouchInput.Sprint = !TouchInput.Sprint;
            Paint();
        }

        private void Update()
        {
            bool moving = _stick != null && _stick.Value.sqrMagnitude > 0.01f;
            bool next = TouchInput.NextSprint(TouchInput.Sprint, _wasMoving, moving);
            _wasMoving = moving;
            if (next != TouchInput.Sprint)
            {
                TouchInput.Sprint = next;
            }

            Paint();
        }

        private void OnDisable()
        {
            TouchInput.Sprint = false;
            _wasMoving = false;
            Paint();
        }

        private void Paint()
        {
            if (_face != null)
            {
                _face.color = TouchInput.Sprint ? _onColor : _offColor;
            }
        }
    }
}
