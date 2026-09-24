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
        /// sceneLoaded を待ってから 1 フレーム進める。同期の LoadScene は Awake / OnEnable を読み込み中に、
        /// Start を sceneLoaded の後の最初の Update の前に呼ぶので、戻った時点で全員の Start が済んでいる。
        /// </summary>
        private static IEnumerator WaitForLoad(LogCapture capture)
        {
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!capture.SceneLoaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return null;
        }
    }
}
