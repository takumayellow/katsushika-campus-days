using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KCD.Editor
{
    /// <summary>
    /// 画面表示をコードだけで組み立てる。フォントは FontLibrary の日本語アセットを使う。
    /// </summary>
    public static partial class UIFactory
    {
        /// <summary>「E 話す」の表示の下端と高さ。</summary>
        private const float PromptBottom = 190f;
        private const float PromptHeight = 64f;

        /// <summary>指のときの知らせの下端。「話す」のボタンに隠れないよう、案内のすぐ上に出す。</summary>
        private const float ToastTouchBottom = PromptBottom + PromptHeight + 8f;

        /// <summary>
        /// 縦長の画面で指のときの「話す」の案内の下端。画面の幅の大半を右下のボタンが占めるので、
        /// 「走る」のボタン（上端は下から CSS で約 178 px、幅 800 の Canvas で 365）より上に出す。
        /// </summary>
        private const float PortraitPromptBottom = 380f;

        /// <summary>縦長の画面で指のときの知らせの下端。案内のすぐ上。</summary>
        private const float PortraitToastTouchBottom = PortraitPromptBottom + PromptHeight + 8f;

        /// <summary>縦長の画面での知らせの高さ。幅 800 では 2 行に折り返すことがある。</summary>
        private const float PortraitToastHeight = 100f;

        /// <summary>縦長の画面での会話の枠の高さ。幅 800 では 1 行が 20 字ほどになるので、6 行入れる。</summary>
        private const float PortraitDialogueHeight = 440f;

        /// <summary>会話の枠の上に出す話し手の顔の大きさ。</summary>
        private const float DialoguePortraitSize = 200f;

        /// <summary>Campus シーンの表示一式。HUD を返す。</summary>
        public static HUD BuildCampusUI(Transform root, GameObject player)
        {
            Canvas canvas = CreateCanvas(root, "HUDCanvas", 0);
            EnsureEventSystem(root);

            var canvasRect = (RectTransform)canvas.transform;
            HUD hud = canvas.gameObject.AddComponent<HUD>();

            RectTransform gameplay = Stretch(canvasRect, "Gameplay");
            hud.GameplayRoot = gameplay.gameObject;

            hud.PromptView = BuildPrompt(canvas.gameObject, gameplay);
            hud.ToastView = BuildToast(canvas.gameObject, gameplay);
            BuildTracker(canvas.gameObject, gameplay);
            BuildClock(canvas.gameObject, gameplay);
            BuildMinimap(canvas.gameObject, gameplay, player.transform);

            hud.QuestLogView = BuildQuestLog(canvas.gameObject, canvasRect);
            hud.PauseMenu = BuildPauseMenu(canvas.gameObject, canvasRect);
            BuildElevatorMenu(canvas.gameObject, canvasRect);
            BuildDialogue(canvas.gameObject, canvasRect, player.transform);
            hud.PauseMenu.Settings = BuildSettings(canvas.gameObject, canvasRect);
            BuildPhotoOverlay(canvas.gameObject, canvasRect);

            // 一日の終わり。Systems は先に置かれているので、ここでリザルト画面を差し込む。
            ResultScreen result = BuildResult(canvas.gameObject, canvasRect);
            DayEndEvaluator evaluator = Object.FindAnyObjectByType<DayEndEvaluator>();
            if (evaluator != null)
            {
                evaluator.Screen = result;
            }

            BuildTouchControls(root);
            return hud;
        }

        /// <summary>画面中央下の「E 話す」表示。</summary>
        private static InteractionPromptView BuildPrompt(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Prompt", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, PromptBottom), new Vector2(460f, PromptHeight));
            Backdrop(rect, Panel);
            rect.gameObject.AddComponent<TouchPosition>().Bind(
                new Vector2(0f, PromptBottom), new Vector2(0f, PortraitPromptBottom));

            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            TMP_Text label = Label(rect, "Label", string.Empty, 30f, TextAlignmentOptions.Center);
            InteractionPromptView view = host.AddComponent<InteractionPromptView>();
            view.Bind(group, label);
            return view;
        }

        /// <summary>画面下のトースト。</summary>
        private static ToastView BuildToast(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Toast", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 96f), new Vector2(1080f, 60f));
            Backdrop(rect, Panel);
            ReflowInCanvas(rect, PortraitToastHeight);
            rect.gameObject.AddComponent<TouchPosition>().Bind(
                new Vector2(0f, ToastTouchBottom), new Vector2(0f, PortraitToastTouchBottom));

            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            TMP_Text label = Label(rect, "Label", string.Empty, 28f, TextAlignmentOptions.Center);
            ToastView view = host.AddComponent<ToastView>();
            view.Bind(group, label);
            return view;
        }

        /// <summary>左上のクエスト追跡。</summary>
        private static void BuildTracker(GameObject host, RectTransform parent)
        {
            // 制限時間つきのステップは 2 行目（残り秒・時間切れ・再挑戦の案内）が付くので、
            // ステップ欄を 96（文字の入る高さ 72）にしてある。以前の 78 では 2 行目が枠の下にはみ出した。
            RectTransform rect = Rect(parent, "QuestTracker", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -40f), new Vector2(520f, 150f));
            Backdrop(rect, Panel);
            rect.gameObject.AddComponent<KeepLeftOfCenter>();

            RectTransform titleRect = Rect(rect, "TitleRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(520f, 54f));
            RectTransform stepRect = Rect(rect, "StepRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -54f), new Vector2(520f, 96f));

            TMP_Text title = Label(titleRect, "Title", string.Empty, 30f, TextAlignmentOptions.TopLeft);
            title.color = Accent;
            TMP_Text step = Label(stepRect, "Step", string.Empty, 26f, TextAlignmentOptions.TopLeft);

            // 英語で 1 行目が折り返したときなど、それでも収まらなければ文字を小さくし、最後は … で切る（枠の外に出さない）。
            step.enableAutoSizing = true;
            step.fontSizeMin = 18f;
            step.fontSizeMax = 26f;
            step.overflowMode = TextOverflowModes.Ellipsis;

            host.AddComponent<QuestTrackerView>().Bind(rect.gameObject, title, step);
        }

        /// <summary>ミニマップの下の時計。</summary>
        private static void BuildClock(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Clock", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, ClockTop), new Vector2(300f, 108f));
            Backdrop(rect, Panel);
            rect.gameObject.AddComponent<KeepPointerSize>();

            RectTransform timeRect = Rect(rect, "TimeRow", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(300f, 62f));
            RectTransform phaseRect = Rect(rect, "PhaseRow", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -62f), new Vector2(300f, 44f));

            TMP_Text time = Label(timeRect, "Time", "08:30", 44f, TextAlignmentOptions.Center);
            TMP_Text phase = Label(phaseRect, "Phase", "朝", 24f, TextAlignmentOptions.Center);
            phase.color = Accent;

            host.AddComponent<ClockView>().Bind(time, phase);
        }

        /// <summary>Tab で開くクエストログ。</summary>
        private static QuestLogView BuildQuestLog(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "QuestLog", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1000f, 700f));
            Backdrop(rect, new Color(0.05f, 0.07f, 0.11f, 0.92f));
            KeepInCanvas(rect);

            TMP_Text body = Label(rect, "Body", string.Empty, 28f, TextAlignmentOptions.TopLeft);
            rect.gameObject.SetActive(false);

            QuestLogView view = host.AddComponent<QuestLogView>();
            view.Bind(rect.gameObject, body);
            view.BindTap(AddScreenTapArea(rect));
            return view;
        }

        /// <summary>Esc のポーズメニュー。</summary>
        private static PauseMenu BuildPauseMenu(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Pause", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 560f));
            Backdrop(rect, new Color(0.04f, 0.05f, 0.09f, 0.94f));
            KeepInCanvas(rect);

            TMP_Text body = Label(rect, "Body", string.Empty, 30f, TextAlignmentOptions.Center);
            rect.gameObject.SetActive(false);

            PauseMenu view = host.AddComponent<PauseMenu>();
            view.Bind(rect.gameObject, body);
            view.BindTap(AddLinkTapArea(rect, body));
            return view;
        }

        /// <summary>エレベーターの行き先を選ぶ画面 (#275)。</summary>
        private static void BuildElevatorMenu(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Elevator", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700f, 540f));
            Backdrop(rect, new Color(0.04f, 0.05f, 0.09f, 0.94f));
            KeepInCanvas(rect);

            TMP_Text body = Label(rect, "Body", string.Empty, 30f, TextAlignmentOptions.Center);
            rect.gameObject.SetActive(false);

            ElevatorMenu view = host.AddComponent<ElevatorMenu>();
            view.Bind(rect.gameObject, body);
            view.BindTap(AddLinkTapArea(rect, body));
        }

        /// <summary>下部の会話ウィンドウ。</summary>
        private static void BuildDialogue(GameObject host, RectTransform parent, Transform player)
        {
            RectTransform rect = Rect(parent, "Dialogue", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 48f), new Vector2(1520f, 300f));
            Backdrop(rect, new Color(0.05f, 0.07f, 0.12f, 0.9f));
            ReflowInCanvas(rect, PortraitDialogueHeight);
            TmpLinkTap tap = AddDialogueTapArea(rect);

            // 話者名の札は枠の内側（上端から 14 px 下）に置き、本文はその下から始める。
            // 札を枠の上にはみ出させると、背景から浮いて本文の 1 行目にも重なる。
            RectTransform speakerRect = Rect(rect, "SpeakerRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -14f), new Vector2(520f, 58f));
            Backdrop(speakerRect, new Color(0.12f, 0.2f, 0.28f, 0.95f));
            TMP_Text speaker = Label(speakerRect, "Speaker", string.Empty, 32f, TextAlignmentOptions.Left);
            speaker.color = Accent;

            RectTransform bodyRect = Stretch(rect, "BodyArea");
            bodyRect.offsetMin = new Vector2(36f, 36f);
            bodyRect.offsetMax = new Vector2(-36f, -80f);
            TMP_Text body = Label(bodyRect, "Body", string.Empty, 32f, TextAlignmentOptions.TopLeft);

            RectTransform markRect = Rect(rect, "ContinueMark", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 24f), new Vector2(40f, 40f));
            TMP_Text mark = Label(markRect, "Mark", "▼", 28f, TextAlignmentOptions.Center);
            mark.color = Accent;

            BuildDialoguePortrait(host, rect, player);

            rect.gameObject.SetActive(false);
            tap.Bind(body);
            DialogueView view = host.AddComponent<DialogueView>();
            view.Bind(rect.gameObject, speaker, body, markRect.gameObject);
            view.BindTap(tap);
        }

        /// <summary>話し手の顔の枠 (#11)。会話の枠の左上の外側に置き、本文の幅は削らない。</summary>
        private static void BuildDialoguePortrait(GameObject host, RectTransform dialogue, Transform player)
        {
            RectTransform frame = Rect(dialogue, "Portrait", new Vector2(0f, 1f), new Vector2(0f, 0f),
                new Vector2(24f, 8f), new Vector2(DialoguePortraitSize, DialoguePortraitSize));
            Backdrop(frame, new Color(0.05f, 0.07f, 0.12f, 0.9f));

            RectTransform faceRect = Stretch(frame, "Face");
            faceRect.offsetMin = new Vector2(6f, 6f);
            faceRect.offsetMax = new Vector2(-6f, -6f);
            RawImage face = faceRect.gameObject.AddComponent<RawImage>();
            face.raycastTarget = false;

            frame.gameObject.SetActive(false);
            host.AddComponent<DialoguePortrait>().Bind(frame.gameObject, face, player);
        }
    }
}
