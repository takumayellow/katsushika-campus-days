using UnityEngine;

namespace KCD
{
    /// <summary>
    /// 隠しアイテムに添えてあるメモ (#175)。拾ったとき、トーストのあとに会話の枠で 1 枚読む（話し手「メモ」、声は鳴らさない）。
    /// 隠しアイテムを 5 個以上拾っていて秘密のクエストがまだなら、会話データ note の t_hint
    /// （いなり先輩に聞いてみよう、という自分の一言）を続けて出す。
    /// </summary>
    public static class HiddenNote
    {
        /// <summary>メモの話し手と、ヒントを入れておく会話データの id。</summary>
        public const string DialogueId = "note";

        public const string HintTopicId = "t_hint";

        /// <summary>メモを読み集めると始まる秘密のクエスト。</summary>
        public const string SecretQuestId = "q_secret_spring_hunt";

        /// <summary>いなり先輩が宝探しの話をしてくれる、隠しアイテムの数。</summary>
        public const int HintThreshold = 5;

        /// <summary>メモの行の話者名（日本語）。note.json の speaker と同じにしておく。英語は ui.npc.note。</summary>
        public const string Speaker = "メモ";

        /// <summary>
        /// メモ 1 枚を会話の話題にする。「[番号/枚数]  本文」の 1 行に、hint があればその行を続ける。
        /// メモの無いアイテムなら null。
        /// </summary>
        public static DialogueTopic Build(CatalogItem item, int total, DialogueTopic hint)
        {
            if (item == null || !item.HasNote)
            {
                return null;
            }

            string number = "[" + item.NoteNo + "/" + total + "]  ";
            var topic = new DialogueTopic { Id = "note_" + item.Id };
            topic.Lines.Add(new DialogueLine
            {
                Speaker = Speaker,
                SpeakerKey = DialogueData.SpeakerKeyPrefix + DialogueId,
                Text = number + item.NoteJa,
                TextEn = string.IsNullOrEmpty(item.NoteEn) ? string.Empty : number + item.NoteEn,
                Written = true
            });

            if (hint != null)
            {
                topic.Lines.AddRange(hint.Lines);
            }

            return topic;
        }

        /// <summary>メモのあとにヒントを出すか。隠しアイテムが 5 個以上で、秘密のクエストを受けても終えてもいないとき。</summary>
        public static bool ShouldHint(int hiddenCount, QuestSystem quests)
        {
            return hiddenCount >= HintThreshold && quests != null && quests.Find(SecretQuestId) != null &&
                   !quests.IsActive(SecretQuestId) && !quests.IsCompleted(SecretQuestId);
        }

        /// <summary>拾ったアイテムのメモを会話の枠に出す。メモが無い・会話中なら何もしない。</summary>
        public static void ShowFor(string itemId)
        {
            DialogueSystem dialogue = DialogueSystem.Instance;
            CollectibleCatalog catalog = CollectibleCatalog.Instance;
            CatalogItem item = catalog?.FindItem(itemId);
            if (dialogue == null || item == null || !item.HasNote)
            {
                return;
            }

            QuestSystem quests = GameManager.Instance != null ? GameManager.Instance.Quests : null;
            DialogueTopic hint = ShouldHint(DayStats.HiddenCollectedCount(catalog), quests)
                ? dialogue.FindTopic(DialogueId, HintTopicId)
                : null;
            DialogueTopic topic = Build(item, catalog.HiddenCount, hint);
            if (topic != null && !dialogue.Play(topic))
            {
                Debug.LogWarning("[KCD] 会話中なのでメモを出せません: " + itemId);
            }
        }
    }
}
