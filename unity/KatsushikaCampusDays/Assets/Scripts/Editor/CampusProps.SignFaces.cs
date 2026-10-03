using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 看板の板に貼る文字 (#181)。
    ///
    /// 入口の立て看板（blender/kcd_lib/props.py の add_pylon_sign）は「文字は Unity 側で貼る」約束の白い板で、
    /// Unity 側が板の上に浮かぶ名札（BuildingLabel）しか置いていなかったので、表も裏も白紙だった。
    /// 共創棟の店舗（スターバックスとファミリーマート）の色板は、FBX の面の表が建物の壁の側を向いていて、
    /// 片面のマテリアルなのでモールからは裏面として描かれず、色板ごと見えていなかった。
    ///
    /// 立て看板には表と裏の両方に、店舗には色板のモール側に、カメラを向かない TextMeshPro を板と平行に貼る。
    /// 言語の切り替えは SignFaceText が受けて ui.building.&lt;id&gt; を引き直す。
    /// 文字のマテリアルはフォントの既定マテリアルを共有させる。renderer.material・fontMaterial・
    /// outlineWidth / outlineColor の setter は編集時にマテリアルを複製してシーンへ埋め込むので使わない（#64）。
    /// 名札の縁取りの共有マテリアル（FontLibrary.EnsureSignMaterial）は呼ぶと名札用の値で上書きされるので、ここでは使わない。
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

        /// <summary>店舗の色板（FBX の面）から店名を浮かせる距離（m）。モールの側へ出す。</summary>
        private const float StoreTextGap = 0.03f;

        /// <summary>色板の手前に足す板を、元の面からモールの側へずらす距離（m）。重ねると元の面とちらつく。</summary>
        private const float StoreBoardGap = 0.005f;

        /// <summary>共創棟の店舗の色板のマテリアル。ファミリーマートは白の板の上下に緑と青の帯が重なる。</summary>
        private static readonly string[] StoreBoardMaterials =
        {
            "sign_starbucks_green",
            "sign_familymart_white",
            "sign_familymart_green",
            "sign_familymart_blue"
        };

        /// <summary>
        /// 立て看板の文字。白い面（2.4 x 0.92 m）より一回り小さい枠に、濃紺で入れる。
        /// 長い英語名（Experiment Building 1 など）は枠に合わせて縮め、2 行に折り返す。
        /// </summary>
        private static readonly FaceStyle PylonStyle =
            new FaceStyle(new Vector2(2.2f, 0.8f), 4.5f, 1.2f, new Color32(26, 42, 79, 255));

        /// <summary>スターバックスの色板（9.2 x 1.6 m の緑）に白で入れる。</summary>
        private static readonly FaceStyle StarbucksStyle =
            new FaceStyle(new Vector2(8.6f, 1.4f), 8f, 2f, new Color32(255, 255, 255, 255));

        /// <summary>ファミリーマートの色板の、緑と青の帯に挟まれた白い帯（高さ 0.8 m）に青で入れる。</summary>
        private static readonly FaceStyle FamilyMartStyle =
            new FaceStyle(new Vector2(8.6f, 0.7f), 5.5f, 2f, new Color32(0, 104, 183, 255));

        /// <summary>店名を貼る色板。Id は ui.building.&lt;Id&gt; と BuildingNames の鍵、Plate は文字を貼る面のマテリアル。</summary>
        private static readonly (string Id, string Plate, FaceStyle Style)[] StoreFaces =
        {
            ("starbucks_green", "sign_starbucks_green", StarbucksStyle),
            ("familymart_green", "sign_familymart_white", FamilyMartStyle)
        };

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

        /// <summary>立て看板と店舗の色板に名前を貼る。</summary>
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
            int boards = FaceStoreBoards(group.transform);
            int stores = PlaceStoreTexts(group.transform, font);
            EditorPaths.Report("看板の板に名前を " + (pylons + stores) + " 面貼りました（立て看板 " + pylons
                + " 面、店舗 " + stores + " 面、モールを向けて足した色板 " + boards + " 枚）。");
        }

        /// <summary>入口の立て看板の白い面に、表と裏のどちらからも読める名前を貼る。貼った面の数を返す。</summary>
        private static int PlacePylonTexts(Transform group, TMP_FontAsset font)
        {
            float offset = PylonHalfThickness + PylonFaceGap;
            int count = 0;
            foreach (string id in BuildingNames.Keys)
            {
                Transform sign = CampusStage.FindChild("sign_" + id);
                if (sign == null || !DoorFrame(id, out _, out Vector3 outward))
                {
                    continue;
                }

                // 板の正面は扉の外向き（door_ → entrance_）。表は外から、裏は建物の側から読む。
                Vector3 white = sign.position + Vector3.down * PylonWhiteDrop;
                PlaceFaceText(group, "SignText_" + id + "_front", font, id, white + outward * offset, outward, PylonStyle);
                PlaceFaceText(group, "SignText_" + id + "_back", font, id, white - outward * offset, -outward, PylonStyle);
                count += 2;
            }

            return count;
        }

        /// <summary>店舗の色板のモールの側に店名を貼る。貼った面の数を返す。</summary>
        private static int PlaceStoreTexts(Transform group, TMP_FontAsset font)
        {
            int count = 0;
            foreach ((string id, string plate, FaceStyle style) in StoreFaces)
            {
                if (!PlateCenter(plate, out Vector3 center))
                {
                    continue;
                }

                PlaceFaceText(group, "SignText_" + id, font, id, center + AxisV * StoreTextGap, AxisV, style);
                count++;
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

        /// <summary>
        /// 店舗の色板のうち、表が建物の側を向いている面の手前に、モールを向いた同じマテリアルの板を足す。足した数を返す。
        /// FBX の面の表がモールを向いていれば何も足さない。
        /// </summary>
        private static int FaceStoreBoards(Transform group)
        {
            GameObject campus = CampusStage.Instance;
            if (campus == null)
            {
                return 0;
            }

            var boardMaterials = new HashSet<string>(StoreBoardMaterials);
            int count = 0;
            foreach (MeshRenderer renderer in campus.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                Vector3[] vertices = null;
                for (int i = 0; i < materials.Length && i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || !boardMaterials.Contains(materials[i].name))
                    {
                        continue;
                    }

                    vertices = vertices ?? mesh.vertices;
                    if (!SubMeshFace(renderer.transform.localToWorldMatrix, vertices, mesh.GetTriangles(i),
                            out Vector3 center, out Vector3 normal, out Vector2 size)
                        || Vector3.Dot(normal, AxisV) >= 0f)
                    {
                        continue;
                    }

                    PlaceStoreBoard(group, renderer, materials[i], center, normal, size);
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 縦に立った面の表の向き・中心・幅と高さ（ワールド）。表の向きは三角形の巡る向きから面積の重みで出す。
        /// 幅は面に沿った水平の広がり、高さは鉛直の広がり。三角形が無いか潰れていれば false。
        /// </summary>
        private static bool SubMeshFace(Matrix4x4 localToWorld, Vector3[] vertices, int[] triangles,
            out Vector3 center, out Vector3 normal, out Vector2 size)
        {
            center = Vector3.zero;
            size = Vector2.zero;
            Vector3 winding = Vector3.zero;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                winding += Vector3.Cross(vertices[triangles[t + 1]] - a, vertices[triangles[t + 2]] - a);
            }

            // 頂点の法線と同じく逆転置で移す。負の拡大で鏡にした物は Unity が裏表を入れ替えて描くので、これで表の向きになる。
            normal = localToWorld.inverse.transpose.MultiplyVector(winding);
            if (normal.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            normal.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, normal);
            if (side.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            side.Normalize();
            Vector3 first = localToWorld.MultiplyPoint3x4(vertices[triangles[0]]);
            var bounds = new Bounds(first, Vector3.zero);
            float minSide = Vector3.Dot(first, side);
            float maxSide = minSide;
            foreach (int index in triangles)
            {
                Vector3 point = localToWorld.MultiplyPoint3x4(vertices[index]);
                bounds.Encapsulate(point);
                float along = Vector3.Dot(point, side);
                minSide = Mathf.Min(minSide, along);
                maxSide = Mathf.Max(maxSide, along);
            }

            center = bounds.center;
            size = new Vector2(maxSide - minSide, bounds.size.y);
            return true;
        }

        /// <summary>元の面と同じ大きさ・マテリアルの板を、表をモールへ向けて元の面のすぐ手前に置く。</summary>
        private static void PlaceStoreBoard(Transform group, MeshRenderer source, Material material,
            Vector3 center, Vector3 normal, Vector2 size)
        {
            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.name = "SignBoard_" + material.name;
            board.layer = source.gameObject.layer;
            board.transform.SetParent(group, false);

            // Quad の表はローカル −Z。+Z を元の面の表（壁の側）へ向けると、表がモールを向く。
            board.transform.SetPositionAndRotation(center - normal * StoreBoardGap, Quaternion.LookRotation(normal, Vector3.up));
            board.transform.localScale = new Vector3(size.x, size.y, 1f);

            MeshRenderer renderer = board.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            GameObjectUtility.SetStaticEditorFlags(board, GameObjectUtility.GetStaticEditorFlags(source.gameObject));
        }
    }
}
