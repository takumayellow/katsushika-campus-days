using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KCD
{
    /// <summary>
    /// 文字の中の <c>&lt;link="id"&gt;</c> を指で叩けるようにする (#187)。
    /// 叩く範囲の大きな透明の板に付け、<see cref="Bind"/> で文字を指す。
    /// 文字そのものはスマホでは指より小さいので、リンクの四角を上下左右に広げてから一番近いものを選ぶ。
    /// どのリンクにも当たらなかったときは null を渡す（「どこでも叩けばはじめる」に使う）。
    /// </summary>
    public sealed class TmpLinkTap : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>リンクの四角を広げる幅（画面の高さに対する割合）。</summary>
        public const float MarginPerScreenHeight = 0.06f;

        private const string ChoicePrefix = "choice:";

        [SerializeField] private TMP_Text _text;

        private readonly List<Rect> _rects = new List<Rect>();
        private readonly List<string> _ids = new List<string>();

        /// <summary>叩かれたリンクの id。どのリンクにも当たらなければ null。</summary>
        public event Action<string> Tapped;

        /// <summary>SceneBuilder から差し込む。</summary>
        public void Bind(TMP_Text text)
        {
            _text = text;
        }

        /// <summary>
        /// point から margin 以内にある四角のうち、point に一番近いものの番号。どれも遠ければ -1。
        /// 近さは四角の縁までの距離（中にあれば 0）で比べ、同じなら中心が近い方。
        /// 幅の広い名前の両脇に小さな矢印があっても、名前の上を叩けば名前になる。
        /// </summary>
        public static int Pick(IReadOnlyList<Rect> rects, Vector2 point, float margin)
        {
            int best = -1;
            float bestEdge = float.MaxValue;
            float bestCenter = float.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                float dx = Mathf.Max(r.xMin - point.x, 0f, point.x - r.xMax);
                float dy = Mathf.Max(r.yMin - point.y, 0f, point.y - r.yMax);
                if (dx > margin || dy > margin)
                {
                    continue;
                }

                float edge = dx * dx + dy * dy;
                float center = (r.center - point).sqrMagnitude;
                if (edge < bestEdge || (Mathf.Approximately(edge, bestEdge) && center < bestCenter))
                {
                    best = i;
                    bestEdge = edge;
                    bestCenter = center;
                }
            }

            return best;
        }

        /// <summary>"choice:N" の N を読む。形が違えば false。</summary>
        public static bool TryParseChoice(string id, out int index)
        {
            return TryParse(id, ChoicePrefix, out index);
        }

        /// <summary>"choice:N" の N が 0 以上 count 未満ならその N。形が違うか範囲の外なら -1。</summary>
        public static int ChoiceIn(string id, int count)
        {
            return TryParseChoice(id, out int index) && index < count ? index : -1;
        }

        /// <summary>prefix のすぐ後ろに続く 0 以上の整数を読む（"inc:3" と "inc:" なら 3）。形が違えば false。</summary>
        public static bool TryParse(string id, string prefix, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(prefix) ||
                !id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(id.Substring(prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (TouchInput.IsEmulatedMouse(eventData))
            {
                return;
            }

            Tapped?.Invoke(FindLink(eventData.position, eventData.pressEventCamera));
        }

        private string FindLink(Vector2 screenPoint, Camera eventCamera)
        {
            if (_text == null || !_text.gameObject.activeInHierarchy)
            {
                return null;
            }

            _text.ForceMeshUpdate();
            TMP_TextInfo info = _text.textInfo;
            _rects.Clear();
            _ids.Clear();
            Camera camera = CameraFor(eventCamera);
            for (int i = 0; i < info.linkCount; i++)
            {
                if (TryLinkRect(info, info.linkInfo[i], camera, out Rect rect))
                {
                    _rects.Add(rect);
                    _ids.Add(info.linkInfo[i].GetLinkID());
                }
            }

            int index = Pick(_rects, screenPoint, Screen.height * MarginPerScreenHeight);
            return index >= 0 ? _ids[index] : null;
        }

        private Camera CameraFor(Camera eventCamera)
        {
            Canvas canvas = _text.canvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera != null ? canvas.worldCamera : eventCamera;
        }

        /// <summary>リンクの文字を囲む四角を画面の座標で返す。見える文字が 1 つも無ければ false。</summary>
        private bool TryLinkRect(TMP_TextInfo info, TMP_LinkInfo link, Camera camera, out Rect rect)
        {
            rect = default;
            bool any = false;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            int end = Mathf.Min(link.linkTextfirstCharacterIndex + link.linkTextLength, info.characterCount);
            for (int c = link.linkTextfirstCharacterIndex; c < end; c++)
            {
                TMP_CharacterInfo character = info.characterInfo[c];
                if (!character.isVisible)
                {
                    continue;
                }

                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera,
                    _text.transform.TransformPoint(character.bottomLeft));
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera,
                    _text.transform.TransformPoint(character.topRight));
                min = Vector2.Min(min, Vector2.Min(bottomLeft, topRight));
                max = Vector2.Max(max, Vector2.Max(bottomLeft, topRight));
                any = true;
            }

            if (any)
            {
                rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            return any;
        }
    }
}
