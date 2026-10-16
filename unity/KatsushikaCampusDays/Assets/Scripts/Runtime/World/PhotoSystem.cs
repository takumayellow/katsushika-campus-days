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
    /// 写真スポットの三脚では、キャラが三脚の前へ駆けていって振り向き、三脚のカメラがその姿を撮る（記念写真, #170）。
    /// </summary>
    public sealed class PhotoSystem : MonoBehaviour
    {
        public const string FolderName = "Photos";

        /// <summary>
        /// 写真を置くフォルダの親を差し替える（テストとスモーク用）。null なら Application.persistentDataPath。
        /// Editor の persistentDataPath は Windows 版と同じフォルダなので、写真スポットで撮るテストがそのまま書くと、
        /// その端末で遊んで撮った写真の隣にテストの写真が溜まる。
        /// </summary>
        public static string DirectoryOverride { get; set; }

        [SerializeField] private GameObject _overlayRoot;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private Image _flash;
        [SerializeField] private float _flashTime = 0.35f;
        [SerializeField] private PhotoCutIn _cutIn;

        /// <summary>記念写真で、三脚の前の立ち位置まで走らせる時間の上限（秒）。着けなければその場で撮る。</summary>
        public const float RunUpSeconds = 2.5f;

        /// <summary>記念写真で、振り向いてから撮るまでの時間（秒）。手を振り始めたところで撮る。</summary>
        public const float PoseSeconds = 0.6f;

        private const float TurnSeconds = 0.2f;

        // 入っている間は時間を止め、カメラはプレイヤーの近くを自由に動かせる（PhotoFreeCamera, #12）。
        // E は自由カメラの上昇に使うので、撮影は Enter / Space / A / X。
        private readonly PhotoModeState _mode = new PhotoModeState();

        private bool _capturing;
        private int _count;
        private PhotoFreeCamera _freeCamera;
        private bool _hintsForGamepad;
        private PlayerController _selfieRunner;
        private bool _selfieBlocking;

        public static PhotoSystem Instance { get; private set; }

        /// <summary>撮影が終わったとき。引数は保存したファイル名。</summary>
        public static event Action<string> PhotoTaken;

        public bool IsActive => KCDInput.PhotoMode;

        /// <summary>このセッションで撮った枚数。リザルトが読む。</summary>
        public int Count => _count;

        /// <summary>記念写真を出すポラロイド。テストが見る。</summary>
        public PhotoCutIn CutIn => _cutIn;

        /// <summary>撮影の途中か（記念写真なら走り出しから保存まで）。</summary>
        public bool IsCapturing => _capturing;

        /// <summary>最後の記念写真の構図。まだ撮っていなければ null。テストが見る。</summary>
        public SelfiePose? LastSelfie { get; private set; }

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(GameObject overlayRoot, TMP_Text hint, Image flash)
        {
            _overlayRoot = overlayRoot;
            _hintLabel = hint;
            _flash = flash;
        }

        /// <summary>SceneBuilder から差し込む。記念写真のポラロイドつき。</summary>
        public void Bind(GameObject overlayRoot, TMP_Text hint, Image flash, PhotoCutIn cutIn)
        {
            Bind(overlayRoot, hint, flash);
            _cutIn = cutIn;
        }

        private void Awake()
        {
            Instance = this;
            _freeCamera = GetComponent<PhotoFreeCamera>();
            if (_freeCamera == null)
            {
                _freeCamera = gameObject.AddComponent<PhotoFreeCamera>();
            }
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

        private void OnDisable()
        {
            // シーンを閉じるときなど。止めた時間と肩越しカメラを戻す（UI と PhotoMode は OnDestroy の後始末に任せる）。
            if (_freeCamera != null)
            {
                _freeCamera.End();
            }

            _mode.Release();
            EndSelfie();
            _capturing = false;
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
                    if (_cutIn != null)
                    {
                        _cutIn.Hide();
                    }

                    _hintsForGamepad = KCDInput.PhotoPressedOnGamepad;
                    _mode.Enter(HUD.Instance != null ? HUD.Instance.GameplayRoot : null);
                    SetActive(true);
                    if (_freeCamera != null)
                    {
                        _freeCamera.Begin();
                    }
                }

                return;
            }

            if (KCDInput.PhotoPressed || KCDInput.MenuPressed || KCDInput.CancelPressed)
            {
                if (_freeCamera != null)
                {
                    _freeCamera.End();
                }

                SetActive(false);
                _mode.Exit();
                KCDInput.MarkModalClosed();
                return;
            }

            if (KCDInput.PhotoHintTogglePressed)
            {
                SetOverlay(_overlayRoot != null && !_overlayRoot.activeSelf);
            }

            if (KCDInput.PhotoShutterPressed)
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

        /// <summary>
        /// 写真スポットの記念写真 (#170)。subject（プレイヤー）を三脚 tripod から lookDir の向き（ふさがっていれば横へ回した向き）へ走らせ、
        /// 三脚のほうを向いて手を振ったところを、三脚のカメラで撮る。構図は collectibles.json の selfie。
        /// subject が PlayerController を持たなければ走らせずにその場で、null なら立ち位置を撮る。
        /// </summary>
        public void TakeSelfieAt(string spotId, Transform tripod, Vector2 lookDir, GameObject subject)
        {
            // 非アクティブ時の StartCoroutine は例外になる。
            if (!_capturing && isActiveAndEnabled && tripod != null && !string.IsNullOrEmpty(spotId))
            {
                StartCoroutine(Selfie(spotId, tripod, lookDir, subject));
            }
        }

        /// <summary>
        /// 撮影のトーストに出す名前。写真スポットならその名前（「メインストリートの見通し」など）、
        /// 一覧に無い id ならその id、自由撮影ならファイル名。
        /// </summary>
        public static string LabelFor(string spotId, string fileName)
        {
            if (string.IsNullOrEmpty(spotId))
            {
                return fileName ?? string.Empty;
            }

            CatalogPhotoSpot spot = CollectibleCatalog.Instance?.FindPhotoSpot(spotId);
            return spot != null ? spot.DisplayName : spotId;
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
                _hintLabel.text = PhotoModeHints.Text(L.IsEnglish, _hintsForGamepad, InputMode.IsTouch);
            }
        }

        private IEnumerator Capture(string spotId)
        {
            _capturing = true;
            bool overlayWasVisible = _overlayRoot != null && _overlayRoot.activeSelf;
            SetOverlay(false);
            yield return EndOfFrame();

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

            HUD.Instance?.ShowToast(L.Format("ui.hud.photo_taken", LabelFor(spotId, fileName)));
            DayStats.NotePhoto(string.IsNullOrEmpty(spotId) ? fileName : spotId);
            PhotoTaken?.Invoke(string.IsNullOrEmpty(spotId) ? fileName : spotId);
            _capturing = false;
        }

        private IEnumerator Selfie(string spotId, Transform tripodRoot, Vector2 lookDir, GameObject subject)
        {
            Vector3 tripod = tripodRoot.position;
            // 途中で例外になっても（振り向く間にプレイヤーが消えた等）、封鎖と HUD を戻して次を撮れるようにする。
            _capturing = true;
            try
            {
                if (_cutIn != null)
                {
                    _cutIn.Hide();
                }

                _selfieBlocking = true;
                KCDInput.Block(this);
                HUD.Instance?.SetGameplayUIVisible(false);

                SelfieComposition composition = CollectibleCatalog.Instance?.FindPhotoSpot(spotId)?.Selfie
                                                ?? new SelfieComposition();
                Transform body = subject != null ? subject.transform : null;
                float turn = SelfieFraming.ChooseTurn(composition, tripod, lookDir, tripodRoot, body);
                Vector3 standPoint = composition.StandPoint(tripod, lookDir, turn);
                PlayerController runner = subject != null ? subject.GetComponent<PlayerController>() : null;
                if (runner != null)
                {
                    yield return RunUp(runner, standPoint);
                }

                Vector3 feet = body != null ? body.position : standPoint;
                if (body != null)
                {
                    yield return TurnTo(body, SelfieComposition.FacingYaw(feet, tripod, body.eulerAngles.y));
                    PlayerAnimatorDriver animator = body != null ? body.GetComponent<PlayerAnimatorDriver>() : null;
                    if (animator != null)
                    {
                        animator.PlayWave();
                    }

                    yield return new WaitForSecondsRealtime(PoseSeconds);
                    feet = body != null ? body.position : feet;
                }

                SelfiePose pose = SelfieFraming.Aim(composition, tripod, feet, turn, body);
                LastSelfie = pose;
                SelfieFraming.RevealSubject();
                string fileName = FileNameFor(DateTime.Now);
                Texture2D shot = null;
                bool saved;
                try
                {
                    shot = SelfieCamera.Shoot(pose.Position, pose.Rotation, pose.FieldOfView, tripodRoot);
                    saved = shot != null && SaveTexture(fileName, shot);
                }
                catch (Exception error)
                {
                    saved = false;
                    Debug.LogWarning("[KCD] 記念写真の保存に失敗: " + error.Message);
                }

                EndSelfie();
                if (!saved)
                {
                    if (shot != null)
                    {
                        Destroy(shot);
                    }

                    HUD.Instance?.ShowToast(L.Pick("写真を保存できませんでした", "Could not save the photo"));
                    yield break;
                }

                Record(spotId, fileName);
                if (_cutIn != null)
                {
                    _cutIn.Show(shot, CutInCaption(spotId, fileName));
                }
                else
                {
                    Destroy(shot);
                }

                yield return Flash();
            }
            finally
            {
                EndSelfie();
                _capturing = false;
            }
        }

        /// <summary>立ち位置まで走らせる。RunUpSeconds で着けなければ（壁や段差で止まったら）そこまで。</summary>
        private IEnumerator RunUp(PlayerController runner, Vector3 standPoint)
        {
            _selfieRunner = runner;
            if (runner.IsSitting)
            {
                runner.StandUp();
            }

            runner.SetAutopilot(standPoint, runner.RunSpeed);
            float until = Time.unscaledTime + RunUpSeconds;
            while (runner != null && runner.AutopilotActive && Time.unscaledTime < until)
            {
                yield return null;
            }

            if (runner != null)
            {
                runner.ClearAutopilot();
            }

            _selfieRunner = null;
        }

        private static IEnumerator TurnTo(Transform body, float yaw)
        {
            float from = body.eulerAngles.y;
            float elapsed = 0f;
            while (elapsed < TurnSeconds && body != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / TurnSeconds);
                body.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(from, yaw, t), 0f);
                yield return null;
            }

            if (body != null)
            {
                body.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        /// <summary>記念写真の途中で止めたときも、走らせたプレイヤーと封鎖と HUD を戻す。</summary>
        private void EndSelfie()
        {
            if (_selfieRunner != null)
            {
                _selfieRunner.ClearAutopilot();
                _selfieRunner = null;
            }

            if (_selfieBlocking)
            {
                _selfieBlocking = false;
                KCDInput.Unblock(this);
                HUD.Instance?.SetGameplayUIVisible(true);
            }
        }

        /// <summary>撮れた 1 枚を数え、知らせる。</summary>
        private void Record(string spotId, string fileName)
        {
            _count++;
            AudioManager.Instance?.PlaySe("camera_shutter");
            string id = string.IsNullOrEmpty(spotId) ? fileName : spotId;
            HUD.Instance?.ShowToast(L.Format("ui.hud.photo_taken", LabelFor(spotId, fileName)));
            DayStats.NotePhoto(id);
            PhotoTaken?.Invoke(id);
        }

        /// <summary>ポラロイドの下の 2 行。1 行目はスポットの名前、2 行目は何日目の何時か。</summary>
        public static string CutInCaption(string spotId, string fileName)
        {
            GameManager manager = GameManager.Instance;
            int day = manager != null ? manager.DayNumber : DayRestart.FirstDay;
            float hours = manager != null ? manager.GameTimeHours : 12f;
            return LabelFor(spotId, fileName) + "\n" + PhotoCutIn.Stamp(day, hours, L.IsEnglish);
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

            string folder = Path.Combine(DirectoryOverride ?? Application.persistentDataPath, FolderName);
            Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, fileName));
            return true;
        }

        /// <summary>描いた写真を PNG で置く（置き場所は SavePhoto と同じ）。置けなければ false か例外。</summary>
        private static bool SaveTexture(string fileName, Texture2D shot)
        {
            byte[] png = shot.EncodeToPNG();
            if (WebDownload.Supported)
            {
                return WebDownload.Send(fileName, png, "image/png");
            }

            string folder = Path.Combine(DirectoryOverride ?? Application.persistentDataPath, FolderName);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), png);
            return true;
        }

        /// <summary>
        /// 描画が終わるまで待つ。batchmode の Editor（PlayMode テスト）では WaitForEndOfFrame が来ず、
        /// 撮影が止まったままになるので、次のフレームまで待つ。CaptureScreenshot は呼んだフレームの描画の終わりに
        /// 撮るので、デスクトップではそれでも撮影の画は変わらない。
        /// </summary>
        private static object EndOfFrame()
        {
            return Application.isBatchMode ? null : new WaitForEndOfFrame();
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
