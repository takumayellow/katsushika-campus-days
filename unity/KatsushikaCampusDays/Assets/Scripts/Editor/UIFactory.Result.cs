using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    public static partial class UIFactory
    {
        private static readonly Color RankGold = new Color(1f, 0.85f, 0.54f);

        /// <summary>
        /// 縦長の画面でのリザルトの枠の高さ (#187)。横長ではランクと内訳を左右に並べるが、縦長の Canvas は幅が 800 しかなく、
        /// 幅 1100 の枠を縮めると本文が 10 px ほどになる。縦長では縮めずに幅を合わせ、ランクの下に内訳を置く。
        /// </summary>
        public const float PortraitResultHeight = 1060f;

        /// <summary>縦長の画面でのランクの位置。枠の上端の真ん中から、ランクの枠の左端の真ん中まで。</summary>
        public static readonly Vector2 PortraitRankPosition = new Vector2(-130f, -250f);

        /// <summary>縦長の画面での内訳の位置。枠の上端の真ん中から、内訳の枠の左端の真ん中まで。</summary>
        public static readonly Vector2 PortraitBodyPosition = new Vector2(-340f, -680f);

        /// <summary>一日の終わりのリザルト。閉じた状態で置く。</summary>
        public static ResultScreen BuildResult(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Result", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1100f, 700f));
            Backdrop(rect, PanelDark);
            // 横長の画面では、幅を狭めるとランクと内訳が重なるので、これまでどおり枠ごと縮める。
            ReflowInCanvas(rect, PortraitResultHeight, true);

            RectTransform headingBox = Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(-100f, 70f));
            headingBox.anchorMin = new Vector2(0f, 1f);
            headingBox.anchorMax = Vector2.one;
            TMP_Text heading = Label(headingBox, "Label", "今日の一日", 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;

            RectTransform rankBox = Rect(rect, "Rank", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(60f, 10f), new Vector2(260f, 300f));
            rankBox.gameObject.AddComponent<PortraitPosition>().Bind(new Vector2(0.5f, 1f), PortraitRankPosition);
            TMP_Text rank = Label(rankBox, "Label", string.Empty, 96f, TextAlignmentOptions.Center);
            rank.fontStyle = FontStyles.Bold;
            rank.color = RankGold;

            RectTransform bodyBox = Rect(rect, "Body", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(360f, 10f), new Vector2(680f, 480f));
            bodyBox.gameObject.AddComponent<PortraitPosition>().Bind(new Vector2(0.5f, 1f), PortraitBodyPosition);
            TMP_Text body = Label(bodyBox, "Label", string.Empty, 30f, TextAlignmentOptions.TopLeft);

            RectTransform choiceBox = Rect(rect, "Choices", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(-100f, 60f));
            choiceBox.anchorMin = Vector2.zero;
            choiceBox.anchorMax = new Vector2(1f, 0f);
            TMP_Text choices = Label(choiceBox, "Label", string.Empty, 32f, TextAlignmentOptions.Center);

            rect.gameObject.SetActive(false);
            ResultScreen view = host.AddComponent<ResultScreen>();
            view.Bind(rect.gameObject, heading, rank, body, choices);
            view.BindTap(AddLinkTapArea(rect, choices));
            return view;
        }
    }
}
