using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// PlayMode テストのシーンの読み方 (#67)。待つだけで Assert はしない（判定はテスト本体と SceneTestBase が行う）。
    /// </summary>
    public static class PlayModeScenes
    {
        /// <summary>シーンの読み込みを待つ上限（実時間の秒）。Campus は重いので長め。</summary>
        public const float LoadTimeoutSeconds = 180f;

        /// <summary>タイトルを読む。capture.SceneLoaded が true になるか、上限まで待つ。</summary>
        public static IEnumerator LoadTitle(LogCapture capture)
        {
            capture.Watch(GameManager.TitleSceneName);
            SceneManager.LoadScene(GameManager.TitleSceneName);
            yield return WaitForLoad(capture);
        }

        /// <summary>タイトルの「はじめから」と同じ手順でキャンパスへ入る（朝 8:30・1 日目・入場前）。</summary>
        public static IEnumerator LoadCampusAsNewGame(LogCapture capture)
        {
            GameManager manager = GameManager.Instance;
            manager.BeginNewGame();
            capture.Watch(GameManager.CampusSceneName);
            manager.EnterCampus();
            yield return WaitForLoad(capture);
        }

        /// <summary>少なくとも frames フレーム、かつ実時間で realSeconds 秒進める。</summary>
        public static IEnumerator Advance(int frames, float realSeconds)
        {
            float until = Time.realtimeSinceStartup + realSeconds;
            for (int i = 0; i < frames || Time.realtimeSinceStartup < until; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// sceneLoaded を待ち、GameManager の非同期の読み込み（SceneLoader, #15）が封鎖と「読み込み中」の画面を
        /// 外すまで待ってから 1 フレーム進める。Awake / OnEnable は切り替えの中で、Start は sceneLoaded の後の
        /// 最初の Update の前に走り、ローダーは切り替えから数フレーム後に外すので、戻った時点で全員の Start と
        /// 最初の Update（CampusDirector の「つづきから」の位置合わせ）が済んでいる。
        /// タイトルを同期の LoadScene で読むときはローダーは動いていないので、sceneLoaded の後の 1 フレームだけ進める。
        /// 上限まで待っても外れなければそのまま戻る（封鎖が残ったことはテスト本体の Assert が落とす）。
        /// </summary>
        private static IEnumerator WaitForLoad(LogCapture capture)
        {
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!capture.SceneLoaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            while (SceneLoader.IsAnyLoading && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return null;
        }
    }
}
