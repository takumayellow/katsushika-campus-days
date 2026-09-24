using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 裏エンド「寮でぐーたら」の画面 (#41)。閉じた状態で置き、<see cref="DormEnding"/> が開く。
    ///
    /// UIFactory の partial には足さない（あちらは本編の HUD・メニュー・リザルトの置き場で、
    /// 別のワークフローが同じファイルを触っている）。使う下ごしらえ（Canvas・矩形・文字）は
    /// UIFactory の public なものをそのまま借りる。色だけは UIFactory 側が private なので写した。
    /// </summary>
    public static class DormEndingFactory
    {
        /// <summary>Systems オブジェクトの名前。SceneBuilder.PlaceSystems が作る。</summary>
        public const string HostName = "Systems";

        /// <summary>画面の Canvas の名前。</summary>
        public const string CanvasName = "DormEndingCanvas";

        /// <summary>下敷きの色。UIFactory.Settings の PanelDark と同じ値。</summary>
        private static readonly Color PanelDark = new Color(0.04f, 0.05f, 0.09f, 0.94f);

        /// <summary>時刻の色。リザルトのランクと同じ金色。</summary>
        private static readonly Color ClockGold = new Color(1f, 0.85f, 0.54f);

        /// <summary>下段の案内の色。控えめに。</summary>
        private static readonly Color Muted = new Color(0.72f, 0.76f, 0.84f);

        /// <summary>パネルの大きさ（1920x1080 換算）。</summary>
        private static readonly Vector2 PanelSize = new Vector2(1000f, 560f);

        /// <summary>
        /// 画面を組んで DormEnding を Systems に付ける。
        /// UIFactory.BuildCampusUI が EventSystem を置くので、ここでは置かない（二重になる）。
        /// </summary>
        public static DormEnding Build(Transform root)
        {
            GameObject host = FindHost(root);
            Canvas canvas = UIFactory.CreateCanvas(root, CanvasName, DormEnding.PanelSortingOrder);
            var parent = (RectTransform)canvas.transform;

            var center = new Vector2(0.5f, 0.5f);
            RectTransform rect = UIFactory.Rect(parent, "DormEnding", center, center, Vector2.zero, PanelSize);
            UIFactory.Backdrop(rect, PanelDark);

            RectTransform headingBox = UIFactory.Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -36f), new Vector2(900f, 72f));
            TMP_Text heading = UIFactory.Label(headingBox, "Label",
                DormEnding.HeadingText(false), 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;

            RectTransform bodyBox = UIFactory.Rect(rect, "Body", center, center,
                new Vector2(0f, -6f), new Vector2(880f, 340f));
            TMP_Text body = UIFactory.Label(bodyBox, "Label", string.Empty, 32f, TextAlignmentOptions.Center);
            // 本文の中で時刻だけ <size=200%> に拡がる。色は本文と同じにせず、時計として目立たせる。
            body.color = ClockGold;

            RectTransform choiceBox = UIFactory.Rect(rect, "Choice", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 34f), new Vector2(900f, 56f));
            TMP_Text choice = UIFactory.Label(choiceBox, "Label",
                DormEnding.ChoiceText(false), 30f, TextAlignmentOptions.Center);
            choice.color = Muted;

            rect.gameObject.SetActive(false);

            DormEnding view = host.AddComponent<DormEnding>();
            view.Bind(rect.gameObject, heading, body, choice);
            EditorPaths.Report("裏エンドの画面を置きました（" + CanvasName + "、重ね順 "
                + DormEnding.PanelSortingOrder + "）。");
            return view;
        }

        /// <summary>DormEnding を付ける先。Systems が無ければ自分で作る（単体で呼ばれても壊れないように）。</summary>
        private static GameObject FindHost(Transform root)
        {
            Transform systems = root != null ? root.Find(HostName) : null;
            if (systems != null)
            {
                return systems.gameObject;
            }

            var go = new GameObject(HostName);
            go.transform.SetParent(root, false);
            EditorPaths.Report(HostName + " が無かったので作りました。");
            return go;
        }
    }
}
