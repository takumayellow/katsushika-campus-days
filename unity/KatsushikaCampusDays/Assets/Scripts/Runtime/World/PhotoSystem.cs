using System;
using System.Collections;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD
{
    /// <summary>
    /// フォトモード。P で HUD を隠してカメラだけ動かせる状態にし、Enter で PNG を保存する。
    /// 保存先は persistentDataPath/Photos（Web 版はブラウザのダウンロード）。撮影は PhotoTaken で通知し、収集や実績が拾う。
    /// </summary>
    public sealed class PhotoSystem : MonoBehaviour
    {
        public const string FolderName = "Photos";

        [SerializeField] private GameObject _overlayRoot;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private Image _flash;
        [SerializeField] private float _flashTime = 0.35f;

        private bool _capturing;
        private int _count;

        public static PhotoSystem Instance { get; private set; }

        /// <summary>撮影が終わったとき。引数は保存したファイル名。</summary>
        public static event Action<string> PhotoTaken;

        public bool IsActive => KCDInput.PhotoMode;

        /// <summary>このセッションで撮った枚数。リザルトが読む。</summary>
        public int Count => _count;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject overlayRoot, TMP_Text hint, Image flash)
        {
            _overlayRoot = overlayRoot;
            _hintLabel = hint;
            _flash = flash;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            SetOverlay(false);
            if (_flash != null)
            {
                _flash.color = new Color(1f, 1f, 1f, 0f);
                _flash.raycastTarget = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (KCDInput.PhotoMode)
            {
                KCDInput.PhotoMode = false;
            }

            KCDInput.Unblock(this);
        }

        private void Update()
        {
            if (_capturing)
            {
                return;
            }

            if (!KCDInput.PhotoMode)
            {
                if (KCDInput.PhotoPressed && CanEnter())
                {
                    SetActive(true);
                }

                return;
            }

            if (KCDInput.PhotoPressed || KCDInput.MenuPressed || KCDInput.CancelPressed)
            {
                SetActive(false);
                KCDInput.MarkModalClosed();
                return;
            }

            if (KCDInput.SubmitPressed || KCDInput.InteractPressed)
            {
                StartCoroutine(Capture(null));
            }
        }

        private static bool CanEnter()
        {
            if (KCDInput.GameplayBlocked)
            {
                return false;
            }

            DialogueSystem dialogue = DialogueSystem.Instance;
            return dialogue == null || !dialogue.IsPlaying;
        }

        /// <summary>撮影スポットの判定などから呼ぶ。フォトモードに入っていなくても保存する。</summary>
        public void CaptureAt(string spotId)
        {
            // 非アクティブ時の StartCoroutine は例外になる（撮影スポットから呼ばれ得る）。
            if (!_capturing && isActiveAndEnabled)
            {
                StartCoroutine(Capture(spotId));
            }
        }

        private void SetActive(bool active)
        {
            KCDInput.PhotoMode = active;

            // 自分の名前で封鎖する。共有の GameplayBlocked への代入だと、写真モードを抜けたときに
            // ほかの画面の封鎖まで外していた (#62)。
            if (active)
            {
                KCDInput.Block(this);
            }
            else
            {
                KCDInput.Unblock(this);
            }

            HUD.Instance?.SetGameplayUIVisible(!active);
            SetOverlay(active);
            AudioManager.Instance?.PlayUi(active ? "ui_open" : "ui_close");
        }

        private void SetOverlay(bool visible)
        {
            if (_overlayRoot != null)
            {
                _overlayRoot.SetActive(visible);
            }

            if (visible && _hintLabel != null)
            {
                _hintLabel.text = L.Pick("Enter で撮影　/　P で戻る", "Enter: shoot   P: back");
            }
        }

        private IEnumerator Capture(string spotId)
        {
            _capturing = true;
            bool overlayWasVisible = _overlayRoot != null && _overlayRoot.activeSelf;
            SetOverlay(false);
            yield return new WaitForEndOfFrame();

            string fileName = FileNameFor(DateTime.Now);
            bool saved;
            try
            {
                saved = SavePhoto(fileName);
            }
            catch (Exception error)
            {
                saved = false;
                Debug.LogWarning("[KCD] 写真の保存に失敗: " + error.Message);
            }

            if (!saved)
            {
                // 保存できなかった撮影は枚数にも収集にも数えない。
                if (overlayWasVisible)
                {
                    SetOverlay(true);
                }

                HUD.Instance?.ShowToast(L.Pick("写真を保存できませんでした", "Could not save the photo"));
                _capturing = false;
                yield break;
            }

            _count++;
            AudioManager.Instance?.PlaySe("camera_shutter");
            yield return Flash();

            if (overlayWasVisible)
            {
                SetOverlay(true);
            }

            string label = string.IsNullOrEmpty(spotId) ? fileName : spotId;
            HUD.Instance?.ShowToast(L.Format("ui.hud.photo_taken", label));
            DayStats.NotePhoto(string.IsNullOrEmpty(spotId) ? fileName : spotId);
            PhotoTaken?.Invoke(string.IsNullOrEmpty(spotId) ? fileName : spotId);
            _capturing = false;
        }

        /// <summary>
        /// 写真のファイル名。撮った日時から作る。端末の言語設定に依らず西暦で書く
        /// （その地域の暦で書くと、タイ語の設定では年が仏暦の 2569 になる）。
        /// </summary>
        public static string FileNameFor(DateTime time)
        {
            return "kcd_" + time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>
        /// 今の画面を 1 枚置く。フレームの描画が終わったあと（WaitForEndOfFrame のあと）に呼ぶ。
        /// デスクトップは persistentDataPath/Photos。Web 版の persistentDataPath は IndexedDB の中で取り出せないので、
        /// PNG にしてブラウザにダウンロードさせる (#61)。置けなければ false か例外。
        /// </summary>
        private static bool SavePhoto(string fileName)
        {
            if (WebDownload.Supported)
            {
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    return WebDownload.Send(fileName, shot.EncodeToPNG(), "image/png");
                }
                finally
                {
                    Destroy(shot);
                }
            }

            string folder = Path.Combine(Application.persistentDataPath, FolderName);
            Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, fileName));
            return true;
        }

        private IEnumerator Flash()
        {
            if (_flash == null)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < _flashTime)
            {
                elapsed += Time.unscaledDeltaTime;
                float alpha = Mathf.Clamp01(1f - elapsed / _flashTime);
                _flash.color = new Color(1f, 1f, 1f, alpha * 0.85f);
                yield return null;
            }

            _flash.color = new Color(1f, 1f, 1f, 0f);
        }
    }
}
