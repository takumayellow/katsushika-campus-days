using UnityEngine;

namespace KCD
{
    /// <summary>制限時間つきステップを翌朝どう扱うか。</summary>
    public enum DayTimerAction
    {
        /// <summary>そのまま。完了済み、または数えていないもの。</summary>
        Keep,

        /// <summary>数える前に戻す。依頼主に話しかけたら再挑戦になる。</summary>
        Reset,

        /// <summary>その場で数え直す。依頼主がいないので、待っていると二度と数え始めない。</summary>
        Restart
    }

    /// <summary>
    /// 「もう一日歩く」で翌朝から始め直すときの判定。
    /// その場で朝になると時間だけ飛んだように見えるので、最初と同じスポーン（正門側）へ戻してから朝にする (#16)。
    /// ここには UnityEngine の Vector3 しか使わない判定だけを置き、暗転やワープは DayEndEvaluator が持つ。
    /// </summary>
    public static class DayRestart
    {
        /// <summary>一日の始まりの時刻。DayNightCycle の _startHour と揃えてある。</summary>
        public const float DayStartHour = 8.5f;

        /// <summary>初日の番号。</summary>
        public const int FirstDay = 1;

        /// <summary>屋内から外へ出るのを待つ上限（実時間の秒）。暗転が重なっても待ち続けない。</summary>
        public const float ExitTimeoutSeconds = 2f;

        /// <summary>翌日の番号。数え始める前（0 や負）でも 2 日目から始める。</summary>
        public static int NextDay(int day)
        {
            return day < FirstDay ? FirstDay + 1 : day + 1;
        }

        /// <summary>屋内から外へ戻す必要があるか。出入り係がいなければ何もしない。</summary>
        public static bool NeedsInteriorExit(bool hasLoader, bool isInside)
        {
            return hasLoader && isInside;
        }

        /// <summary>戻し先。スポーンを覚えていなければ、その場に置いたままにする。</summary>
        public static Vector3 ReturnPoint(bool hasSpawn, Vector3 spawn, Vector3 current)
        {
            return hasSpawn ? spawn : current;
        }

        /// <summary>戻したあとの向き。スポーンを覚えていなければ今の向きのまま。</summary>
        public static float ReturnYaw(bool hasSpawn, float spawnYaw, float currentYaw)
        {
            return hasSpawn ? spawnYaw : currentYaw;
        }

        /// <summary>
        /// 制限時間つきステップの朝の扱い。数えかけ（running）と時間切れ（failed）だけが 1 日ごとの状態で、
        /// 完了済みと、依頼主を待っている Idle はそのまま残す。
        /// </summary>
        public static DayTimerAction TimerAction(bool timed, bool completed, bool running, bool failed, bool hasGiver)
        {
            if (!timed || completed || !(running || failed))
            {
                return DayTimerAction.Keep;
            }

            return hasGiver ? DayTimerAction.Reset : DayTimerAction.Restart;
        }
    }
}
