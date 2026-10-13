#if KCD_CINEMACHINE
using Unity.Cinemachine;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 肩越しカメラを壁・天井・木の幹にめり込ませない (#12)。CinemachineDeoccluder の代わりに FollowCamera に付ける。
    ///
    /// Body 段（OrbitalFollow が位置を決めた直後）で、見る点（CameraAim）からカメラへ半径 0.25 m の球を飛ばし、
    /// 最初に当たった止める面（<see cref="CameraObstacleFilter"/>）の 0.05 m 手前まで寄せる。
    /// Deoccluder は見る点から 1.12 m 先から球を飛ばし、0.8 m より手前に寄らず、0.1 s 待ってから 0.2 s かけて寄ったので、
    /// 狭い部屋・階段・脇の幹で壁の中が写っていた。こちらは見る点から飛ばし、寄るのはそのフレームで済ませる。
    /// 戻るときは <see cref="CameraDistanceFilter"/> が 0.2 s 待ってから約 0.3 s の時定数で戻す。
    ///
    /// 屋内（InteriorLoader.IsInside）では縦角と距離の範囲を <see cref="CameraOrbitProfile.Indoor"/> に絞り、
    /// 真上に飛ばした球で見つけた天井の 0.3 m 下より上へはカメラを出さない。
    /// 寄りすぎて頭の裏で画面が埋まるときは、プレイヤーの体を影だけにする（<see cref="CameraNearHider"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineCameraGuard : CinemachineExtension
    {
        [Tooltip("見る点からカメラへ飛ばす球の半径（m）。")]
        [SerializeField] private float _castRadius = 0.25f;

        [Tooltip("当たった面からどれだけ手前にカメラを置くか（m）。")]
        [SerializeField] private float _skin = 0.05f;

        [Tooltip("見る点にこれより近くは寄せない（m）。")]
        [SerializeField] private float _minDistance = 0.1f;

        [Tooltip("屋内で天井からどれだけ下にカメラを置くか（m）。")]
        [SerializeField] private float _ceilingMargin = 0.3f;

        [Tooltip("遮蔽が消えてから戻り始めるまでの秒数。")]
        [SerializeField] private float _pullInHold = 0.2f;

        [Tooltip("戻るときの時定数（秒）。")]
        [SerializeField] private float _recoverTime = 0.3f;

        private readonly CameraNearHider _nearHider = new CameraNearHider();

        private CinemachineOrbitalFollow _orbital;
        private bool _orbitalSearched;
        private bool _outdoorCaptured;
        private CameraOrbitProfile _outdoor;
        private bool _indoorApplied;
        private float _outdoorRadialValue = 1f;
        private Transform _playerRoot;

        /// <summary>屋内の範囲に絞っているか。</summary>
        public bool IndoorApplied => _indoorApplied;

        private sealed class VcamExtraState : VcamExtraStateBase
        {
            public readonly CameraDistanceFilter Filter = new CameraDistanceFilter();
            public Vector3 PreviousCameraPosition;
            public bool PreviousValid;
        }

        /// <summary>寄りすぎで隠していたプレイヤーの体を出す。フォトモードに入るとき（パイプラインが止まる前）に呼ぶ。</summary>
        public void ShowPlayer()
        {
            _nearHider.Show();
        }

        protected override void OnDestroy()
        {
            _nearHider.Show();
            base.OnDestroy();
        }

        private void OnDisable()
        {
            _nearHider.Show();
        }

        public override void PrePipelineMutateCameraStateCallback(
            CinemachineVirtualCameraBase vcam, ref CameraState curState, float deltaTime)
        {
            base.PrePipelineMutateCameraStateCallback(vcam, ref curState, deltaTime);
            ApplyOrbitProfile(IsIndoors());
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body)
            {
                return;
            }

            VcamExtraState extra = GetExtraState<VcamExtraState>(vcam);
            if (!state.HasLookAt())
            {
                extra.PreviousValid = false;
                _nearHider.Show();
                return;
            }

            bool valid = vcam.PreviousStateIsValid && deltaTime >= 0f && extra.PreviousValid;
            extra.Filter.PullInHold = _pullInHold;
            extra.Filter.RecoverTime = _recoverTime;

            Vector3 pivot = state.ReferenceLookAt;
            Vector3 camera = state.GetCorrectedPosition();
            Vector3 target = camera;

            if (_indoorApplied)
            {
                float reach = Mathf.Max(0f, camera.y - pivot.y) + _ceilingMargin;
                float ceiling = CameraObstacleFilter.CeilingAbove(pivot, _castRadius, reach);
                target.y = CameraMath.CeilingClampedHeight(camera.y, pivot.y, ceiling, _ceilingMargin);
            }

            Vector3 offset = target - pivot;
            float desired = offset.magnitude;
            if (desired < Epsilon)
            {
                extra.PreviousValid = false;
                return;
            }

            Vector3 direction = offset / desired;
            float hit = CameraObstacleFilter.Sweep(pivot, _castRadius, direction, desired, out _);
            float allowed = CameraMath.AllowedDistance(desired, hit, _skin, _minDistance);
            float distance = extra.Filter.Step(desired, allowed, valid ? deltaTime : -1f);

            Vector3 corrected = pivot + direction * distance;
            Vector3 displacement = corrected - camera;
            if (displacement.sqrMagnitude > Epsilon)
            {
                state.PositionCorrection += displacement;

                // 後段の回転の減衰が、寄せた分の向きの変化を追いかけて遅れないようにする（Deoccluder と同じ扱い）。
                if (valid)
                {
                    Vector3 from = extra.PreviousCameraPosition - pivot;
                    Vector3 to = corrected - pivot;
                    if (from.sqrMagnitude > Epsilon && to.sqrMagnitude > Epsilon)
                    {
                        state.RotationDampingBypass = UnityVectorExtensions.SafeFromToRotation(from, to, state.ReferenceUp);
                    }
                }
            }

            extra.PreviousCameraPosition = corrected;
            extra.PreviousValid = true;
            _nearHider.Apply(PlayerRoot(vcam), distance);
        }

        public override void OnTargetObjectWarped(
            CinemachineVirtualCameraBase vcam, Transform target, Vector3 positionDelta)
        {
            base.OnTargetObjectWarped(vcam, target, positionDelta);
            ResetState(vcam);
        }

        public override void ForceCameraPosition(CinemachineVirtualCameraBase vcam, Vector3 pos, Quaternion rot)
        {
            base.ForceCameraPosition(vcam, pos, rot);
            ResetState(vcam);
        }

        public override float GetMaxDampTime()
        {
            return Mathf.Max(0f, _pullInHold) + Mathf.Max(0f, _recoverTime) * 3f;
        }

        private void ResetState(CinemachineVirtualCameraBase vcam)
        {
            VcamExtraState extra = GetExtraState<VcamExtraState>(vcam);
            extra.Filter.Reset();
            extra.PreviousValid = false;
        }

        private static bool IsIndoors()
        {
            InteriorLoader loader = InteriorLoader.Instance;
            return loader != null && loader.IsInside;
        }

        /// <summary>屋内と屋外で縦角と距離の範囲を差し替える。屋外に戻るときは入る前のズームに戻す。</summary>
        private void ApplyOrbitProfile(bool indoor)
        {
            CinemachineOrbitalFollow orbital = Orbital();
            if (orbital == null)
            {
                return;
            }

            if (!_outdoorCaptured)
            {
                _outdoor = new CameraOrbitProfile(orbital.VerticalAxis.Range, orbital.RadialAxis.Range);
                _outdoorCaptured = true;
            }

            if (indoor == _indoorApplied)
            {
                return;
            }

            _indoorApplied = indoor;
            if (indoor)
            {
                _outdoorRadialValue = orbital.RadialAxis.Value;
                SetRanges(orbital, CameraOrbitProfile.Indoor);
            }
            else
            {
                SetRanges(orbital, _outdoor);
                orbital.RadialAxis.Value = orbital.RadialAxis.ClampValue(_outdoorRadialValue);
            }
        }

        private static void SetRanges(CinemachineOrbitalFollow orbital, CameraOrbitProfile profile)
        {
            orbital.VerticalAxis.Range = profile.PitchRange;
            orbital.VerticalAxis.Value = orbital.VerticalAxis.ClampValue(orbital.VerticalAxis.Value);
            orbital.RadialAxis.Range = profile.RadialRange;
            orbital.RadialAxis.Value = orbital.RadialAxis.ClampValue(orbital.RadialAxis.Value);
        }

        private CinemachineOrbitalFollow Orbital()
        {
            if (_orbital == null && !_orbitalSearched)
            {
                _orbitalSearched = true;
                _orbital = GetComponent<CinemachineOrbitalFollow>();
            }

            return _orbital;
        }

        /// <summary>隠す対象。見る点（CameraAim）の親、つまりプレイヤー。</summary>
        private Transform PlayerRoot(CinemachineVirtualCameraBase vcam)
        {
            if (_playerRoot == null)
            {
                Transform follow = vcam.Follow;
                if (follow != null)
                {
                    _playerRoot = follow.parent != null ? follow.parent : follow;
                }
            }

            return _playerRoot;
        }
    }
}
#endif
