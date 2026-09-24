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

        /// <summary>英語の目標文（JSON の text_en）。空なら英語表示でも Text を出す。</summary>
        public string TextEn = string.Empty;

        public QuestStepKind Kind = QuestStepKind.Flag;
        public string Target = string.Empty;

        /// <summary>Collect のとき必要な個数。他の種類では 1。</summary>
        public int Count = 1;

        /// <summary>
        /// 制限時間（秒）。0 なら無制限。受注した瞬間（途中のステップなら前のステップを終えた瞬間）から 1 回だけ数える。
        /// 時間切れになったら失敗で止まり、Giver に話しかけるとはじめから数え直す。
        /// </summary>
        public float TimeLimit;

        /// <summary>時間切れのあと話しかけると再挑戦になる NPC の id。クエストの giver を写したもの。</summary>
        public string Giver = string.Empty;

        /// <summary>この時刻（ゲーム内時）以降でないと達成できない。0 なら無条件。</summary>
        public float MinHour;

        /// <summary>いま何個拾ったか。</summary>
        public int Progress;

        public bool Completed;

        /// <summary>制限時間の計時。TimeLimit が 0 のステップでは Idle のまま使わない。</summary>
        public readonly ChallengeTimer Timer = new ChallengeTimer();

        /// <summary>制限時間つきか。</summary>
        public bool IsTimed => TimeLimit > 0f;

        /// <summary>達成を受け付けるか。制限時間つきは計時中だけ（時間切れのあとは再挑戦が要る）。</summary>
        public bool AcceptsProgress => !IsTimed || Timer.IsRunning;

        /// <summary>依頼主に話しかけて（もう一度）始めるのを待っているか。時間切れのあと、またはロード直後。</summary>
        public bool AwaitingGiver => IsTimed && !Completed && !Timer.IsRunning && !string.IsNullOrEmpty(Giver);

        /// <summary>画面に出す目標文。いまの言語で選ぶ。</summary>
        public string DisplayText => L.Pick(Text, TextEn);
    }

    /// <summary>クエスト 1 件。JSON 1 ファイルに 1 件対応する。</summary>
    public sealed class QuestData
    {
        /// <summary>rewardType がこれなら、達成時に rewardId の称号を得る。</summary>
        public const string RewardAchievement = "achievement";

        /// <summary>rewardType がこれなら、達成時に rewardId の収集物（collectibles.json の source = quest）をもらう。</summary>
        public const string RewardCollectible = "collectible";

        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Summary = string.Empty;

        /// <summary>英語の題名・あらすじ（JSON の title_en / summary_en）。空なら英語表示でも日本語を出す。</summary>
        public string TitleEn = string.Empty;
        public string SummaryEn = string.Empty;

        public int Order;
        public bool AutoStart;

        /// <summary>サブクエストか（JSON の side）。称号「本編を歩き切った」(ach_main_story) は本編だけを数える。</summary>
        public bool Side;

        public string RewardText = string.Empty;

        /// <summary>英語の報酬文（JSON の rewardText_en）。</summary>
        public string RewardTextEn = string.Empty;

        /// <summary>報酬の種類（collectible / achievement）。空なら RewardText だけ。</summary>
        public string RewardType = string.Empty;

        /// <summary>報酬の id（c_* / ach_*）。</summary>
        public string RewardId = string.Empty;

        /// <summary>依頼主の NPC id。制限時間つきのステップは、この NPC に話しかけると再挑戦できる。</summary>
        public string Giver = string.Empty;

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

        /// <summary>画面に出す題名・あらすじ・報酬文。いまの言語で選ぶ。</summary>
        public string DisplayTitle => L.Pick(Title, TitleEn);
        public string DisplaySummary => L.Pick(Summary, SummaryEn);
        public string DisplayRewardText => L.Pick(RewardText, RewardTextEn);

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
                TitleEn = MiniJson.GetString(node, "title_en"),
                SummaryEn = MiniJson.GetString(node, "summary_en"),
                Order = MiniJson.GetInt(node, "order", 999),
                AutoStart = MiniJson.GetBool(node, "autoStart"),
                Side = MiniJson.GetBool(node, "side"),
                RewardText = MiniJson.GetString(node, "rewardText"),
                RewardTextEn = MiniJson.GetString(node, "rewardText_en"),
                RewardType = MiniJson.GetString(node, "rewardType"),
                RewardId = MiniJson.GetString(node, "rewardId"),
                Giver = MiniJson.GetString(node, "giver")
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
                        TextEn = MiniJson.GetString(stepNode, "text_en"),
                        Kind = ParseKind(MiniJson.GetString(stepNode, "type", "flag")),
                        Target = MiniJson.GetString(stepNode, "target"),
                        Count = System.Math.Max(1, MiniJson.GetInt(stepNode, "count", 1)),
                        TimeLimit = MiniJson.GetFloat(stepNode, "timeLimit"),
                        Giver = quest.Giver,
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
