using System;
using System.Collections.Generic;
using System.Globalization;

namespace KCD
{
    /// <summary>
    /// NPC ごとの好感度と、答えた選択肢の話題 (#9)。会話の選択肢で上がり、話題の requiresAffinity が見る。
    /// DayStats と同じく周回単位で、新規ゲームで消し、セーブに載せてつづきからで戻す。
    /// </summary>
    public static class NpcAffinity
    {
        /// <summary>好感度の上限。下限は 0。</summary>
        public const int Max = 10;

        private const char Separator = '=';

        private static readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _answered = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>npcId（会話データの id）の好感度。知らない NPC は 0。</summary>
        public static int Get(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && _values.TryGetValue(npcId, out int value) ? value : 0;
        }

        /// <summary>好感度を delta だけ動かす。0 から <see cref="Max"/> の間に収める。id が空なら何もしない。</summary>
        public static void Add(string npcId, int delta)
        {
            if (string.IsNullOrEmpty(npcId) || delta == 0)
            {
                return;
            }

            Set(npcId, Get(npcId) + delta);
        }

        /// <summary>選択肢に答えた話題（「NPC id/話題 id」）か。答えた話題は二度出さない。</summary>
        public static bool HasAnswered(string topicKey)
        {
            return !string.IsNullOrEmpty(topicKey) && _answered.Contains(topicKey);
        }

        /// <summary>選択肢に答えた話題として覚える。</summary>
        public static void NoteAnswered(string topicKey)
        {
            if (!string.IsNullOrEmpty(topicKey))
            {
                _answered.Add(topicKey);
            }
        }

        /// <summary>タイトルから新規に始めたときに呼ぶ。</summary>
        public static void Reset()
        {
            _values.Clear();
            _answered.Clear();
        }

        /// <summary>セーブ用。0 でない好感度を "id=値" の形で、id の順に並べる。</summary>
        public static List<string> SortedValues()
        {
            var ids = new List<string>(_values.Keys);
            ids.Sort(StringComparer.Ordinal);
            var entries = new List<string>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                entries.Add(ids[i] + Separator + _values[ids[i]].ToString(CultureInfo.InvariantCulture));
            }

            return entries;
        }

        /// <summary>セーブ用。答えた話題。</summary>
        public static List<string> SortedAnswered()
        {
            var list = new List<string>(_answered);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// セーブから戻す。今の記録は捨てて置き換える。null のリストは空、形の壊れた項目は読み飛ばし、
        /// 値は 0 から <see cref="Max"/> の間に収める。
        /// </summary>
        public static void Restore(IEnumerable<string> values, IEnumerable<string> answered)
        {
            Reset();
            if (values != null)
            {
                foreach (string entry in values)
                {
                    if (TryParseEntry(entry, out string id, out int value))
                    {
                        Set(id, value);
                    }
                }
            }

            if (answered != null)
            {
                foreach (string key in answered)
                {
                    NoteAnswered(key);
                }
            }
        }

        /// <summary>"id=値" を読む。id が空、= が無い、値が整数でなければ false。</summary>
        public static bool TryParseEntry(string entry, out string id, out int value)
        {
            id = string.Empty;
            value = 0;
            if (string.IsNullOrEmpty(entry))
            {
                return false;
            }

            int split = entry.LastIndexOf(Separator);
            if (split <= 0 || !int.TryParse(entry.Substring(split + 1), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            id = entry.Substring(0, split);
            return true;
        }

        private static void Set(string npcId, int value)
        {
            int clamped = Math.Max(0, Math.Min(Max, value));
            if (clamped == 0)
            {
                _values.Remove(npcId);
            }
            else
            {
                _values[npcId] = clamped;
            }
        }
    }
}
