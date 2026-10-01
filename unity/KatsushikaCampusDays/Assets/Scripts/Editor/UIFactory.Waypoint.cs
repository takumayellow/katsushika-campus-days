using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KCD.Editor
{
    public static partial class UIFactory
    {
        /// <summary>
        /// 画面の上の目的地の印 (#163)。輪（見えているとき）・向きの矢印（画面の外のとき）・距離の札。
        /// ほかの HUD より奥に描くよう、Gameplay のいちばん先に置く。
        /// </summary>
        private static void BuildWaypoint(GameObject host, RectTransform parent, Transform target)
        {
            RectTransform area = Stretch(parent, "Waypoint");

            RectTransform marker = Rect(area, "Marker", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(56f, 56f));

            Image ring = marker.gameObject.AddComponent<Image>();
            ring.sprite = MinimapAssets.Ring();
            ring.color = Accent;
            ring.raycastTarget = false;

            RectTransform arrow = Rect(marker, "Arrow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(44f, 44f));
            Image arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = MinimapAssets.Arrow();
            arrowImage.color = Accent;
            arrowImage.raycastTarget = false;

            RectTransform labelRect = Rect(marker, "Distance", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(200f, 40f));
            Backdrop(labelRect, Panel);
            TMP_Text label = Label(labelRect, "Label", string.Empty, 22f, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)label.transform).offsetMin = new Vector2(8f, 2f);
            ((RectTransform)label.transform).offsetMax = new Vector2(-8f, -2f);

            marker.gameObject.SetActive(false);
            host.AddComponent<ObjectiveWaypoint>().Bind(area, marker, ring, arrow, label, target);
        }
    }
}
