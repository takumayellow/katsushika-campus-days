using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 写真スポットの三脚 (#65)。E で、collectibles.json の look_dir の向きへプレイヤーを向け、
    /// カメラを背後へ回してから PhotoSystem.CaptureAt(spotId) で撮る。撮った id は DayStats が
    /// 写真として数える（結果画面の「写真」と称号「写真散歩」系）。
    ///
    /// 入っただけでサブクエ（q_sq_photo_walk）の visit が進むのは、同じ場所に置く VisitZone の役目。
    /// 置くのは SceneBuilder の CollectibleStage。
    /// </summary>
    public sealed class PhotoSpot : Interactable
    {
        [SerializeField] private string _spotId = string.Empty;
        [SerializeField] private Vector2 _lookDir = Vector2.zero;

        /// <summary>collectibles.json の photo_spots の id（ps_*）。</summary>
        public string SpotId
        {
            get => _spotId;
            set => _spotId = value;
        }

        /// <summary>撮る向き (x, z)。0 なら向きを変えずに撮る。</summary>
        public Vector2 LookDir
        {
            get => _lookDir;
            set => _lookDir = value;
        }

        public override bool CanInteract =>
            isActiveAndEnabled && !string.IsNullOrEmpty(_spotId) && PhotoSystem.Instance != null && !KCDInput.PhotoMode;

        private void Awake()
        {
            PromptLabel = L.Get("ui.interact.photo", "撮影する");
        }

        public override void Interact(GameObject interactor)
        {
            PhotoSystem photos = PhotoSystem.Instance;
            if (photos == null || string.IsNullOrEmpty(_spotId))
            {
                return;
            }

            if (interactor != null && TryYaw(_lookDir, out float yaw))
            {
                // CharacterController は回転を戻さないので、向きだけならそのまま入れてよい。
                interactor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                CameraRig.SnapBehind(interactor.transform);
            }

            photos.CaptureAt(_spotId);
        }

        /// <summary>
        /// 向き (x, z) を Y 軸まわりの角度（度）にする。+z が 0 度、+x が 90 度。
        /// 長さがほぼ 0 なら false（向きを変えない）。
        /// </summary>
        public static bool TryYaw(Vector2 lookDir, out float yawDegrees)
        {
            if (lookDir.sqrMagnitude < 1e-6f)
            {
                yawDegrees = 0f;
                return false;
            }

            yawDegrees = Mathf.Atan2(lookDir.x, lookDir.y) * Mathf.Rad2Deg;
            return true;
        }
    }
}
