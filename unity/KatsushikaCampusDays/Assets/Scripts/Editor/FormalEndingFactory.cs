using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 本編のエンディングの画面 (#176)。閉じた状態で置き、<see cref="FormalEnding"/> が開いてページを差し替える。
    /// 作りは <see cref="DormEndingFactory"/> と同じで、最後のページにクレジットの本文が入るぶん大きく取る。
    /// </summary>
    public static class FormalEndingFactory
    {
        /// <summary>画面の Canvas の名前。</summary>
        public const string CanvasName = "FormalEndingCanvas";

        /// <summary>下敷きの色。UIFactory.Settings の PanelDark と同じ値。</summary>
        private static readonly Color PanelDark = new Color(0.04f, 0.05f, 0.09f, 0.94f);

        /// <summary>見出しの色。リザルトのランクと同じ金色。</summary>
        private static readonly Color HeadingGold = new Color(1f, 0.85f, 0.54f);

        /// <summary>下段の案内の色。控えめに。</summary>
        private static readonly Color Muted = new Color(0.72f, 0.76f, 0.84f);

        /// <summary>パネルの大きさ（1920x1080 換算）。クレジットの本文の枠 1084x536 が収まる大きさ。</summary>
        private static readonly Vector2 PanelSize = new Vector2(1240f, 800f);

        /// <summary>画面を組んで FormalEnding を Systems に付ける。EventSystem は UIFactory.BuildCampusUI が置く。</summary>
        public static FormalEnding Build(Transform root)
        {
            GameObject host = FindHost(root);
            Canvas canvas = UIFactory.CreateCanvas(root, CanvasName, FormalEnding.PanelSortingOrder);
            var parent = (RectTransform)canvas.transform;

            var center = new Vector2(0.5f, 0.5f);
            RectTransform rect = UIFactory.Rect(parent, "FormalEnding", center, center, Vector2.zero, PanelSize);
            UIFactory.Backdrop(rect, PanelDark);
            UIFactory.KeepInCanvas(rect);

            RectTransform headingBox = UIFactory.Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(1120f, 72f));
            TMP_Text heading = UIFactory.Label(headingBox, "Label",
                FormalEnding.HeadingText(0, false), 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;
            heading.color = HeadingGold;

            RectTransform bodyBox = UIFactory.Rect(rect, "Body", center, center,
                new Vector2(0f, 4f), new Vector2(1120f, 580f));
            TMP_Text body = UIFactory.Label(bodyBox, "Label", string.Empty, 30f, TextAlignmentOptions.Center);
            // クレジットのページは行が多いので、枠に収まるまで縮める。振り返りと会った人は 30 のまま。
            body.enableAutoSizing = true;
            body.fontSizeMin = 18f;
            body.fontSizeMax = 30f;

            RectTransform choiceBox = UIFactory.Rect(rect, "Choice", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 34f), new Vector2(1120f, 56f));
            TMP_Text choice = UIFactory.Label(choiceBox, "Label",
                FormalEnding.ChoiceText(0, false), 28f, TextAlignmentOptions.Center);
            choice.color = Muted;

            rect.gameObject.SetActive(false);

            FormalEnding view = host.AddComponent<FormalEnding>();
            view.Bind(rect.gameObject, heading, body, choice);
            view.BindTap(UIFactory.AddScreenTapArea(rect));
            EditorPaths.Report("エンディングの画面を置きました（" + CanvasName + "、重ね順 "
                + FormalEnding.PanelSortingOrder + "）。");
            return view;
        }

        /// <summary>FormalEnding を付ける先。Systems が無ければ自分で作る（単体で呼ばれても壊れないように）。</summary>
        private static GameObject FindHost(Transform root)
        {
            Transform systems = root != null ? root.Find(DormEndingFactory.HostName) : null;
            if (systems != null)
            {
                return systems.gameObject;
            }

            var go = new GameObject(DormEndingFactory.HostName);
            go.transform.SetParent(root, false);
            EditorPaths.Report(DormEndingFactory.HostName + " が無かったので作りました。");
            return go;
        }
    }
}
