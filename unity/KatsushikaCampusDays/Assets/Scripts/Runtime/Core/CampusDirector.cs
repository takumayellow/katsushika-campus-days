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
        private bool _continued;

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
            // タイトルの「つづきから」の位置と屋内。Start ではなく最初の Update で当てるのは、
            // Minimap.Start が屋内の表示を消すなど、ほかの Start より後でないと上書きされるため (#61)。
            if (!_continued)
            {
                _continued = true;
                SaveSystem.ApplyPending();
            }

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
                else
                {
                    HUD.Instance?.ShowToast(L.Get("ui.hud.save_failed", "セーブできませんでした"));
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
                "WASD / 矢印キーで移動　Shift でダッシュ　E で話す　Tab でクエスト",
                "WASD / Arrows Move   Shift Sprint   E Talk   Tab Quests"));

            // Web 版はカーソルがブラウザのポインターロックに取られる。外し方と全画面の出し方を最初に伝える (#48)。
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                hud.ShowToast(L.Get("ui.hud.web_controls", "Esc でマウスを解放　F で全画面"));
            }
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
