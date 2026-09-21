using System.Collections.Generic;

namespace KCD
{
    /// <summary>クエストの 1 ステップが何をすれば進むか。</summary>
    public enum QuestStepKind
    {
        /// <summary>指定 id の NPC と会話する。</summary>
        Talk,

        /// <summary>指定 id の建物の入口に入る。</summary>
        Enter,

        /// <summary>指定 id の地点に近づく。</summary>
        Visit,

        /// <summary>指定 id の落とし物を指定個数だけ拾う。</summary>
        Collect,

        /// <summary>任意のフラグが立つ（買い物・イベントなど）。</summary>
        Flag
    }

    /// <summary>クエストの 1 ステップ。</summary>
    public sealed class QuestStep
    {
        public string Id = string.Empty;
        public string Text = string.Empty;
        public QuestStepKind Kind = QuestStepKind.Flag;
        public string Target = string.Empty;

        /// <summary>Collect のとき必要な個数。他の種類では 1。</summary>
        public int Count = 1;

        /// <summary>制限時間（秒）。0 なら無制限。ステップが現在地になった瞬間から数える。</summary>
        public float TimeLimit;

        /// <summary>この時刻（ゲーム内時）以降でないと達成できない。0 なら無条件。</summary>
        public float MinHour;

        /// <summary>いま何個拾ったか。</summary>
        public int Progress;

        public bool Completed;
    }

    /// <summary>クエスト 1 件。JSON 1 ファイルに 1 件対応する。</summary>
    public sealed class QuestData
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Summary = string.Empty;
        public int Order;
        public bool AutoStart;
        public string RewardText = string.Empty;
        public readonly List<string> Prerequisites = new List<string>();
        public readonly List<QuestStep> Steps = new List<QuestStep>();

        /// <summary>まだ終わっていない最初のステップ。全部終わっていれば null。</summary>
        public QuestStep CurrentStep
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Completed)
                    {
                        return Steps[i];
                    }
                }

                return null;
            }
        }

        /// <summary>全ステップが完了しているか。</summary>
        public bool IsComplete => CurrentStep == null && Steps.Count > 0;

        /// <summary>JSON ノードからクエストを組み立てる。壊れていれば null。</summary>
        public static QuestData FromJson(Dictionary<string, object> node)
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

            var quest = new QuestData
            {
                Id = id,
                Title = MiniJson.GetString(node, "title", id),
                Summary = MiniJson.GetString(node, "summary"),
                Order = MiniJson.GetInt(node, "order", 999),
                AutoStart = MiniJson.GetBool(node, "autoStart"),
                RewardText = MiniJson.GetString(node, "rewardText")
            };

            quest.Prerequisites.AddRange(MiniJson.ToStringList(MiniJson.GetArray(node, "prerequisites")));

            List<object> steps = MiniJson.GetArray(node, "steps");
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] is Dictionary<string, object> stepNode)
                {
                    quest.Steps.Add(new QuestStep
                    {
                        Id = MiniJson.GetString(stepNode, "id", "s" + (i + 1)),
                        Text = MiniJson.GetString(stepNode, "text"),
                        Kind = ParseKind(MiniJson.GetString(stepNode, "type", "flag")),
                        Target = MiniJson.GetString(stepNode, "target"),
                        Count = System.Math.Max(1, MiniJson.GetInt(stepNode, "count", 1)),
                        TimeLimit = MiniJson.GetFloat(stepNode, "timeLimit"),
                        MinHour = MiniJson.GetFloat(stepNode, "minHour")
                    });
                }
            }

            return quest;
        }

        private static QuestStepKind ParseKind(string text)
        {
            switch (text)
            {
                case "talk": return QuestStepKind.Talk;
                case "enter": return QuestStepKind.Enter;
                case "visit": return QuestStepKind.Visit;
                case "collect": return QuestStepKind.Collect;
                default: return QuestStepKind.Flag;
            }
        }
    }
}
