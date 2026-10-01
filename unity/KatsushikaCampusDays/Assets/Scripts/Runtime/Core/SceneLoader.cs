using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD
{
    /// <summary>
    /// タイトルとキャンパスの行き来を非同期で読む (#15)。GameManager（DontDestroyOnLoad）に付き、
    /// 読み込みの間は <see cref="LoadingOverlay"/> を出して操作を封鎖する。同期の LoadScene では、Web 版で
    /// 読み込みの間ずっと画面が固まっていた。
    ///
    /// 順番は同期の頃と同じ意味を保つ。
    /// ・読み始め: 残った封鎖をまとめて外し (#40)、自分の名前で封鎖し直す。頼まれていた自動セーブを捨てる (#61)。
    ///   タイトルへ戻るときは古いシーン（キャンパス）の時間を止める。同期の頃は読んだ瞬間に消えていたので、
    ///   読み込みの間に時計が進んで 24 時をまたいだり、チャイムが鳴ったりしないように。
    /// ・切り替えの直前: 読み込みの間に古いシーンが掛けた封鎖と自動セーブの依頼をもう一度捨てる（持ち主はこのあと消える）。
    ///   タイトルへ戻るときは timeScale を 1 に戻す。タイトルの Awake / Start は同期の頃と同じく 1 で走る。
    /// ・切り替えのあと: <see cref="SettleFrames"/> フレームのあいだ封鎖と画面を残してから外す。
    ///   キャンパスでは CampusDirector の最初の Update が「つづきから」の位置を当てる（SaveSystem.ApplyPending）。
    ///   GameManager.Update の自動セーブとどちらが先に走るかは決まっていないので、その間も封鎖して書かせない。
    ///   最初の数フレームのシェーダの読み込みも画面の裏で済む。
    ///
    /// 「つづきから」のセーブを読む SaveSystem.PrepareContinue は、呼ぶ側（TitleMenu）が読み始めの前に済ませる。
    ///
    /// 各段は public にしてある。EditMode では LoadSceneAsync を使えないので、テストは段を順に直接呼ぶ。
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        /// <summary>切り替えのあと、封鎖と画面を残すフレーム数。</summary>
        public const int SettleFrames = 2;

        /// <summary>allowSceneActivation = false のとき progress はここで止まる（Unity の仕様）。</summary>
        public const float ActivationProgress = 0.9f;

        private const float ProgressEpsilon = 0.0001f;

        /// <summary>まだ切り替わっていないことを表すフレーム番号。</summary>
        public const int NoFrame = -1;

        private enum Phase
        {
            Idle,
            Loading,
            Activating,
            Settling,
        }

        private static SceneLoader _current;

        private Phase _phase = Phase.Idle;
        private string _sceneName;
        private bool _toTitle;
        private int _loadedFrame = NoFrame;
        private int _startedFrame;
        private float _startedAt;
        private LoadingOverlay _overlay;
        private Coroutine _routine;

        /// <summary>
        /// どこかでシーンを読んでいる最中か（切り替えのあとの数フレームを含む）。ポーズメニューの Esc の門に使う。
        /// 持ち主が消えていれば false（テストの途中で消されても残らない）。
        /// </summary>
        public static bool IsAnyLoading => _current != null && _current.IsLoading;

        /// <summary>このローダーが読んでいる最中か。</summary>
        public bool IsLoading => _phase != Phase.Idle;

        /// <summary>切り替えを済ませ、封鎖を外すのを待っているところか。</summary>
        public bool IsSettling => _phase == Phase.Settling;

        /// <summary>読んでいるシーンの名前。読んでいなければ null。</summary>
        public string SceneName => IsLoading ? _sceneName : null;

        /// <summary>読み込み中の画面を出しているか。</summary>
        public bool OverlayVisible => _overlay != null && _overlay.IsVisible;

        /// <summary>読み込み中の画面のバーの伸び（0-1）。</summary>
        public float OverlayProgress => _overlay != null ? _overlay.Progress : 0f;

        /// <summary>読み込み中の画面の文字。</summary>
        public string OverlayLabel => _overlay != null ? _overlay.Label : string.Empty;

        private LoadingOverlay Overlay
        {
            get
            {
                if (_overlay == null || _overlay.Root == null)
                {
                    _overlay = LoadingOverlay.Create(transform);
                }

                return _overlay;
            }
        }

        // ---- 純関数（Unity を起動せずにテストする）----

        /// <summary>バーに出す値。progress は 0.9 で止まるので、0-0.9 を 0-1 に引き伸ばす。</summary>
        public static float DisplayProgress(float rawProgress)
        {
            return Mathf.Clamp01(rawProgress / ActivationProgress);
        }

        /// <summary>読み終えて、切り替えを許してよいか。</summary>
        public static bool ReadyToActivate(float rawProgress)
        {
            return rawProgress >= ActivationProgress - ProgressEpsilon;
        }

        /// <summary>切り替えたフレームから <see cref="SettleFrames"/> 進んだら封鎖と画面を外す。</summary>
        public static bool ShouldSettle(int loadedFrame, int currentFrame)
        {
            return loadedFrame >= 0 && currentFrame - loadedFrame >= SettleFrames;
        }

        // ---- 読み込み ----

        /// <summary>
        /// sceneName を非同期で読む。toTitle はタイトルへ戻るとき（古いシーンの時間を止め、切り替えの直前に 1 に戻す）。
        /// 読んでいる最中に呼ばれたら（二度押し）何もせず false。切り替えのあとの数フレームに呼ばれたら、
        /// 前の読み込みを仕上げてから新しく読む（新しいシーンの画面から次のシーンへ進む操作を捨てない）。
        /// シーンが読めなければ（Build Settings に無いなど。エラーは Unity が出す）封鎖と時間を戻して false。
        /// </summary>
        public bool Load(string sceneName, bool toTitle)
        {
            if (!Begin(sceneName, toTitle))
            {
                return false;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Abort();
                return false;
            }

            operation.allowSceneActivation = false;
            _routine = StartCoroutine(Drive(operation));
            return true;
        }

        private IEnumerator Drive(AsyncOperation operation)
        {
            while (!operation.isDone)
            {
                ReportProgress(operation.progress);
                if (_phase == Phase.Loading && ReadyToActivate(operation.progress))
                {
                    BeforeActivation();
                    operation.allowSceneActivation = true;
                }

                yield return null;
            }

            OnTargetLoaded(Time.frameCount);
            while (!TrySettle(Time.frameCount))
            {
                yield return null;
            }
        }

        /// <summary>
        /// 読み始め。読んでいる最中なら false（二度押しは 1 回だけ読む）。
        /// 残った封鎖を外して自分の名前で封鎖し、自動セーブの依頼を捨て、読み込み中の画面を出す。
        /// </summary>
        public bool Begin(string sceneName, bool toTitle)
        {
            if (_phase == Phase.Loading || _phase == Phase.Activating)
            {
                return false;
            }

            if (_phase == Phase.Settling)
            {
                Finish();
            }

            _phase = Phase.Loading;
            _sceneName = sceneName;
            _toTitle = toTitle;
            _loadedFrame = NoFrame;
            _startedFrame = Time.frameCount;
            _startedAt = Time.realtimeSinceStartup;
            _current = this;

            // 封鎖を掛けた画面・演出はシーンごと消えるので、残った封鎖をまとめて外す (#40)。
            // 読み込みの間は古いシーンがまだ動いているので、自分の名前で封鎖し直して移動・会話・セーブを止める。
            KCDInput.ClearAllBlocks();
            KCDInput.Block(this);

            if (toTitle)
            {
                Time.timeScale = 0f;
            }

            // 前のキャンパスで頼まれたまま書けなかった自動セーブを、次のキャンパスの最初のフレーム
            // （「つづきから」の位置を当てる前）に書かないよう捨てる (#61)。
            AutoSave.Cancel();

            Overlay.Show(0f);
            return true;
        }

        /// <summary>読み込みの進み具合（LoadSceneAsync の progress そのまま）をバーに出す。</summary>
        public void ReportProgress(float rawProgress)
        {
            if (_overlay != null)
            {
                _overlay.SetProgress(DisplayProgress(rawProgress));
            }
        }

        /// <summary>
        /// 切り替えを許す直前。読み込みの間に古いシーンが掛けた封鎖と頼んだ自動セーブを捨てる。
        /// タイトルへ戻るときは、タイトルが止まったまま始まらないよう timeScale を 1 に戻す。
        /// </summary>
        public void BeforeActivation()
        {
            if (_phase != Phase.Loading)
            {
                return;
            }

            _phase = Phase.Activating;
            KCDInput.ClearAllBlocks();
            KCDInput.Block(this);
            AutoSave.Cancel();

            if (_toTitle)
            {
                Time.timeScale = 1f;
            }

            ReportProgress(ActivationProgress);
        }

        /// <summary>
        /// 新しいシーンに切り替わった。ここからは新しいシーンの封鎖と自動セーブの依頼なので捨てない。
        /// 自分の封鎖と画面は <see cref="TrySettle"/> が外す。
        /// </summary>
        public void OnTargetLoaded(int frame)
        {
            if (_phase != Phase.Loading && _phase != Phase.Activating)
            {
                return;
            }

            _phase = Phase.Settling;
            _loadedFrame = frame;
            ReportProgress(ActivationProgress);
        }

        /// <summary>切り替えから <see cref="SettleFrames"/> 進んでいれば、自分の封鎖と画面を外して true。</summary>
        public bool TrySettle(int frame)
        {
            if (_phase != Phase.Settling || !ShouldSettle(_loadedFrame, frame))
            {
                return false;
            }

            _routine = null;
            Debug.Log("[KCD] scene_load scene=" + _sceneName
                      + " ms=" + Mathf.RoundToInt((Time.realtimeSinceStartup - _startedAt) * 1000f)
                      + " frames=" + (frame - _startedFrame));
            Finish();
            return true;
        }

        /// <summary>
        /// 読み込みをやめて元に戻す。シーンが読めなかったときと、読んでいる最中にローダーが消えるとき。
        /// 封鎖と画面を外し、タイトルへ戻る途中で時間を止めたままなら 1 に戻す（同期の頃の ReturnToTitle と同じ）。
        /// 切り替えの直前より後なら時間はもう戻してあるので、新しいシーンの timeScale には触らない。
        /// </summary>
        public void Abort()
        {
            if (_phase == Phase.Idle)
            {
                return;
            }

            if (_toTitle && _phase == Phase.Loading)
            {
                Time.timeScale = 1f;
            }

            Finish();
        }

        private void Finish()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _phase = Phase.Idle;
            _loadedFrame = NoFrame;
            KCDInput.Unblock(this);
            if (_overlay != null)
            {
                _overlay.Hide();
            }

            if (_current == this)
            {
                _current = null;
            }
        }

        private void OnDisable()
        {
            // コルーチンはここで止まるので、封鎖・止めた時間・画面を残さない。
            Abort();
        }

        private void OnDestroy()
        {
            KCDInput.Unblock(this);
            if (_current == this)
            {
                _current = null;
            }
        }
    }
}
