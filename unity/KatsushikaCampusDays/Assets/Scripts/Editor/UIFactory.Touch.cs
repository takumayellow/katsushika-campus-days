using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace KCD.Editor
{
    /// <summary>
    /// 指で遊ぶときの操作 UI (#187)。スティックとボタンは仮想のゲームパッドに書くので、
    /// <see cref="KCDInput"/> はゲームパッドと同じ読み方で動く。PC で仮想のゲームパッドを作らないよう、
    /// OnScreenControl を持つ物はどれも止めた状態で置き、指で遊んでいるときだけ動かす。
    /// </summary>
    public static partial class UIFactory
    {
        /// <summary>
        /// タッチの UI の基準の大きさ。高さに合わせて縮めるので、横長のスマホでもボタンが指の大きさを保つ
        /// （1080 基準の HUD のまま置くと、高さ 390 の画面で 1/3 弱に縮む）。
        /// </summary>
        private static readonly Vector2 TouchReference = new Vector2(1280f, 720f);

        /// <summary>HUD より上に描き、HUD より先に指を受ける。</summary>
        private const int TouchSortOrder = 5;

        private static readonly Color TouchFace = new Color(0.06f, 0.08f, 0.12f, 0.55f);
        private static readonly Color StickBase = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color StickKnob = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>Campus シーンのタッチの操作 UI。</summary>
        public static TouchHud BuildTouchControls(Transform root)
        {
            Canvas canvas = CreateCanvas(root, "TouchCanvas", TouchSortOrder);
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.referenceResolution = TouchReference;
            scaler.matchWidthOrHeight = 1f;
            var canvasRect = (RectTransform)canvas.transform;

            // 先に止めてから中身を作る。動いている親の下で OnScreenControl を足すと、その場で仮想のゲームパッドができる。
            RectTransform controls = Stretch(canvasRect, "Controls");
            controls.gameObject.SetActive(false);

            RectTransform play = Stretch(controls, "Play");
            CanvasGroup playGroup = play.gameObject.AddComponent<CanvasGroup>();

            RectTransform look = Stretch(play, "LookArea");
            HitArea(look);
            look.gameObject.AddComponent<TouchLookArea>();

            TouchStick stick = BuildStick(play);

            TMP_Text jumpLabel = BuildPadButton(play, "Jump", new Vector2(-120f, 130f), 140f,
                "<Gamepad>/buttonSouth", 30f);
            TMP_Text interactLabel = BuildPadButton(play, "Interact", new Vector2(-290f, 105f), 124f,
                "<Gamepad>/buttonWest", 26f);

            RectTransform sprintRect = Rect(play, "Sprint", new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(-120f, 300f), new Vector2(104f, 104f));
            Image sprintFace = Disc(sprintRect, TouchFace);
            TMP_Text sprintLabel = Label(sprintRect, "Label", string.Empty, 26f, TextAlignmentOptions.Center);
            sprintRect.gameObject.AddComponent<TouchSprintButton>().Bind(stick, sprintFace);

            RectTransform pause = Stretch(controls, "Pause");
            CanvasGroup pauseGroup = pause.gameObject.AddComponent<CanvasGroup>();
            TMP_Text pauseLabel = BuildPadButton(pause, "PauseButton", new Vector2(0f, -64f), 96f,
                "<Gamepad>/start", 34f, new Vector2(0.5f, 1f));
            pauseLabel.text = "II";

            TouchHud hud = canvas.gameObject.AddComponent<TouchHud>();
            hud.Bind(controls.gameObject, playGroup, pauseGroup);
            hud.BindLabels(jumpLabel, interactLabel, sprintLabel);
            return hud;
        }

        /// <summary>
        /// 会話の枠に、指で叩いて進める受け口を足す。画面のどこを叩いても進むよう、枠からはみ出して画面を覆う。
        /// 押すと仮想のゲームパッドの西ボタン（キーの E と同じ）が入る。
        /// </summary>
        private static void AddDialogueTapArea(RectTransform dialogue)
        {
            RectTransform tap = Stretch(dialogue, "TapArea");
            tap.SetAsFirstSibling();
            tap.offsetMin = new Vector2(-1600f, -400f);
            tap.offsetMax = new Vector2(1600f, 2000f);
            tap.gameObject.SetActive(false);
            HitArea(tap);
            tap.gameObject.AddComponent<OnScreenButton>().controlPath = "<Gamepad>/buttonWest";
            dialogue.gameObject.AddComponent<TouchOnly>().Bind(tap.gameObject);
        }

        /// <summary>
        /// 叩ける透明の板を parent いっぱいに敷き、text の中の link を叩けるようにする。
        /// タイトルとキャラ選択で使う。マウスでは何もしない（受け手が入力の種類を見る）。
        /// </summary>
        public static TmpLinkTap AddLinkTapArea(RectTransform parent, TMP_Text text)
        {
            RectTransform area = Stretch(parent, "TapArea");
            area.SetAsFirstSibling();
            HitArea(area);
            TmpLinkTap tap = area.gameObject.AddComponent<TmpLinkTap>();
            tap.Bind(text);
            return tap;
        }

        /// <summary>左側の、指を置いたところが中心になるスティック。</summary>
        private static TouchStick BuildStick(RectTransform parent)
        {
            var area = new GameObject("StickArea", typeof(RectTransform));
            area.layer = LayerMask.NameToLayer("UI");
            area.transform.SetParent(parent, false);
            var rect = (RectTransform)area.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0.45f, 0.85f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            HitArea(rect);

            // 丸はスティックの範囲の中心を基準に置く（TouchStick は範囲の中心からの位置で動かす）。
            RectTransform baseCircle = Rect(rect, "Base", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(200f, 200f));
            Disc(baseCircle, StickBase).raycastTarget = false;
            RectTransform knob = Rect(rect, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(96f, 96f));
            Disc(knob, StickKnob).raycastTarget = false;

            TouchStick stick = area.AddComponent<TouchStick>();
            stick.Bind(baseCircle, knob, 90f);
            return stick;
        }

        /// <summary>丸いボタン。押している間 controlPath のボタンが入る。既定は右下を基準に置く。</summary>
        private static TMP_Text BuildPadButton(RectTransform parent, string name, Vector2 position, float size,
            string controlPath, float fontSize, Vector2? anchor = null)
        {
            RectTransform rect = Rect(parent, name, anchor ?? new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                position, new Vector2(size, size));
            Disc(rect, TouchFace);
            TMP_Text label = Label(rect, "Label", string.Empty, fontSize, TextAlignmentOptions.Center);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            rect.gameObject.AddComponent<OnScreenButton>().controlPath = controlPath;
            return label;
        }

        /// <summary>丸い面。ボタンの当たりもこの丸の四角になる。</summary>
        private static Image Disc(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = MinimapAssets.Circle();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>見えないが指を受ける面。</summary>
        private static void HitArea(RectTransform rect)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            rect.GetComponent<CanvasRenderer>().cullTransparentMesh = true;
        }
    }
}
