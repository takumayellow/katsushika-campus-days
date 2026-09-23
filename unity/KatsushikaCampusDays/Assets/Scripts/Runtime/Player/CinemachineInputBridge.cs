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
    /// 同じカメラの CinemachineDeoccluder の平滑化もここで起動時に入れ直す (#30)。
    /// </summary>
    public sealed class CinemachineInputBridge : MonoBehaviour
    {
        /// <summary>
        /// 遮蔽物で寄ったカメラ距離を保つ秒数（Deoccluder の SmoothingTime）。
        /// 木の幹や柱の脇を通るたびにカメラが寄ったり戻ったりを細かく繰り返すのを抑える。
        /// </summary>
        public const float DeoccluderSmoothingTime = 0.4f;

        /// <summary>この秒数より短い遮蔽は無視する（Deoccluder の MinimumOcclusionTime）。一瞬横切る枝などで跳ねない。</summary>
        public const float DeoccluderMinimumOcclusionTime = 0.1f;

        /// <summary>遮蔽をよけて寄るときの減衰（Deoccluder の DampingWhenOccluded）。</summary>
        public const float DeoccluderDampingWhenOccluded = 0.2f;

        [SerializeField] private float _lookSensitivity = 2.2f;
        [SerializeField] private float _zoomSensitivity = 3.5f;
        [SerializeField] private bool _invertVertical = true;

        private readonly List<IInputAxisOwner.AxisDescriptor> _axes = new List<IInputAxisOwner.AxisDescriptor>();

        /// <summary>会話中などに視点操作を止める。</summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            // 作り直す前のシーン（SmoothingTime 0 のまま保存されている）にも効くよう、起動時に入れ直す。
            // AvoidObstacles は構造体なので、写してから書き換えて戻す。
            CinemachineDeoccluder deoccluder = GetComponent<CinemachineDeoccluder>();
            if (deoccluder != null)
            {
                CinemachineDeoccluder.ObstacleAvoidance avoid = deoccluder.AvoidObstacles;
                ApplyDeoccluderSmoothing(ref avoid);
                deoccluder.AvoidObstacles = avoid;
            }
        }

        private void OnEnable()
        {
            Collect();
        }

        /// <summary>Deoccluder の平滑化の値を入れる。ActorFactory（シーン生成）と起動時の両方から使う。</summary>
        public static void ApplyDeoccluderSmoothing(ref CinemachineDeoccluder.ObstacleAvoidance avoid)
        {
            avoid.SmoothingTime = DeoccluderSmoothingTime;
            avoid.MinimumOcclusionTime = DeoccluderMinimumOcclusionTime;
            avoid.DampingWhenOccluded = DeoccluderDampingWhenOccluded;
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
