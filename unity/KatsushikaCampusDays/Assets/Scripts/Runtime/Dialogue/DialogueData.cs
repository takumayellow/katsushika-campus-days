using System.Collections.Generic;

namespace KCD
{
    /// <summary>会話の 1 行。Speaker が空ならプレイヤーの台詞として扱う。</summary>
    public sealed class DialogueLine
    {
        public string Speaker = string.Empty;
        public string Text = string.Empty;
    }

    /// <summary>ひとまとまりの会話。条件を満たしたものが上から順に選ばれる。</summary>
    public sealed class DialogueTopic
    {
        public string Id = string.Empty;

        /// <summary>このクエストが受注中のときだけ出す。空なら無条件。</summary>
        public string RequiresActiveQuest = string.Empty;

        /// <summary>このクエストが完了済みのときだけ出す。空なら無条件。</summary>
        public string RequiresCompletedQuest = string.Empty;

        /// <summary>一度話したら二度と出さない。</summary>
        public bool Once;

        /// <summary>会話終了時に受注させるクエスト id。</summary>
        public string StartsQuest = string.Empty;

        /// <summary>会話終了時に立てるフラグ。</summary>
        public string SetsFlag = string.Empty;

        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
    }

    /// <summary>NPC 1 人ぶんの会話データ。</summary>
    public sealed class DialogueData
    {
        public string Id = string.Empty;
        public string Speaker = string.Empty;
        public readonly List<DialogueTopic> Topics = new List<DialogueTopic>();

        /// <summary>JSON ノードから組み立てる。id が無ければ null。</summary>
        public static DialogueData FromJson(Dictionary<string, object> node)
        {
            if (node == null)
            {
                return null;
            }

            string id = MiniJson.GetString(node, "id");
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            var data = new DialogueData
            {
                Id = id,
                Speaker = MiniJson.GetString(node, "speaker", id)
            };

            List<object> topics = MiniJson.GetArray(node, "topics");
            for (int i = 0; i < topics.Count; i++)
            {
                if (topics[i] is Dictionary<string, object> topicNode)
                {
                    data.Topics.Add(ParseTopic(topicNode, data.Speaker, i));
                }
            }

            return data;
        }

        private static DialogueTopic ParseTopic(Dictionary<string, object> node, string defaultSpeaker, int index)
        {
            var topic = new DialogueTopic
            {
                Id = MiniJson.GetString(node, "id", "t" + index),
                RequiresActiveQuest = MiniJson.GetString(node, "requiresActiveQuest"),
                RequiresCompletedQuest = MiniJson.GetString(node, "requiresCompletedQuest"),
                Once = MiniJson.GetBool(node, "once"),
                StartsQuest = MiniJson.GetString(node, "startsQuest"),
                SetsFlag = MiniJson.GetString(node, "setsFlag")
            };

            List<object> lines = MiniJson.GetArray(node, "lines");
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] is Dictionary<string, object> lineNode)
                {
                    topic.Lines.Add(new DialogueLine
                    {
                        Speaker = MiniJson.GetString(lineNode, "speaker", defaultSpeaker),
                        Text = MiniJson.GetString(lineNode, "text")
                    });
                }
                else if (lines[i] is string plain)
                {
                    topic.Lines.Add(new DialogueLine { Speaker = defaultSpeaker, Text = plain });
                }
            }

            return topic;
        }
    }
}
