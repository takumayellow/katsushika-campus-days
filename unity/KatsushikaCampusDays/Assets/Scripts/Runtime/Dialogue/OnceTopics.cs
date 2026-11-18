using System;
using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 使い終えた一度きり（once）の話題（「会話データの id/話題 id」）(#395)。会話の選び方がこれを見て同じ話題を二度出さない。
    /// NpcAffinity と同じく周回単位で、新規ゲームで消し、セーブに載せてつづきからで戻す。
    /// </summary>
    public static class OnceTopics
    {
        private static readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>今の記録そのもの。<see cref="DialogueSystem.SelectTopic(DialogueData, QuestSystem, ICollection{string})"/> に渡す。</summary>
        public static ICollection<string> Keys => _keys;

        /// <summary>使い終えた話題として覚える。空なら何もしない。</summary>
        public static void Note(string topicKey)
        {
            if (!string.IsNullOrEmpty(topicKey))
            {
                _keys.Add(topicKey);
            }
        }

        /// <summary>タイトルから新規に始めたときに呼ぶ。</summary>
        public static void Reset()
        {
            _keys.Clear();
        }

        /// <summary>セーブ用。キーの順に並べる。</summary>
        public static List<string> Sorted()
        {
            var list = new List<string>(_keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>セーブから戻す。今の記録は捨てて置き換える。null のリストは空、空のキーは読み飛ばす。</summary>
        public static void Restore(IEnumerable<string> keys)
        {
            Reset();
            if (keys == null)
            {
                return;
            }

            foreach (string key in keys)
            {
                Note(key);
            }
        }
    }
}
