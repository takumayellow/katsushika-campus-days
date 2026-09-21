#if KCD_CINEMACHINE
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Cinemachine の入力軸を KCDInput につなぐ。
    /// 既定の CinemachineInputAxisController は InputActionReference アセットを要求するため、
    /// コードだけでシーンを組む都合に合わせて軸を直接動かす。
    /// </summary>
    public sealed class CinemachineInputBridge : MonoBehaviour
    {
        [SerializeField] private float _lookSensitivity = 2.2f;
        [SerializeField] private float _zoomSensitivity = 3.5f;
        [SerializeField] private bool _invertVertical = true;

        private readonly List<IInputAxisOwner.AxisDescriptor> _axes = new List<IInputAxisOwner.AxisDescriptor>();

        /// <summary>会話中などに視点操作を止める。</summary>
        public bool InputEnabled { get; set; } = true;

        private void OnEnable()
        {
            Collect();
        }

        /// <summary>軸の持ち主（OrbitalFollow など）から駆動対象を集める。</summary>
        private void Collect()
        {
            _axes.Clear();
            foreach (IInputAxisOwner owner in GetComponentsInChildren<IInputAxisOwner>())
            {
                owner.GetInputAxes(_axes);
            }
        }

        private void Update()
        {
            if (!InputEnabled || _axes.Count == 0 || KCDInput.LookBlocked)
            {
                return;
            }

            Vector2 look = KCDInput.Look;
            float zoom = KCDInput.Zoom;

            for (int i = 0; i < _axes.Count; i++)
            {
                IInputAxisOwner.AxisDescriptor descriptor = _axes[i];
                if (descriptor.DrivenAxis == null)
                {
                    continue;
                }

                ref InputAxis axis = ref descriptor.DrivenAxis();
                float delta = descriptor.Hint switch
                {
                    IInputAxisOwner.AxisDescriptor.Hints.X => look.x * _lookSensitivity,
                    IInputAxisOwner.AxisDescriptor.Hints.Y =>
                        look.y * _lookSensitivity * (_invertVertical ? -1f : 1f),
                    _ => -zoom * _zoomSensitivity
                };

                if (Mathf.Abs(delta) < 0.0001f)
                {
                    continue;
                }

                axis.Value = axis.ClampValue(axis.Value + delta);
            }
        }
    }
}
#endif
