using UnityEngine;
using UnityEngine.EventSystems;

namespace KCD
{
    /// <summary>
    /// 視点を回す範囲 (#187)。スティックとボタンの後ろに画面いっぱいに敷く。
    /// 1 本指でなぞると視点を回し、2 本指で広げる・つまむと寄る・引く。3 本目からの指は見ない。
    /// </summary>
    public sealed class TouchLookArea : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        private const int MaxFingers = 2;

        private readonly int[] _ids = new int[MaxFingers];
        private readonly Vector2[] _positions = new Vector2[MaxFingers];
        private int _count;

        /// <summary>今触れている指の数（2 まで）。</summary>
        public int FingerCount => _count;

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_count >= MaxFingers || IndexOf(eventData.pointerId) >= 0 || TouchInput.IsEmulatedMouse(eventData))
            {
                return;
            }

            _ids[_count] = eventData.pointerId;
            _positions[_count] = eventData.position;
            _count++;
        }

        public void OnDrag(PointerEventData eventData)
        {
            int index = IndexOf(eventData.pointerId);
            if (index < 0)
            {
                return;
            }

            Vector2 before = _positions[index];
            if (_count == 1)
            {
                TouchInput.AddDrag(eventData.position - before, Screen.height);
            }
            else
            {
                float gapBefore = Vector2.Distance(_positions[0], _positions[1]);
                _positions[index] = eventData.position;
                float gapAfter = Vector2.Distance(_positions[0], _positions[1]);
                TouchInput.AddPinch(gapBefore, gapAfter);
            }

            _positions[index] = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            int index = IndexOf(eventData.pointerId);
            if (index < 0)
            {
                return;
            }

            // 残った指を前に詰める。2 本から 1 本に戻ったら、残った指のなぞりで視点を回す。
            for (int i = index; i < _count - 1; i++)
            {
                _ids[i] = _ids[i + 1];
                _positions[i] = _positions[i + 1];
            }

            _count--;
        }

        private void OnDisable()
        {
            _count = 0;
        }

        private int IndexOf(int pointerId)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_ids[i] == pointerId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
