using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KCD.Tests
{
    /// <summary>
    /// 裏エンド「寮でぐーたら」を見た記録 (#41) の配線。結果が出たときに記録して称号「自主休講」を黙って獲得にし、
    /// 記録はセーブとは別のファイルに残る。DormEnding と GameManager.Instance を通るので EditMode では見られない。
    /// </summary>
    public sealed class DormEndingRecordPlayTests : SceneTestBase
    {
        private const string AchievementId = "ach_skip_school";

        /// <summary>結果が出る・称号が数え直されるのを待つ上限（実時間の秒）。暗転は 0.9 秒。</summary>
        private const float StepTimeoutSeconds = 20f;

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_DormEnding_RecordsTheEndingOnceTheResultAppears()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            GameManager manager = GameManager.Instance;
            DormEnding ending = Object.FindAnyObjectByType<DormEnding>();
            Assert.IsNotNull(ending, "キャンパスに DormEnding が無い（SceneBuilder → DormEndingFactory）");
            Assert.IsFalse(EndingProgress.Has(DormRoute.EndingId), "空の置き場なのに見たことになっている");
            Assert.IsFalse(manager.Achievements.IsUnlocked(AchievementId), "見ていないのに称号が取れている");

            ending.Play();
            Assert.IsFalse(ending.IsShowing, "暗転を待たずに結果が出た");
            Assert.IsFalse(EndingProgress.Has(DormRoute.EndingId), "暗転の途中で見たことにした");

            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!ending.IsShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(ending.IsShowing, StepTimeoutSeconds + " 秒待っても結果が出ない");
            Assert.IsTrue(EndingProgress.Has(DormRoute.EndingId), "結果が出たのに見たエンドを記録していない");
            Assert.IsTrue(manager.Achievements.IsUnlocked(AchievementId), "結果が出たのに称号「自主休講」が取れていない");
            Assert.IsTrue(File.Exists(SaveSystem.PathOf(EndingProgress.FileName)), "記録のファイルができていない");

            AssertNoErrors("寮の裏エンド");
        }

        [UnityTest]
        [Timeout(SceneTestTimeoutMs)]
        public IEnumerator Campus_AnEndingNoted_UnlocksTheAchievementOnTheNextRefresh()
        {
            CollectErrorsInsteadOfLogAssert();

            yield return PlayModeScenes.LoadCampusAsNewGame(Capture);
            AssertLoaded(GameManager.CampusSceneName);
            yield return PlayModeScenes.Advance(30, 1f);

            GameManager manager = GameManager.Instance;
            Assert.IsFalse(manager.Achievements.IsUnlocked(AchievementId), "見ていないのに称号が取れている");

            // GameManager は DayStats と EndingProgress の Version が動いたフレームだけ称号を数え直す。
            // 記録だけが変わっても、次の Update で取れること。
            Assert.IsTrue(EndingProgress.Note(DormRoute.EndingId));
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!manager.Achievements.IsUnlocked(AchievementId) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(manager.Achievements.IsUnlocked(AchievementId),
                "見たエンドを記録しても称号を数え直さない（GameManager.RefreshAchievements が EndingProgress.Version を見ていない）");

            AssertNoErrors("見たエンドの記録");
        }
    }
}
