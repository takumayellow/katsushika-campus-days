using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    public static partial class UIFactory
    {
        private static readonly Color RankGold = new Color(1f, 0.85f, 0.54f);

        /// <summary>一日の終わりのリザルト。閉じた状態で置く。</summary>
        public static ResultScreen BuildResult(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Result", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1100f, 700f));
            Backdrop(rect, PanelDark);

            RectTransform headingBox = Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(1000f, 70f));
            TMP_Text heading = Label(headingBox, "Label", "今日の一日", 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;

            RectTransform rankBox = Rect(rect, "Rank", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(60f, 10f), new Vector2(260f, 300f));
            TMP_Text rank = Label(rankBox, "Label", string.Empty, 96f, TextAlignmentOptions.Center);
            rank.fontStyle = FontStyles.Bold;
            rank.color = RankGold;

            RectTransform bodyBox = Rect(rect, "Body", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(360f, 10f), new Vector2(680f, 480f));
            TMP_Text body = Label(bodyBox, "Label", string.Empty, 30f, TextAlignmentOptions.TopLeft);

            RectTransform choiceBox = Rect(rect, "Choices", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(1000f, 60f));
            TMP_Text choices = Label(choiceBox, "Label", string.Empty, 32f, TextAlignmentOptions.Center);

            rect.gameObject.SetActive(false);
            ResultScreen view = host.AddComponent<ResultScreen>();
            view.Bind(rect.gameObject, heading, rank, body, choices);
            return view;
        }
    }
}
