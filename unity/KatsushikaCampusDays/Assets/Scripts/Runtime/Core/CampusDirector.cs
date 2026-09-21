using UnityEngine;

namespace KCD
{
    /// <summary>
    /// Campus シーンの進行役。カーソルの掴み、初回の歓迎メッセージ、F5/F9 のクイックセーブを見る。
    /// </summary>
    public sealed class CampusDirector : MonoBehaviour
    {
        [SerializeField] private float _welcomeDelay = 1.2f;

        private bool _welcomed;
        private float _startedAt;

        private void Start()
        {
            _startedAt = Time.unscaledTime;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 軌道カメラの初期ヨーはワールド 0 なので、プレイヤーの向きの真後ろへ合わせる
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                CameraRig.SnapBehind(player.transform);
            }
        }

        private void Update()
        {
            if (!_welcomed && Time.unscaledTime - _startedAt > _welcomeDelay)
            {
                _welcomed = true;
                ShowWelcome();
            }

            // リザルトや会話の下でセーブ・ロードすると画面と実状態が食い違う。
            if (ResultScreen.IsAnyOpen || KCDInput.GameplayBlocked)
            {
                return;
            }

            if (KCDInput.QuickSavePressed)
            {
                if (SaveSystem.Save())
                {
                    HUD.Instance?.ShowToast(L.Get("ui.hud.quick_saved", "クイックセーブしました"));
                }
            }
            else if (KCDInput.QuickLoadPressed)
            {
                SaveSystem.Load();
            }
        }

        private static void ShowWelcome()
        {
            HUD hud = HUD.Instance;
            if (hud == null)
            {
                return;
            }

            hud.ShowToast(L.Pick("東京理科大学 葛飾キャンパス", "Tokyo University of Science, Katsushika Campus"));
            hud.ShowToast(L.Pick(
                "WASD で移動　Shift でダッシュ　E で話す　Tab でクエスト",
                "WASD Move   Shift Sprint   E Talk   Tab Quests"));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || KCDInput.GameplayBlocked)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
