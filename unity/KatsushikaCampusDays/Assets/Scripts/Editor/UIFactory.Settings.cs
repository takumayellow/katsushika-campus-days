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

        /// <summary>設定パネル。閉じた状態で置く。</summary>
        public static SettingsView BuildSettings(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(960f, 620f));
            Backdrop(rect, PanelDark);

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
            return view;
        }

        /// <summary>クレジットパネル。閉じた状態で置く。</summary>
        public static CreditsView BuildCredits(GameObject host, RectTransform parent)
        {
            RectTransform rect = Rect(parent, "Credits", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1240f, 720f));
            Backdrop(rect, PanelDark);

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
            return view;
        }

        /// <summary>フォトモードの操作案内とフラッシュ。案内は閉じた状態、フラッシュは透明で置く。</summary>
        public static PhotoSystem BuildPhotoOverlay(GameObject host, RectTransform parent)
        {
            RectTransform flashRect = Stretch(parent, "PhotoFlash");
            Image flash = Backdrop(flashRect, new Color(1f, 1f, 1f, 0f));
            flash.raycastTarget = false;

            RectTransform hintRect = Rect(parent, "PhotoHint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 56f), new Vector2(760f, 56f));
            Backdrop(hintRect, Panel);
            TMP_Text hint = Label(hintRect, "Label", string.Empty, 28f, TextAlignmentOptions.Center);
            hintRect.gameObject.SetActive(false);

            PhotoSystem photo = host.AddComponent<PhotoSystem>();
            photo.Bind(hintRect.gameObject, hint, flash);
            return photo;
        }
    }
}
