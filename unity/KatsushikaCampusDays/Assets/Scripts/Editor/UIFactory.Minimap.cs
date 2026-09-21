using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD.Editor
{
    /// <summary>右上の北固定ミニマップ。俯瞰図を円の中でずらして見せる。</summary>
    public static partial class UIFactory
    {
        private const float MinimapSize = 300f;
        private const float MinimapInner = 284f;
        private const float MinimapViewMeters = 60f;

        /// <summary>時計をミニマップの下に置くための Y。</summary>
        private const float ClockTop = -(40f + MinimapSize + 52f);

        private static void BuildMinimap(GameObject host, RectTransform parent, Transform target)
        {
            MinimapAssets.MapInfo info = MinimapAssets.ReadInfo();
            Sprite mapSprite = MinimapAssets.EnsureMapSprite();
            Sprite circle = MinimapAssets.Circle();

            RectTransform rect = Rect(parent, "Minimap", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, -40f), new Vector2(MinimapSize, MinimapSize));

            RectTransform frameRect = Centered(rect, "Frame", MinimapSize);
            Image frame = frameRect.gameObject.AddComponent<Image>();
            frame.sprite = circle;
            frame.color = new Color(0.04f, 0.06f, 0.09f, 0.9f);
            frame.raycastTarget = false;

            RectTransform maskRect = Centered(rect, "Mask", MinimapInner);
            Image maskImage = maskRect.gameObject.AddComponent<Image>();
            maskImage.sprite = circle;
            maskImage.color = new Color(0.16f, 0.22f, 0.16f, 1f);
            maskImage.raycastTarget = false;
            Mask mask = maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            float pixelsPerMeter = MinimapInner * 0.5f / MinimapViewMeters;
            float mapPixels = 2f * info.HalfExtent * pixelsPerMeter;
            RectTransform mapRect = Centered(maskRect, "Map", mapPixels);
            Image map = mapRect.gameObject.AddComponent<Image>();
            map.sprite = mapSprite;
            map.color = Color.white;
            map.raycastTarget = false;

            RectTransform markers = Centered(maskRect, "Markers", 0f);

            RectTransform arrowRect = Centered(rect, "PlayerArrow", 24f);
            Image arrow = arrowRect.gameObject.AddComponent<Image>();
            arrow.sprite = MinimapAssets.Arrow();
            arrow.color = Accent;
            arrow.raycastTarget = false;

            RectTransform northRect = Rect(rect, "North", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 6f), new Vector2(80f, 44f));
            TMP_Text north = Label(northRect, "Label", "N", 22f, TextAlignmentOptions.Center);
            north.color = Accent;
            north.fontStyle = FontStyles.Bold;

            RectTransform captionRect = Rect(rect, "Caption", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(MinimapSize, 44f));
            Backdrop(captionRect, Panel);
            TMP_Text caption = Label(captionRect, "Label", string.Empty, 22f, TextAlignmentOptions.Center);

            Minimap minimap = host.AddComponent<Minimap>();
            minimap.Bind(mapRect, markers, arrowRect, caption, MinimapAssets.Dot(), MinimapAssets.Ring());
            minimap.Configure(info.Center, info.HalfExtent, MinimapViewMeters, MinimapInner * 0.5f);
            minimap.Target = target;
        }

        /// <summary>親の中央に置く正方形。</summary>
        private static RectTransform Centered(RectTransform parent, string name, float size)
        {
            return Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(size, size));
        }
    }
}
