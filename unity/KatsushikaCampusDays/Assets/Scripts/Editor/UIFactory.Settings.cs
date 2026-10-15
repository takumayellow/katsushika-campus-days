using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD.Editor
{
    /// <summary>
    /// 設定・クレジット・フォトモードの表示。Campus / Title の両方から使う。
    /// </summary>
    public static partial class UIFactory
    {
        private static readonly Color PanelDark = new Color(0.04f, 0.05f, 0.09f, 0.94f);

        /// <summary>縦長の画面でのフォトモードの操作説明の高さ。幅 800 では 2 行が 3 行に折り返す。</summary>
        private const float PhotoHintPortraitHeight = 140f;

        /// <summary>設定パネル。閉じた状態で置く。</summary>
        public static SettingsView BuildSettings(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(960f, 620f));
            Backdrop(rect, PanelDark);
            KeepInCanvas(rect);

            RectTransform headingBox = Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(900f, 70f));
            TMP_Text heading = Label(headingBox, "Label", "設定", 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;

            RectTransform bodyBox = Rect(rect, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -40f), new Vector2(820f, 460f));
            TMP_Text body = Label(bodyBox, "Label", string.Empty, 30f, TextAlignmentOptions.TopLeft);

            rect.gameObject.SetActive(false);
            SettingsView view = host.AddComponent<SettingsView>();
            view.Bind(rect.gameObject, heading, body);
            view.BindTap(AddLinkTapArea(rect, body));
            return view;
        }

        /// <summary>クレジットパネル。閉じた状態で置く。</summary>
        public static CreditsView BuildCredits(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Credits", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1240f, 720f));
            Backdrop(rect, PanelDark);
            KeepInCanvas(rect);

            RectTransform headingBox = Rect(rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(1100f, 70f));
            TMP_Text heading = Label(headingBox, "Label", "クレジット", 44f, TextAlignmentOptions.Center);
            heading.fontStyle = FontStyles.Bold;

            RectTransform bodyBox = Rect(rect, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -44f), new Vector2(1120f, 560f));
            TMP_Text body = Label(bodyBox, "Label", string.Empty, 26f, TextAlignmentOptions.TopLeft);

            rect.gameObject.SetActive(false);
            CreditsView view = host.AddComponent<CreditsView>();
            view.Bind(rect.gameObject, heading, body);
            view.BindTap(AddScreenTapArea(rect));
            return view;
        }

        /// <summary>フォトモードの操作案内とフラッシュ。案内は閉じた状態、フラッシュは透明で置く。</summary>
        public static PhotoSystem BuildPhotoOverlay(GameObject host, RectTransform parent)
        {
            RectTransform flashRect = Stretch(parent, "PhotoFlash");
            Image flash = Backdrop(flashRect, new Color(1f, 1f, 1f, 0f));
            flash.raycastTarget = false;

            // 自由カメラの操作を 2 行で出す (#12)。ゲームパッドの日本語がいちばん長いので、
            // 収まらなければ文字を小さくし、最後は … で切る（枠の外に出さない）。
            RectTransform hintRect = Rect(parent, "PhotoHint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 56f), new Vector2(1180f, 96f));
            Backdrop(hintRect, Panel);
            ReflowInCanvas(hintRect, PhotoHintPortraitHeight);
            TMP_Text hint = Label(hintRect, "Label", string.Empty, 26f, TextAlignmentOptions.Center);
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 18f;
            hint.fontSizeMax = 26f;
            hint.overflowMode = TextOverflowModes.Ellipsis;
            hintRect.gameObject.SetActive(false);

            PhotoCutIn cutIn = BuildPhotoCutIn(host, parent);
            PhotoSystem photo = host.AddComponent<PhotoSystem>();
            photo.Bind(hintRect.gameObject, hint, flash, cutIn);
            return photo;
        }

        /// <summary>
        /// 記念写真のポラロイド (#170)。画面の右に少し傾けて置き、白い台紙に 16:9 の写真と 2 行の書き込み。
        /// フラッシュより後に作るので、フラッシュが引くと上に残って見える。閉じた状態で置く。
        /// </summary>
        private static PhotoCutIn BuildPhotoCutIn(GameObject host, RectTransform parent)
        {
            const float margin = 20f;
            var photoSize = new Vector2(600f, 338f);
            var frameSize = new Vector2(photoSize.x + margin * 2f, photoSize.y + margin + 104f);

            RectTransform frame = Rect(parent, "PhotoCutIn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-96f, 48f), frameSize);
            frame.localRotation = Quaternion.Euler(0f, 0f, 3f);
            CanvasGroup group = frame.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform shadow = Stretch(frame, "Shadow");
            shadow.offsetMin = new Vector2(10f, -14f);
            shadow.offsetMax = new Vector2(10f, -14f);
            Image shadowImage = shadow.gameObject.AddComponent<Image>();
            shadowImage.color = new Color(0f, 0f, 0f, 0.35f);
            shadowImage.raycastTarget = false;

            Backdrop(frame, new Color(0.98f, 0.97f, 0.94f, 1f));

            RectTransform photoRect = Rect(frame, "Photo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -margin), photoSize);
            RawImage photo = photoRect.gameObject.AddComponent<RawImage>();
            photo.color = Color.white;
            photo.raycastTarget = false;

            RectTransform captionBox = Rect(frame, "Caption", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(photoSize.x, 98f));
            TMP_Text caption = Label(captionBox, "Label", string.Empty, 28f, TextAlignmentOptions.Center);
            caption.color = new Color(0.17f, 0.16f, 0.15f, 1f);
            caption.enableAutoSizing = true;
            caption.fontSizeMin = 18f;
            caption.fontSizeMax = 28f;
            caption.overflowMode = TextOverflowModes.Ellipsis;

            frame.gameObject.SetActive(false);
            PhotoCutIn cutIn = host.AddComponent<PhotoCutIn>();
            cutIn.Bind(frame, group, photo, caption);
            return cutIn;
        }
    }
}
