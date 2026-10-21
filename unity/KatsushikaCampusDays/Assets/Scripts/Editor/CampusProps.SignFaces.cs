using TMPro;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 入口の立て看板の板に貼る建物の名前 (#181)。
    ///
    /// 立て看板（blender/kcd_lib/props.py の add_pylon_sign）は「文字は Unity 側で貼る」約束の白い板。
    /// 表と裏の両方に、カメラを向かない TextMeshPro を板と平行に貼る。建物の名前が出るのはこの板だけで、
    /// 板の外に浮かぶ札は置かない。店（共創棟のカフェとコンビニ）は外に看板も名前も出さない。
    /// 言語の切り替えは SignFaceText が受けて ui.building.&lt;id&gt; を引き直す。
    /// 文字のマテリアルはフォントの既定マテリアルを共有させる。renderer.material・fontMaterial・
    /// outlineWidth / outlineColor の setter は編集時にマテリアルを複製してシーンへ埋め込むので使わない（#64）。
    /// </summary>
    public static partial class CampusProps
    {
        /// <summary>立て看板の板の厚みの半分（m）。add_pylon_sign の箱は板の法線の向きに ±0.05 m。</summary>
        private const float PylonHalfThickness = 0.05f;

        /// <summary>板の面から文字を浮かせる距離（m）。面と同じ奥行きに置くと、面と文字が交互に描かれてちらつく。</summary>
        private const float PylonFaceGap = 0.01f;

        /// <summary>
        /// sign_&lt;id&gt;（板の中心）から白い面の中心までの下がり（m）。板の高さ 1.2 m のうち上の 0.28 m は緑の帯なので、
        /// 白い面は板の中心の −0.6〜+0.32 m、その中心は −0.14 m。
        /// </summary>
        private const float PylonWhiteDrop = 0.14f;

        /// <summary>
        /// 立て看板の文字。白い面（2.4 x 0.92 m）より一回り小さい枠に、濃紺で入れる。
        /// 長い英語名（Experiment Building 1 など）は枠に合わせて縮め、2 行に折り返す。
        /// </summary>
        private static readonly FaceStyle PylonStyle =
            new FaceStyle(new Vector2(2.2f, 0.8f), 4.5f, 1.2f, new Color32(26, 42, 79, 255));

        /// <summary>板に貼る文字の枠（m）・自動で合わせる文字の大きさの上限と下限・文字の色。</summary>
        private readonly struct FaceStyle
        {
            public readonly Vector2 Frame;
            public readonly float FontMax;
            public readonly float FontMin;
            public readonly Color32 Ink;

            public FaceStyle(Vector2 frame, float fontMax, float fontMin, Color32 ink)
            {
                Frame = frame;
                FontMax = fontMax;
                FontMin = fontMin;
                Ink = ink;
            }
        }

        /// <summary>立て看板に名前を貼る。</summary>
        private static void PlaceSignFaces(Transform parent, TMP_FontAsset font)
        {
            if (font == null)
            {
                EditorPaths.Report("日本語フォントが無いので、看板の板に名前を貼れませんでした。");
                return;
            }

            var group = new GameObject("SignFaces");
            group.transform.SetParent(parent, false);

            int pylons = PlacePylonTexts(group.transform, font);
            EditorPaths.Report("入口の立て看板に名前を " + pylons + " 面貼りました。");
        }

        /// <summary>
        /// 入口の立て看板の白い面に、表と裏のどちらからも読める名前を貼る。貼った面の数を返す。
        /// 2 つ目からの扉の看板（sign_lecture_se）にも、その扉の建物の名前を貼る。
        /// </summary>
        private static int PlacePylonTexts(Transform group, TMP_FontAsset font)
        {
            float offset = PylonHalfThickness + PylonFaceGap;
            int count = 0;
            var doors = new System.Collections.Generic.List<string>(BuildingNames.Keys);
            doors.AddRange(BuildingDoors.SecondDoorIds);
            foreach (string doorId in doors)
            {
                Transform sign = CampusStage.FindChild("sign_" + doorId);
                if (sign == null || !DoorFrame(doorId, out _, out Vector3 outward))
                {
                    continue;
                }

                // 板の正面は扉の外向き（door_ → entrance_）。表は外から、裏は建物の側から読む。
                string id = BuildingDoors.BuildingOf(doorId);
                Vector3 white = sign.position + Vector3.down * PylonWhiteDrop;
                PlaceFaceText(group, "SignText_" + doorId + "_front", font, id, white + outward * offset, outward, PylonStyle);
                PlaceFaceText(group, "SignText_" + doorId + "_back", font, id, white - outward * offset, -outward, PylonStyle);
                count += 2;
            }

            return count;
        }

        /// <summary>facing の側から読める、板と平行な名前を置く。</summary>
        private static void PlaceFaceText(Transform group, string name, TMP_FontAsset font, string buildingId,
            Vector3 position, Vector3 facing, FaceStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group, false);

            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = style.Ink;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = style.FontMin;
            text.fontSizeMax = style.FontMax;
            text.fontSize = style.FontMax;
            text.rectTransform.sizeDelta = style.Frame;

            // TextMeshPro の文字はローカル −Z の側から正しい向きに読める。+Z を板の内側へ向け、facing の側から読ませる。
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-facing, Vector3.up));

            go.AddComponent<SignFaceText>().Bind("ui.building." + buildingId, DisplayName(buildingId));
        }
    }
}
