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

        /// <summary>指で遊んでいるときの本文（JSON の text_touch / text_touch_en）。キー操作の案内を指の操作に言い換えた文。空なら Text を出す。</summary>
        public string TextTouch = string.Empty;

        public string TextTouchEn = string.Empty;

        /// <summary>英語表示で話者名を引く辞書キー（ui.npc.*）。空なら英語表示でも Speaker を出す。</summary>
        public string SpeakerKey = string.Empty;

        /// <summary>紙に書かれた文（メモ・手紙）。声を鳴らさずに出す（JSON の written）(#175)。</summary>
        public bool Written;

        /// <summary>画面に出す本文。いまの言語で選ぶ。</summary>
        public string DisplayText => InputMode.IsTouch && !string.IsNullOrEmpty(TextTouch)
            ? L.Pick(TextTouch, TextTouchEn)
            : L.Pick(Text, TextEn);

        /// <summary>画面に出す話者名。日本語ではデータのまま、英語では辞書の名前（無ければデータのまま）。</summary>
        public string DisplaySpeaker =>
            L.IsEnglish && !string.IsNullOrEmpty(SpeakerKey) ? L.Get(SpeakerKey, Speaker) : Speaker;
    }

    /// <summary>
    /// 話題の終わりに出す選択肢の 1 つ (#9)。選ぶとその NPC の好感度が Affinity だけ動き、返事の行が続く。
    /// </summary>
    public sealed class DialogueChoice
    {
        public string Text = string.Empty;

        /// <summary>英語の文（JSON の text_en）。空なら英語表示でも Text を出す。</summary>
        public string TextEn = string.Empty;

        /// <summary>選んだときに動く好感度。</summary>
        public int Affinity;

        /// <summary>選んだときに立てるフラグ。</summary>
        public string SetsFlag = string.Empty;

        /// <summary>
        /// 選んでも話題を答えた話題にしない（JSON の retry）。外れの答えに付けると、次に話しかけたときにまた聞く (#389)。
        /// </summary>
        public bool Retry;

        /// <summary>選んだあとに続く行。空なら選んだところで会話を閉じる。</summary>
        public readonly List<DialogueLine> Lines = new List<DialogueLine>();

        /// <summary>画面に出す文。いまの言語で選ぶ。</summary>
        public string DisplayText => L.Pick(Text, TextEn);
    }

    /// <summary>ひとまとまりの会話。条件を満たしたものが上から順に選ばれる。</summary>
    public sealed class DialogueTopic
    {
        public string Id = string.Empty;

        /// <summary>このクエストが受注中のときだけ出す。空なら無条件。</summary>
        public string RequiresActiveQuest = string.Empty;

        /// <summary>このクエストが完了済みのときだけ出す。空なら無条件。</summary>
        public string RequiresCompletedQuest = string.Empty;

        /// <summary>
        /// 「クエスト id/段の id」。そのクエストを受注中で、いまの段がこれのときだけ出す。空なら無条件 (#389)。
        /// 段より先に答えさせると、立てたフラグはその段で数えられないので、問いはこの条件で段に合わせて出す。
        /// </summary>
        public string RequiresStep = string.Empty;

        /// <summary>隠しアイテムをこの数以上拾っているときだけ出す。0 なら無条件。</summary>
        public int RequiresHiddenCount;

        /// <summary>このアイテム（コレクション id）を拾っているときだけ出す。空なら無条件。</summary>
        public string RequiresItem = string.Empty;

        /// <summary>この NPC の好感度がこの値以上のときだけ出す。0 なら無条件 (#9)。</summary>
        public int RequiresAffinity;

        /// <summary>クエスト以外の条件（隠しアイテムの数・持ち物）があるか。</summary>
        public bool HasItemCondition => RequiresHiddenCount > 0 || !string.IsNullOrEmpty(RequiresItem);

        /// <summary>一度話したら二度と出さない。</summary>
        public bool Once;

        /// <summary>会話終了時に受注させるクエスト id。</summary>
        public string StartsQuest = string.Empty;

        /// <summary>会話終了時に立てるフラグ。</summary>
        public string SetsFlag = string.Empty;

        public readonly List<DialogueLine> Lines = new List<DialogueLine>();

        /// <summary>最後の行のあとに出す選択肢。空なら選ばせない (#9)。</summary>
        public readonly List<DialogueChoice> Choices = new List<DialogueChoice>();

        /// <summary>選択肢があるか。</summary>
        public bool HasChoices => Choices.Count > 0;
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
                RequiresStep = MiniJson.GetString(node, "requiresStep"),
                RequiresHiddenCount = MiniJson.GetInt(node, "requiresHiddenCount"),
                RequiresItem = MiniJson.GetString(node, "requiresItem"),
                RequiresAffinity = MiniJson.GetInt(node, "requiresAffinity"),
                Once = MiniJson.GetBool(node, "once"),
                StartsQuest = MiniJson.GetString(node, "startsQuest"),
                SetsFlag = MiniJson.GetString(node, "setsFlag")
            };

            ParseLines(MiniJson.GetArray(node, "lines"), data, topic.Lines);

            List<object> choices = MiniJson.GetArray(node, "choices");
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] is Dictionary<string, object> choiceNode)
                {
                    var choice = new DialogueChoice
                    {
                        Text = MiniJson.GetString(choiceNode, "text"),
                        TextEn = MiniJson.GetString(choiceNode, "text_en"),
                        Affinity = MiniJson.GetInt(choiceNode, "affinity"),
                        SetsFlag = MiniJson.GetString(choiceNode, "setsFlag"),
                        Retry = MiniJson.GetBool(choiceNode, "retry")
                    };
                    ParseLines(MiniJson.GetArray(choiceNode, "lines"), data, choice.Lines);
                    topic.Choices.Add(choice);
                }
            }

            return topic;
        }

        /// <summary>行の配列を into に足す。文字列だけの行はこの NPC の台詞として扱う。</summary>
        private static void ParseLines(List<object> lines, DialogueData data, List<DialogueLine> into)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] is Dictionary<string, object> lineNode)
                {
                    string speaker = MiniJson.GetString(lineNode, "speaker", data.Speaker);
                    into.Add(new DialogueLine
                    {
                        Speaker = speaker,
                        SpeakerKey = data.SpeakerKeyFor(speaker),
                        Text = MiniJson.GetString(lineNode, "text"),
                        TextEn = MiniJson.GetString(lineNode, "text_en"),
                        TextTouch = MiniJson.GetString(lineNode, "text_touch"),
                        TextTouchEn = MiniJson.GetString(lineNode, "text_touch_en"),
                        Written = MiniJson.GetBool(lineNode, "written")
                    });
                }
                else if (lines[i] is string plain)
                {
                    into.Add(new DialogueLine
                    {
                        Speaker = data.Speaker,
                        SpeakerKey = data.SpeakerKeyFor(data.Speaker),
                        Text = plain
                    });
                }
            }
        }
    }
}
