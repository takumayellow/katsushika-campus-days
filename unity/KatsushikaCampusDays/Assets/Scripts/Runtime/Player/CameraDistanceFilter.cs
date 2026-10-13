using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 遮蔽でカメラを寄せたあとの距離の出し方 (#12)。
    /// 寄るときはそのフレームで寄る（壁の中を 1 フレームも写さない）。
    /// 遮蔽が消えたら <see cref="PullInHold"/> 秒だけ寄せたまま待ち、そこから <see cref="RecoverTime"/> の時定数で戻す。
    /// 木の幹や柱の脇を通るたびに、寄ったり戻ったりを細かく繰り返さないための待ち。
    /// 遮られていないときの距離の変化（ホイールのズーム）はそのまま通す。
    /// </summary>
    public sealed class CameraDistanceFilter
    {
        /// <summary>遮られたと見なす、望む距離との差（m）。</summary>
        public const float ObstructionEpsilon = 0.001f;

        /// <summary>戻り切ったと見なして目標にそろえる差（m）。</summary>
        public const float SnapDistance = 0.01f;

        private float _current;
        private float _hold;
        private bool _recovering;
        private bool _initialized;

        /// <summary>遮蔽が消えてから戻り始めるまでの秒数。</summary>
        public float PullInHold { get; set; } = 0.2f;

        /// <summary>戻るときの時定数（秒）。この秒数で残りの約 63% を戻す。</summary>
        public float RecoverTime { get; set; } = 0.3f;

        /// <summary>最後に返した距離。</summary>
        public float Current => _current;

        /// <summary>遮蔽で寄せた距離から戻っている途中か（待ちの間も含む）。</summary>
        public bool IsRecovering => _recovering;

        /// <summary>次の Step をそのフレームの許される距離から始め直す。ワープやカットのあとに呼ぶ。</summary>
        public void Reset()
        {
            _initialized = false;
            _hold = 0f;
            _recovering = false;
        }

        /// <summary>
        /// 1 フレーム分進めて、使う距離を返す。desired は望む距離、allowed は遮蔽から見て置いてよい距離
        /// （<see cref="CameraMath.AllowedDistance"/>）。deltaTime が負ならリセットして allowed をそのまま使う。
        /// deltaTime が 0（ポーズ中）でも寄るのは即時で、戻るのは止まる。
        /// </summary>
        public float Step(float desired, float allowed, float deltaTime)
        {
            desired = Mathf.Max(0f, desired);
            allowed = Mathf.Clamp(allowed, 0f, desired);
            bool obstructed = allowed < desired - ObstructionEpsilon;

            if (!_initialized || deltaTime < 0f)
            {
                _initialized = true;
                _current = allowed;
                _hold = 0f;
                _recovering = obstructed;
                return _current;
            }

            if (allowed <= _current)
            {
                // 寄る（または同じ所に留まる）。遮られている間は待ちを取り直す。
                _current = allowed;
                if (obstructed)
                {
                    _hold = Mathf.Max(0f, PullInHold);
                    _recovering = true;
                }
                else
                {
                    _hold = 0f;
                    _recovering = false;
                }

                return _current;
            }

            if (!_recovering)
            {
                // 遮蔽で寄せていないときの伸び（ズームアウト）はそのまま。
                _current = allowed;
                return _current;
            }

            if (_hold > 0f)
            {
                _hold -= Mathf.Max(0f, deltaTime);
                return _current;
            }

            float t = RecoverTime <= 0f ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / RecoverTime);
            _current = Mathf.Lerp(_current, allowed, t);
            if (allowed - _current <= SnapDistance)
            {
                _current = allowed;
            }

            if (!obstructed && _current >= desired - ObstructionEpsilon)
            {
                _recovering = false;
            }

            return _current;
        }
    }
}
