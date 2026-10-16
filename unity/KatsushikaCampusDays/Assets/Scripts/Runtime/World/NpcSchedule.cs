using System;
using System.Collections.Generic;
using UnityEngine;

namespace KCD
{
    /// <summary>
    /// NPC の居場所を時間帯で変える (#9)。教授は午前は講義棟の前、午後は研究室のある第2研究棟の前にいる。
    /// 時刻は GameManager.GameTimeHours。時間帯が変わったとき、プレイヤーが近くにいれば歩いて移り、
    /// 遠くにいれば（屋内にいるときも含む）一瞬で移る。シーンに入った直後とロードの直後も一瞬で移る。
    /// </summary>
    [RequireComponent(typeof(NPCWander))]
    public sealed class NpcSchedule : MonoBehaviour
    {
        /// <summary>プレイヤーがこれより近ければ、時間帯が変わったときに歩いて移る（m）。</summary>
        public const float WalkVisibleDistance = 40f;

        /// <summary>時間帯を確かめる間隔（秒）。</summary>
        public const float CheckIntervalSeconds = 0.5f;

        [SerializeField] private float[] _hours = Array.Empty<float>();
        [SerializeField] private Vector3[] _spots = Array.Empty<Vector3>();
        [SerializeField] private string _holdUntilQuest = string.Empty;

        private NPCWander _wander;
        private Transform _player;
        private int _slot = -1;
        private float _nextCheckAt;

        /// <summary>
        /// 時間帯の始まりの時刻（昇順）と、そのときの居場所（hours と同じ数）。hold が空でなければ、そのクエストを終えるまで
        /// 最初の居場所にとどまる（教授は「体育館まで 60 秒」を講義棟の前から出すので、それが済むまで動かない）。
        /// </summary>
        public void Configure(float[] hours, Vector3[] spots, string holdUntilQuest)
        {
            _hours = hours ?? Array.Empty<float>();
            _spots = spots ?? Array.Empty<Vector3>();
            _holdUntilQuest = holdUntilQuest ?? string.Empty;
        }

        /// <summary>いまの居場所の番号。まだ一度も決めていなければ -1。</summary>
        public int Slot => _slot;

        /// <summary>
        /// その時刻の時間帯の番号。始まりの時刻が hour 以下のもののうち最後のもの。
        /// どれより早い（夜中に日付をまたいだ）ときは、前の日の続きとして最後の時間帯。
        /// </summary>
        public static int SlotAt(IReadOnlyList<float> hours, float hour)
        {
            if (hours == null || hours.Count == 0)
            {
                return -1;
            }

            for (int i = hours.Count - 1; i >= 0; i--)
            {
                if (hours[i] <= hour)
                {
                    return i;
                }
            }

            return hours.Count - 1;
        }

        /// <summary>
        /// いまの居場所の番号。hold のクエストがあって済んでいなければ最初の居場所（0）、
        /// そうでなければ時刻で決まる時間帯。
        /// </summary>
        public static int CurrentSlot(IReadOnlyList<float> hours, float hour, string holdUntilQuest, Func<string, bool> isCompleted)
        {
            if (!string.IsNullOrEmpty(holdUntilQuest) && (isCompleted == null || !isCompleted(holdUntilQuest)))
            {
                return hours != null && hours.Count > 0 ? 0 : -1;
            }

            return SlotAt(hours, hour);
        }

        /// <summary>移るときに一瞬で移るか。最初の 1 回と、プレイヤーが遠くにいるとき。</summary>
        public static bool ShouldWarp(bool first, bool hasPlayer, Vector3 player, Vector3 npc)
        {
            if (first || !hasPlayer)
            {
                return true;
            }

            Vector3 delta = player - npc;
            delta.y = 0f;
            return delta.sqrMagnitude > WalkVisibleDistance * WalkVisibleDistance;
        }

        private void Awake()
        {
            _wander = GetComponent<NPCWander>();
        }

        private void Update()
        {
            if (Time.time < _nextCheckAt)
            {
                return;
            }

            _nextCheckAt = Time.time + CheckIntervalSeconds;
            GameManager manager = GameManager.Instance;
            if (manager == null || _spots.Length == 0 || _hours.Length != _spots.Length)
            {
                return;
            }

            QuestSystem quests = manager.Quests;
            int slot = CurrentSlot(_hours, manager.GameTimeHours, _holdUntilQuest,
                quests != null ? (Func<string, bool>)quests.IsCompleted : null);
            if (slot == _slot || slot < 0 || _wander.Paused)
            {
                // 話している相手を置いて行かない。会話が済んでから移る。
                return;
            }

            if (_player == null)
            {
                PlayerController controller = FindAnyObjectByType<PlayerController>();
                _player = controller != null ? controller.transform : null;
            }

            bool warp = ShouldWarp(_slot < 0, _player != null,
                _player != null ? _player.position : Vector3.zero, transform.position);
            _slot = slot;
            _wander.Relocate(_spots[slot], warp);
        }
    }
}
