using System.Collections.Generic;

namespace KCD
{
    /// <summary>会話の 1 行。Speaker が空ならプレイヤーの台詞として扱う。</summary>
    public sealed class DialogueLine
    {
        /// <summary>データでプレイヤー自身の台詞を表す話者名。</summary>
        public const string SelfSpeaker = "自分";

        /// <summary>話者名（データのまま。声の選び分けにも使う）。</summary>
        public string Speaker = string.Empty;

        public string Text = string.Empty;

        /// <summary>英語の本文（JSON の text_en）。空なら英語表示でも Text を出す。</summary>
        public string TextEn = string.Empty;

        /// <summary>英語表示で話者名を引く辞書キー（ui.npc.*）。空なら英語表示でも Speaker を出す。</summary>
        public string SpeakerKey = string.Empty;

        /// <summary>画面に出す本文。いまの言語で選ぶ。</summary>
        public string DisplayText => L.Pick(Text, TextEn);

        /// <summary>画面に出す話者名。日本語ではデータのまま、英語では辞書の名前（無ければデータのまま）。</summary>
        public string DisplaySpeaker =>
            L.IsEnglish && !string.IsNullOrEmpty(SpeakerKey) ? L.Get(SpeakerKey, Speaker) : Speaker;
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
        /// <summary>話者名の辞書キーの接頭辞。ui.npc.&lt;会話データの id&gt; が NPC、ui.npc.self がプレイヤー。</summary>
        public const string SpeakerKeyPrefix = "ui.npc.";

        public const string SelfSpeakerKey = SpeakerKeyPrefix + "self";

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
                    data.Topics.Add(ParseTopic(topicNode, data, i));
                }
            }

            return data;
        }

        /// <summary>
        /// 話者名から英語表示用の辞書キーを決める。この NPC 本人なら ui.npc.&lt;id&gt;、「自分」なら ui.npc.self。
        /// それ以外の名前は辞書に対応が無いので空（英語表示でもデータの名前を出す）。
        /// </summary>
        public string SpeakerKeyFor(string speaker)
        {
            if (speaker == DialogueLine.SelfSpeaker)
            {
                return SelfSpeakerKey;
            }

            return speaker == Speaker ? SpeakerKeyPrefix + Id : string.Empty;
        }

        private static DialogueTopic ParseTopic(Dictionary<string, object> node, DialogueData data, int index)
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
                    string speaker = MiniJson.GetString(lineNode, "speaker", data.Speaker);
                    topic.Lines.Add(new DialogueLine
                    {
                        Speaker = speaker,
                        SpeakerKey = data.SpeakerKeyFor(speaker),
                        Text = MiniJson.GetString(lineNode, "text"),
                        TextEn = MiniJson.GetString(lineNode, "text_en")
                    });
                }
                else if (lines[i] is string plain)
                {
                    topic.Lines.Add(new DialogueLine
                    {
                        Speaker = data.Speaker,
                        SpeakerKey = data.SpeakerKeyFor(data.Speaker),
                        Text = plain
                    });
                }
            }

            return topic;
        }
    }
}
