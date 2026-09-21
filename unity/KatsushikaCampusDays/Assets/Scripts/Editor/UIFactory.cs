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
            BuildDialogue(canvas.gameObject, canvasRect);
            hud.PauseMenu.Settings = BuildSettings(canvas.gameObject, canvasRect);
            BuildPhotoOverlay(canvas.gameObject, canvasRect);

            // 一日の終わり。Systems は先に置かれているので、ここでリザルト画面を差し込む。
            ResultScreen result = BuildResult(canvas.gameObject, canvasRect);
            DayEndEvaluator evaluator = Object.FindAnyObjectByType<DayEndEvaluator>();
            if (evaluator != null)
            {
                evaluator.Screen = result;
            }

            return hud;
        }

        /// <summary>画面中央下の「E 話す」表示。</summary>
        private static InteractionPromptView BuildPrompt(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Prompt", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 190f), new Vector2(460f, 64f));
            Backdrop(rect, Panel);

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
            RectTransform rect = Rect(parent, "QuestTracker", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -40f), new Vector2(520f, 132f));
            Backdrop(rect, Panel);

            RectTransform titleRect = Rect(rect, "TitleRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(520f, 54f));
            RectTransform stepRect = Rect(rect, "StepRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -54f), new Vector2(520f, 78f));

            TMP_Text title = Label(titleRect, "Title", string.Empty, 30f, TextAlignmentOptions.TopLeft);
            title.color = Accent;
            TMP_Text step = Label(stepRect, "Step", string.Empty, 26f, TextAlignmentOptions.TopLeft);

            host.AddComponent<QuestTrackerView>().Bind(rect.gameObject, title, step);
        }

        /// <summary>ミニマップの下の時計。</summary>
        private static void BuildClock(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Clock", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, ClockTop), new Vector2(300f, 108f));
            Backdrop(rect, Panel);

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

            TMP_Text body = Label(rect, "Body", string.Empty, 28f, TextAlignmentOptions.TopLeft);
            rect.gameObject.SetActive(false);

            QuestLogView view = host.AddComponent<QuestLogView>();
            view.Bind(rect.gameObject, body);
            return view;
        }

        /// <summary>Esc のポーズメニュー。</summary>
        private static PauseMenu BuildPauseMenu(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Pause", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 560f));
            Backdrop(rect, new Color(0.04f, 0.05f, 0.09f, 0.94f));

            TMP_Text body = Label(rect, "Body", string.Empty, 30f, TextAlignmentOptions.Center);
            rect.gameObject.SetActive(false);

            PauseMenu view = host.AddComponent<PauseMenu>();
            view.Bind(rect.gameObject, body);
            return view;
        }

        /// <summary>下部の会話ウィンドウ。</summary>
        private static void BuildDialogue(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Dialogue", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 48f), new Vector2(1520f, 300f));
            Backdrop(rect, new Color(0.05f, 0.07f, 0.12f, 0.9f));

            RectTransform speakerRect = Rect(rect, "SpeakerRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, 10f), new Vector2(520f, 66f));
            Backdrop(speakerRect, new Color(0.12f, 0.2f, 0.28f, 0.95f));
            TMP_Text speaker = Label(speakerRect, "Speaker", string.Empty, 32f, TextAlignmentOptions.Left);
            speaker.color = Accent;

            RectTransform bodyRect = Stretch(rect, "BodyArea");
            bodyRect.offsetMin = new Vector2(36f, 36f);
            bodyRect.offsetMax = new Vector2(-36f, -24f);
            TMP_Text body = Label(bodyRect, "Body", string.Empty, 32f, TextAlignmentOptions.TopLeft);

            RectTransform markRect = Rect(rect, "ContinueMark", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 24f), new Vector2(40f, 40f));
            TMP_Text mark = Label(markRect, "Mark", "▼", 28f, TextAlignmentOptions.Center);
            mark.color = Accent;

            rect.gameObject.SetActive(false);
            host.AddComponent<DialogueView>().Bind(rect.gameObject, speaker, body, markRect.gameObject);
        }
    }
}
