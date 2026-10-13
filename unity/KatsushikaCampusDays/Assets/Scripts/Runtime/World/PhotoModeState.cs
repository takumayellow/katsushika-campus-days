using UnityEngine;

namespace KCD
{
    /// <summary>
    /// フォトモードの間だけ変えるもの（時間・操作の封鎖・ゲーム中の UI）を、入る前の値に戻す (#12)。
    /// 入るときに Time.timeScale を 0 にし、自分の名前で操作を封鎖し、ゲーム中の UI を隠す。
    /// 抜けるときは入る前の値へ戻す（入る前の timeScale が 0.5 なら 0.5、入る前から隠れていた UI は隠れたまま）。
    /// 封鎖はオーナーごとなので、フォトモードの間にほかの画面が共有の封鎖を外しても、フォトモードの封鎖は残る。
    /// </summary>
    public sealed class PhotoModeState
    {
        private float _savedTimeScale = 1f;
        private GameObject _uiRoot;
        private bool _uiWasActive;

        /// <summary>フォトモードに入っているか。</summary>
        public bool IsActive { get; private set; }

        /// <summary>フォトモードに入る。uiRoot はゲーム中の UI（HUD の Gameplay）。null なら UI は触らない。入っていれば何もしない。</summary>
        public void Enter(GameObject uiRoot)
        {
            if (IsActive)
            {
                return;
            }

            IsActive = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            KCDInput.Block(this);
            KCDInput.PhotoMode = true;

            _uiRoot = uiRoot;
            _uiWasActive = uiRoot != null && uiRoot.activeSelf;
            if (_uiWasActive)
            {
                uiRoot.SetActive(false);
            }
        }

        /// <summary>フォトモードを抜け、時間・封鎖・UI を入る前に戻す。入っていなければ何もしない。</summary>
        public void Exit()
        {
            if (!IsActive)
            {
                return;
            }

            GameObject uiRoot = _uiRoot;
            bool uiWasActive = _uiWasActive;
            Release();
            KCDInput.PhotoMode = false;

            if (uiRoot != null && uiRoot.activeSelf != uiWasActive)
            {
                uiRoot.SetActive(uiWasActive);
            }
        }

        /// <summary>
        /// 片付けのときに呼ぶ（シーンを閉じるときの OnDisable など）。時間と自分の封鎖だけを戻す。
        /// UI は破棄の途中かもしれないので触らない。PhotoMode は PhotoSystem.OnDestroy に任せる
        /// （そちらは PhotoMode が立っているのを見て後始末をするので、先に下ろすと後始末が抜ける）。
        /// </summary>
        public void Release()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            Time.timeScale = _savedTimeScale;
            KCDInput.Unblock(this);
            _uiRoot = null;
        }
    }
}
