namespace KCD
{
    /// <summary>話者名から文字送りの音（talk_blip_*）を決める。</summary>
    public static class DialogueVoice
    {
        /// <summary>プレイアブルキャラクターの id から。</summary>
        public static string ForCharacter(string characterId)
        {
            switch (characterId)
            {
                case "botchan":
                    return "talk_blip_m1";
                case "madonna":
                    return "talk_blip_f2";
                default:
                    return "talk_blip_f1";
            }
        }

        /// <summary>会話データの話者名から。空・「自分」は操作キャラクターの声。</summary>
        public static string ForSpeaker(string speaker, string playerCharacterId)
        {
            if (string.IsNullOrEmpty(speaker) || speaker == "自分")
            {
                return ForCharacter(playerCharacterId);
            }

            if (speaker.Contains("教授") || speaker.Contains("先生") || speaker.Contains("prof"))
            {
                return "talk_blip_prof";
            }

            if (speaker.Contains("いなり") || speaker.Contains("そら"))
            {
                return "talk_blip_f1";
            }

            if (speaker.Contains("かなめ"))
            {
                return "talk_blip_f2";
            }

            // 未知の話者は名前の長さで女声 2 種に振り分ける（同じ話者なら常に同じ声）。
            return speaker.Length % 2 == 0 ? "talk_blip_f1" : "talk_blip_f2";
        }
    }
}
