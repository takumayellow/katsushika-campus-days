using UnityEngine;

namespace KCD
{
    /// <summary>
    /// モブの枠の貸し借り。シーンに焼き込んだ人数（Capacity）を超えては出さず、
    /// 同時に出す人数も MaxActive で切る。実行中に Instantiate / Destroy をしないための帳簿。
    /// 消えた枠は cooldown 秒たつまで貸さない（同じ人が入口から出た直後に門から現れないように）。
    /// </summary>
    public sealed class MobPool
    {
        private readonly bool[] _active;
        private readonly float[] _readyAt;
        private int _cursor;

        public MobPool(int capacity, int maxActive)
        {
            capacity = Mathf.Max(0, capacity);
            _active = new bool[capacity];
            _readyAt = new float[capacity];
            MaxActive = Mathf.Clamp(maxActive, 0, capacity);
        }

        /// <summary>シーンにある枠の数。</summary>
        public int Capacity => _active.Length;

        /// <summary>同時に貸し出せる上限。</summary>
        public int MaxActive { get; }

        /// <summary>いま貸し出している枠の数。</summary>
        public int ActiveCount { get; private set; }

        public bool IsActive(int slot)
        {
            return slot >= 0 && slot < _active.Length && _active[slot];
        }

        /// <summary>空いている枠を 1 つ借りる。上限に達しているか、空きがまだ冷めていなければ -1。</summary>
        public int Acquire(float now)
        {
            if (ActiveCount >= MaxActive)
            {
                return -1;
            }

            int capacity = _active.Length;
            for (int i = 0; i < capacity; i++)
            {
                int slot = (_cursor + i) % capacity;
                if (_active[slot] || now < _readyAt[slot])
                {
                    continue;
                }

                _active[slot] = true;
                ActiveCount++;
                _cursor = (slot + 1) % capacity;
                return slot;
            }

            return -1;
        }

        /// <summary>枠を返す。cooldown 秒たつまでは次に貸さない。借りていない枠なら false。</summary>
        public bool Release(int slot, float now, float cooldown)
        {
            if (!IsActive(slot))
            {
                return false;
            }

            _active[slot] = false;
            ActiveCount--;
            _readyAt[slot] = now + Mathf.Max(0f, cooldown);
            return true;
        }
    }
}
