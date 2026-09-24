namespace KCD
{
    /// <summary>
    /// クエストの報酬の収集物（rewardType = collectible）を、拾った物として DayStats に記録する (#65)。
    /// 以前は rewardText のトーストを出すだけで、c_greenhouse_sprout・c_sora_charm・c_inari_bookmark は
    /// どこにも記録されなかった。称号のレア度の条件（rarity_count）はクエストの報酬も数える。
    /// 結果画面の隠しアイテムの数には入らない（CollectibleCatalog.CountHidden は source = hidden だけを数える）。
    /// </summary>
    public static class QuestRewards
    {
        /// <summary>報酬が収集物なら記録して true。</summary>
        public static bool Grant(QuestData quest)
        {
            if (quest == null || quest.RewardType != QuestData.RewardCollectible || string.IsNullOrEmpty(quest.RewardId))
            {
                return false;
            }

            DayStats.NoteCollect(quest.RewardId);
            return true;
        }

        /// <summary>
        /// 達成済みのクエストの報酬を全部記録する。何度呼んでも同じ（DayStats は同じ id を 1 回だけ数える）。
        /// セーブを読んだあと（QuestSystem.Restore はクエスト達成のイベントを出さない）もこれで追いつく。
        /// 記録したクエストの数を返す。
        /// </summary>
        public static int GrantCompleted(QuestSystem quests)
        {
            if (quests == null)
            {
                return 0;
            }

            int granted = 0;
            for (int i = 0; i < quests.All.Count; i++)
            {
                QuestData quest = quests.All[i];
                if (quests.IsCompleted(quest.Id) && Grant(quest))
                {
                    granted++;
                }
            }

            return granted;
        }
    }
}
