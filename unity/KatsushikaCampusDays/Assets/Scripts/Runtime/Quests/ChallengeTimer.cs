using System;

namespace KCD
{
    /// <summary>制限時間つきステップの状態。</summary>
    public enum ChallengeState
    {
        /// <summary>まだ数えていない。受注前、またはセーブを読み込んだ直後で依頼主に話しかけるのを待っている。</summary>
        Idle,

        /// <summary>計時中。</summary>
        Running,

        /// <summary>時間切れで失敗した。依頼主に話しかけて再挑戦するまで、達成にはならない。</summary>
        Failed,

        /// <summary>時間内に達成した。</summary>
        Cleared
    }

    /// <summary>
    /// 制限時間つきステップの計時。UnityEngine に依存しないので EditMode テストから直接叩ける。
    /// Start で 1 回だけ数え始め、時間切れになったら Failed で止まる。勝手に数え直すことはなく、
    /// 再挑戦は呼び出し側が Start をもう一度呼ぶ。
    /// </summary>
    public sealed class ChallengeTimer
    {
        /// <summary>制限時間（秒）。</summary>
        public float Limit { get; private set; }

        /// <summary>経過秒。時間切れのときは Limit で止まる。</summary>
        public float Elapsed { get; private set; }

        public ChallengeState State { get; private set; } = ChallengeState.Idle;

        public bool IsRunning => State == ChallengeState.Running;

        public bool HasFailed => State == ChallengeState.Failed;

        /// <summary>残り秒。計時中は減っていき、時間切れなら 0、達成なら達成した瞬間の残り。</summary>
        public float Remaining => Math.Max(0f, Limit - Elapsed);

        /// <summary>はじめから数える。limit が 0 以下なら数えない（Idle のまま）。</summary>
        public void Start(float limit)
        {
            Limit = Math.Max(0f, limit);
            Elapsed = 0f;
            State = Limit > 0f ? ChallengeState.Running : ChallengeState.Idle;
        }

        /// <summary>
        /// 時間を進める。この呼び出しで時間切れになったときだけ true を返す。
        /// 計時中でなければ何もしない（時間切れのあとも Failed のまま）。
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (State != ChallengeState.Running || !(deltaTime > 0f))
            {
                return false;
            }

            Elapsed += deltaTime;
            if (Elapsed < Limit)
            {
                return false;
            }

            Elapsed = Limit;
            State = ChallengeState.Failed;
            return true;
        }

        /// <summary>時間内に達成した。計時中でなければ何もせず false。</summary>
        public bool Clear()
        {
            if (State != ChallengeState.Running)
            {
                return false;
            }

            State = ChallengeState.Cleared;
            return true;
        }

        /// <summary>数える前の状態に戻す。セーブの読み込みなどで使う。</summary>
        public void Reset()
        {
            Elapsed = 0f;
            State = ChallengeState.Idle;
        }
    }
}
