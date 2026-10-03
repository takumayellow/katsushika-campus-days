using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KCD.Tests
{
    /// <summary>
    /// 構内の看板の板に名前が貼ってあること (#181)。
    ///
    /// 入口の立て看板 9 枚は、上に浮かぶ名札（BuildingLabel）はあっても、板そのものは表も裏も白紙だった。
    /// 共創棟の店舗の色板は FBX の面の表が建物の壁の側を向いていて、モールからは色板ごと見えなかった。
    /// CampusProps.SignFaces が焼いた文字（SignFaceText）と色板を、FBX の板の形と突き合わせて確かめる。
    /// </summary>
    public sealed class CampusSignFaceTests
    {
        private const string ScenePath = "Assets/Scenes/Campus.unity";

        /// <summary>立て看板の白い面のマテリアル（blender/kcd_lib/props.py の add_pylon_sign）。上の緑の帯は別のマテリアル。</summary>
        private const string PylonMaterial = "sign_plate";

        /// <summary>入口の立て看板の数（研究棟 2・講義棟・共創棟・図書館・体育館・実験棟 2・温室）。</summary>
        private const int PylonCount = 9;

        /// <summary>CampusProps.AxisV と同じ、モールの側を指す向き（テストのアセンブリは KCD.Editor を参照しない）。</summary>
        private static readonly Vector3 MallSide = new Vector3(0.41550916f, 0f, 0.90958899f);

        /// <summary>文字と板の面のあいだに要る隙間の下限（m）。これより近いと面と文字が交互に描かれてちらつく。</summary>
        private const float MinGap = 0.002f;

        /// <summary>文字が板の面から離れてよい上限（m）。これより離れると斜めから見て板から浮いて見える。</summary>
        private const float MaxGap = 0.1f;

        /// <summary>向きがそろっているとみなす内積の下限（約 8 度以内）。</summary>
        private const float Aligned = 0.99f;

        /// <summary>
        /// 立て看板の厚みの上限（m）。板は扉の外向きに ±0.05 m の箱なので、外向きと平行なら 0.1 m。
        /// 板が外向きから回っていると、幅 2.4 m が外向きの厚みに混ざって太る。
        /// </summary>
        private const float MaxPylonThickness = 0.15f;

        /// <summary>立て看板の周りで、その看板の板と名前を拾う水平の半径（m）。隣の看板は 10 m 以上離れている。</summary>
        private const float PylonReach = 2f;

        /// <summary>共創棟の店舗の色板のマテリアル。ファミリーマートは白い板の上下に緑と青の帯が重なる。</summary>
        private static readonly string[] StoreMaterials =
        {
            "sign_starbucks_green",
            "sign_familymart_white",
            "sign_familymart_green",
            "sign_familymart_blue"
        };

        /// <summary>店名の辞書の鍵と、店名を貼る色板のマテリアル。ファミリーマートは緑と青の帯に挟まれた白い板に貼る。</summary>
        private static readonly (string Key, string Plate)[] StoreSigns =
        {
            ("ui.building.starbucks_green", "sign_starbucks_green"),
            ("ui.building.familymart_green", "sign_familymart_white")
        };

        private Scene _scene;
        private readonly Dictionary<string, Transform> _marks = new Dictionary<string, Transform>();
        private readonly List<SignFaceText> _faces = new List<SignFaceText>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<string> _pylons = new List<string>();

        [OneTimeSetUp]
        public void OpenCampus()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Transform mark in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!_marks.ContainsKey(mark.name))
                    {
                        _marks.Add(mark.name, mark);
                    }
                }

                _faces.AddRange(root.GetComponentsInChildren<SignFaceText>(false));
                _renderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(false));
            }

            // 立て看板は FBX の sign_<id>（板の中心）に door_<id> と entrance_<id> がそろう建物。
            // 屋内の案内板の sign_<id>_<n> には扉の印が無いので入らない。
            foreach (string name in _marks.Keys)
            {
                if (!name.StartsWith("sign_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                string id = name.Substring("sign_".Length);
                if (_marks.ContainsKey("door_" + id) && _marks.ContainsKey("entrance_" + id))
                {
                    _pylons.Add(id);
                }
            }

            _pylons.Sort(System.StringComparer.Ordinal);
        }

        [OneTimeTearDown]
        public void CloseCampus()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void 入口の立て看板は表と裏の両方に名前がある()
        {
            Assert.GreaterOrEqual(_pylons.Count, PylonCount,
                ScenePath + " の入口の立て看板（sign_/door_/entrance_）が足りない: " + string.Join(", ", _pylons));

            var problems = new List<string>();
            foreach (string id in _pylons)
            {
                Pylon pylon = PylonOf(id);
                if (pylon == null)
                {
                    problems.Add(id + ": sign_" + id + " の周りに " + PylonMaterial + " の板が無い");
                    continue;
                }

                if (pylon.Front - pylon.Back > MaxPylonThickness)
                {
                    problems.Add(id + ": 板が扉の外向きと平行でない（外向きの厚み " + (pylon.Front - pylon.Back).ToString("F3") + " m）");
                }

                bool front = false;
                bool back = false;
                foreach (SignFaceText face in FacesNear("ui.building." + id, pylon.Center, PylonReach))
                {
                    float along = Vector3.Dot(face.transform.position - pylon.Center, pylon.Normal);
                    if (along > pylon.Front + MinGap && along <= pylon.Front + MaxGap)
                    {
                        front = true;
                    }
                    else if (along < pylon.Back - MinGap && along >= pylon.Back - MaxGap)
                    {
                        back = true;
                    }
                    else
                    {
                        problems.Add(id + ": " + face.name + " が板の面のすぐ外にない（板の中心から外向きに "
                            + along.ToString("F3") + " m、面は " + pylon.Back.ToString("F3") + " / " + pylon.Front.ToString("F3") + " m）");
                    }
                }

                if (!front)
                {
                    problems.Add(id + ": 表（扉の外向き）の面に名前が無い");
                }

                if (!back)
                {
                    problems.Add(id + ": 裏（建物の側）の面に名前が無い");
                }
            }

            Assert.IsEmpty(problems, "立て看板の名前（KCD/シーンを組み直す）:\n" + string.Join("\n", problems));
        }

        [Test]
        public void 立て看板の名前は白い面からはみ出さない()
        {
            Assert.GreaterOrEqual(_pylons.Count, PylonCount, ScenePath + " の入口の立て看板が足りない");

            var problems = new List<string>();
            ForEachLocale(locale =>
            {
                foreach (string id in _pylons)
                {
                    Pylon pylon = PylonOf(id);
                    if (pylon == null)
                    {
                        continue;
                    }

                    foreach (SignFaceText face in FacesNear("ui.building." + id, pylon.Center, PylonReach))
                    {
                        TextMeshPro text = Refresh(face);
                        if (text.textInfo.characterCount == 0)
                        {
                            problems.Add(locale + ": " + face.name + " が空");
                            continue;
                        }

                        Rect glyphs = RectOf(TextCorners(text), pylon.Side);
                        if (!Contains(pylon.White, glyphs))
                        {
                            problems.Add(locale + ": " + face.name + " 「" + text.text + "」が白い面 " + Describe(pylon.White)
                                + " からはみ出す " + Describe(glyphs));
                        }
                    }
                }
            });

            Assert.IsEmpty(problems, "立て看板の名前の収まり:\n" + string.Join("\n", problems));
        }

        [Test]
        public void 看板の名前は板の外から読める向きで鏡文字にならない()
        {
            var problems = new List<string>();
            foreach (string id in _pylons)
            {
                Pylon pylon = PylonOf(id);
                if (pylon == null)
                {
                    continue;
                }

                foreach (SignFaceText face in FacesNear("ui.building." + id, pylon.Center, PylonReach))
                {
                    float along = Vector3.Dot(face.transform.position - pylon.Center, pylon.Normal);
                    CheckReadableFrom(face, along >= 0f ? pylon.Normal : -pylon.Normal, problems);
                }
            }

            foreach ((string key, _) in StoreSigns)
            {
                foreach (SignFaceText face in _faces.FindAll(f => f.Key == key))
                {
                    CheckReadableFrom(face, MallSide, problems);
                }
            }

            Assert.IsEmpty(problems, "看板の名前の向き:\n" + string.Join("\n", problems));
        }

        [Test]
        public void 看板の名前はどの言語の辞書にもある()
        {
            Assert.GreaterOrEqual(_faces.Count, PylonCount * 2 + StoreSigns.Length, ScenePath + " の看板の名前が足りない（KCD/シーンを組み直す）");

            const string Missing = "<missing>";
            var missing = new List<string>();
            ForEachLocale(locale =>
            {
                foreach (SignFaceText face in _faces)
                {
                    if (string.IsNullOrEmpty(face.Key) || BuildingLabel.Resolve(face.Key, Missing) == Missing)
                    {
                        missing.Add(locale + ": " + face.name + " (" + face.Key + ")");
                    }
                }
            });

            Assert.IsEmpty(missing, "辞書に無い看板の名前（英語表示でも日本語の名前が焼いたまま出る）:\n" + string.Join("\n", missing));
        }

        [Test]
        public void 看板の名前はフォントの既定マテリアルを共有する()
        {
            Assert.IsNotEmpty(_faces, ScenePath + " に看板の名前が無い（KCD/シーンを組み直す）");

            foreach (SignFaceText face in _faces)
            {
                TextMeshPro text = face.GetComponent<TextMeshPro>();
                Assert.IsNotNull(text, face.name + " に TextMeshPro が無い");
                Assert.IsNotNull(text.font, face.name + " にフォントが無い");

                // 編集時に fontMaterial や outlineWidth を触ると、複製（(Instance)）がシーンに埋め込まれる（#64）。
                Material shared = text.fontSharedMaterial;
                Assert.AreSame(text.font.material, shared, face.name + " がフォントの既定マテリアルを使っていない: " + AssetPathOf(shared));
                Assert.IsNotEmpty(AssetDatabase.GetAssetPath(shared), face.name + " のマテリアルがシーンに埋め込まれている");
                Assert.AreSame(shared, text.GetComponent<MeshRenderer>().sharedMaterial, face.name + " の MeshRenderer に別のマテリアルが差さっている");
            }
        }

        [Test]
        public void 店舗の色板はモールから見えて店名が貼ってある()
        {
            var problems = new List<string>();
            var mallBoards = new List<Surface>();
            foreach (string material in StoreMaterials)
            {
                List<Surface> seen = SurfacesOf(material).FindAll(s => SeenFrom(s, MallSide));
                if (seen.Count == 0)
                {
                    problems.Add(material + " の色板の表がモールを向いていない（裏面になって描かれない）");
                }

                mallBoards.AddRange(seen);
            }

            var signs = new List<(Surface Board, SignFaceText Face)>();
            foreach ((string key, string plate) in StoreSigns)
            {
                Surface board = mallBoards.Find(s => s.Material == plate);
                if (board == null)
                {
                    continue;
                }

                List<SignFaceText> faces = FacesNear(key, board.Center, PylonReach);
                if (faces.Count == 0)
                {
                    problems.Add(plate + " の色板に店名（" + key + "）が無い");
                }

                foreach (SignFaceText face in faces)
                {
                    float gap = Vector3.Dot(face.transform.position - board.Center, MallSide);
                    if (gap <= MinGap || gap > MaxGap)
                    {
                        problems.Add(face.name + " が色板の面から " + gap.ToString("F3") + " m（"
                            + MinGap + "〜" + MaxGap + " m のモールの側に置く）");
                    }

                    signs.Add((board, face));
                }
            }

            Vector3 side = Vector3.Cross(Vector3.up, MallSide);
            ForEachLocale(locale =>
            {
                foreach ((Surface board, SignFaceText face) in signs)
                {
                    TextMeshPro text = Refresh(face);
                    if (text.textInfo.characterCount == 0)
                    {
                        problems.Add(locale + ": " + face.name + " が空");
                        continue;
                    }

                    Rect plateRect = RectOf(board.Points, side);
                    Rect glyphs = RectOf(TextCorners(text), side);
                    if (!Contains(plateRect, glyphs))
                    {
                        problems.Add(locale + ": " + face.name + " 「" + text.text + "」が色板 " + Describe(plateRect)
                            + " からはみ出す " + Describe(glyphs));
                    }

                    // ファミリーマートの緑と青の帯は白い板より手前にある。店名は帯に挟まれた白いところに収める。
                    foreach (Surface other in mallBoards)
                    {
                        bool inFront = Vector3.Dot(other.Center - face.transform.position, MallSide) > 0f;
                        if (other != board && inFront && RectOf(other.Points, side).Overlaps(glyphs))
                        {
                            problems.Add(locale + ": " + face.name + " 「" + text.text + "」が手前の " + other.Material + " の板に隠れる");
                        }
                    }
                }
            });

            Assert.IsEmpty(problems, "店舗の色板（KCD/シーンを組み直す）:\n" + string.Join("\n", problems));
        }

        /// <summary>立て看板 1 枚。距離はどれも板の中心（sign_&lt;id&gt;）から外向き（door_ → entrance_）に測る。</summary>
        private sealed class Pylon
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Side;
            public float Front;
            public float Back;
            public Rect White;
        }

        /// <summary>サブメッシュ 1 つぶんの面。表の向きは三角形の巡る向きから出す（片面のマテリアルは表だけ描く）。</summary>
        private sealed class Surface
        {
            public string Material;
            public Vector3 Center;
            public Vector3 Normal;
            public bool DoubleSided;
            public List<Vector3> Points;
        }

        private Pylon PylonOf(string id)
        {
            Vector3 center = _marks["sign_" + id].position;
            Vector3 normal = _marks["entrance_" + id].position - _marks["door_" + id].position;
            normal.y = 0f;
            normal.Normalize();
            var pylon = new Pylon
            {
                Center = center,
                Normal = normal,
                Side = Vector3.Cross(Vector3.up, normal),
                Front = float.MinValue,
                Back = float.MaxValue
            };

            var points = new List<Vector3>();
            foreach (Surface surface in SurfacesOf(PylonMaterial))
            {
                foreach (Vector3 point in surface.Points)
                {
                    Vector3 offset = point - center;
                    if (new Vector2(offset.x, offset.z).magnitude > PylonReach || Mathf.Abs(offset.y) > 1.5f)
                    {
                        continue;
                    }

                    points.Add(point);
                    float along = Vector3.Dot(offset, normal);
                    pylon.Front = Mathf.Max(pylon.Front, along);
                    pylon.Back = Mathf.Min(pylon.Back, along);
                }
            }

            if (points.Count == 0)
            {
                return null;
            }

            pylon.White = RectOf(points, pylon.Side);
            return pylon;
        }

        private List<Surface> SurfacesOf(string material)
        {
            var surfaces = new List<Surface>();
            foreach (MeshRenderer renderer in _renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (!renderer.enabled || mesh == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
                {
                    if (materials[s] != null && materials[s].name == material)
                    {
                        surfaces.Add(SurfaceOf(renderer.transform.localToWorldMatrix, mesh, s, materials[s]));
                    }
                }
            }

            return surfaces;
        }

        private static Surface SurfaceOf(Matrix4x4 localToWorld, Mesh mesh, int subMesh, Material material)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(subMesh);
            Vector3 winding = Vector3.zero;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                winding += Vector3.Cross(vertices[triangles[t + 1]] - a, vertices[triangles[t + 2]] - a);
            }

            var points = new List<Vector3>(triangles.Length);
            foreach (int index in triangles)
            {
                points.Add(localToWorld.MultiplyPoint3x4(vertices[index]));
            }

            var bounds = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
            foreach (Vector3 point in points)
            {
                bounds.Encapsulate(point);
            }

            return new Surface
            {
                Material = material.name,
                Center = bounds.center,
                // 頂点の法線と同じく逆転置で移す（負の拡大で鏡にした物は Unity が裏表を入れ替えて描く）。
                Normal = localToWorld.inverse.transpose.MultiplyVector(winding).normalized,
                DoubleSided = material.HasProperty("_Cull") && Mathf.Approximately(material.GetFloat("_Cull"), 0f),
                Points = points
            };
        }

        /// <summary>from の側から見たとき面が描かれるか。片面なら表が from を向いていること。</summary>
        private static bool SeenFrom(Surface surface, Vector3 from)
        {
            float facing = Vector3.Dot(surface.Normal, from);
            return facing > Aligned || (surface.DoubleSided && facing < -Aligned);
        }

        /// <summary>key の名前のうち、center から水平に reach m 以内のもの。</summary>
        private List<SignFaceText> FacesNear(string key, Vector3 center, float reach)
        {
            return _faces.FindAll(face =>
            {
                Vector3 offset = face.transform.position - center;
                return face.Key == key && new Vector2(offset.x, offset.z).magnitude <= reach;
            });
        }

        /// <summary>文字が reader の側から正しい向きで読めること。TextMeshPro はローカル −Z の側から読める。</summary>
        private static void CheckReadableFrom(SignFaceText face, Vector3 reader, List<string> problems)
        {
            Transform t = face.transform;
            if (Vector3.Dot(-t.forward, reader) < Aligned)
            {
                problems.Add(face.name + " が板の外から読める向きでない（読める側 " + (-t.forward).ToString("F2") + "、外 " + reader.ToString("F2") + "）");
            }

            if (Vector3.Dot(t.up, Vector3.up) < Aligned)
            {
                problems.Add(face.name + " が傾いている（上 " + t.up.ToString("F2") + "）");
            }

            if (t.lossyScale.x <= 0f || t.lossyScale.y <= 0f)
            {
                problems.Add(face.name + " が鏡文字になる（拡大 " + t.lossyScale.ToString("F2") + "）");
            }
        }

        /// <summary>シーンに焼いた文字を、いまの言語で引き直して形を作り直す（SignFaceText は編集時に動かない）。</summary>
        private static TextMeshPro Refresh(SignFaceText face)
        {
            TextMeshPro text = face.GetComponent<TextMeshPro>();
            text.text = BuildingLabel.Resolve(face.Key, text.text);
            text.ForceMeshUpdate();
            return text;
        }

        /// <summary>字の形の四隅（ワールド）。</summary>
        private static Vector3[] TextCorners(TextMeshPro text)
        {
            Bounds bounds = text.textBounds;
            Transform t = text.transform;
            return new[]
            {
                t.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, 0f)),
                t.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, 0f)),
                t.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, 0f)),
                t.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, 0f))
            };
        }

        /// <summary>点を板の面へ写した範囲。x は side の向き、y は高さ。</summary>
        private static Rect RectOf(IEnumerable<Vector3> points, Vector3 side)
        {
            float xMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            foreach (Vector3 point in points)
            {
                float across = Vector3.Dot(point, side);
                xMin = Mathf.Min(xMin, across);
                xMax = Mathf.Max(xMax, across);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            const float Epsilon = 0.001f;
            return inner.xMin >= outer.xMin - Epsilon && inner.xMax <= outer.xMax + Epsilon
                && inner.yMin >= outer.yMin - Epsilon && inner.yMax <= outer.yMax + Epsilon;
        }

        private static string Describe(Rect rect)
        {
            return "[" + rect.xMin.ToString("F2") + ".." + rect.xMax.ToString("F2") + ", y " + rect.yMin.ToString("F2") + ".." + rect.yMax.ToString("F2") + "]";
        }

        private static void ForEachLocale(System.Action<string> body)
        {
            using (new PlayerPrefsKeyScope(L.PrefKey))
            {
                string before = L.Locale;
                try
                {
                    foreach (string locale in new[] { "ja", "en" })
                    {
                        L.SetLocale(locale);
                        Assert.AreEqual(locale, L.Locale, "Resources/KCD/Localization/" + locale + ".json を読めていない");
                        body(locale);
                    }
                }
                finally
                {
                    L.SetLocale(before);
                }
            }
        }

        private static string AssetPathOf(Material material)
        {
            if (material == null)
            {
                return "(なし)";
            }

            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? material.name + "（シーン埋め込み）" : path;
        }
    }
}
