using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KCD.Tests
{
    /// <summary>
    /// キャラの .mat と FBX が、ToonLook の役割と KCD_Toon の前提に合っていることを守る (#14, #44)。
    ///
    /// - .mat の名前から決まる役割（顔・肌・髪・目・顔の線）が全キャラに揃っている。
    /// - .mat のトゥーンの値が役割どおり（MaterialLibrary.RestyleCharacters が塗った状態）。
    /// - palette.json の色に陰の係数を掛けると、肌は赤みの陰、服・髪・金属は元の色相の陰になる。
    /// - 輪郭の太さが #44 の表（ゲームの画角 55°・1080p）に入る。
    /// - FBX の顔の頂点に、CharacterImporter が頭の向きと左右の位置を焼いている。
    ///
    /// .mat の値は SceneBuilder.BuildAll（MaterialLibrary.RepaintCharacters）で、
    /// FBX の顔の向きは取り込み直し（CharacterImporter の GetVersion を上げると走る）で入る。
    /// どちらかを忘れるとここが落ちる。
    /// </summary>
    public sealed class CharacterToonAgreementTests
    {
        private const string CharactersAssetFolder = "Assets/Models/Characters";
        private const string CharacterMaterialsFolder = "Assets/Materials/Characters";

        /// <summary>ゲームのカメラの縦の画角（ActorFactory / ScenePreview.DefaultFov）。</summary>
        private const float GameFov = 55f;

        [System.Serializable]
        private sealed class Entry
        {
            public string name;
            public string hex;
            public string pattern;
            public float pattern_scale;
        }

        [System.Serializable]
        private sealed class PaletteFile
        {
            public Entry[] materials;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineNearDistanceId = Shader.PropertyToID("_OutlineNearDistance");
        private static readonly int OutlineFadeStartId = Shader.PropertyToID("_OutlineFadeStart");
        private static readonly int OutlineFadeEndId = Shader.PropertyToID("_OutlineFadeEnd");
        private static readonly int OutlineMaxPixelsId = Shader.PropertyToID("_OutlineMaxPixels");

        private static string[] CharacterIds()
        {
            return AssetDatabase.GetSubFolders(CharactersAssetFolder).Select(folder => Path.GetFileName(folder)).ToArray();
        }

        /// <summary>MaterialLibrary.Normalize と同じ（palette.json の名前の揺れを吸収する）。</summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "default";
            }

            string key = name.Trim().ToLowerInvariant();
            int dot = key.IndexOf('.');
            return dot > 0 ? key.Substring(0, dot) : key;
        }

        /// <summary>MaterialLibrary.LoadCharacterPalette と同じ {マテリアル名: 色}。</summary>
        private static Dictionary<string, Color> LoadPalette(string characterId)
        {
            var palette = new Dictionary<string, Color>();
            string path = Path.Combine(Application.dataPath, "Models", "Characters", characterId, "palette.json");
            if (!File.Exists(path))
            {
                return palette;
            }

            PaletteFile file = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(path));
            foreach (Entry entry in file?.materials ?? new Entry[0])
            {
                if (!string.IsNullOrEmpty(entry.name) && ColorUtility.TryParseHtmlString("#" + entry.hex, out Color color))
                {
                    palette[Normalize(entry.name)] = color;
                }
            }

            return palette;
        }

        /// <summary>キャラの .mat（パス、キャラ id、Blender 側の名前）。id は MaterialLibrary と同じく一番長く一致するフォルダ名。</summary>
        private static List<(string Path, string CharacterId, string Name)> CharacterMaterials()
        {
            string[] ids = CharacterIds();
            var result = new List<(string, string, string)>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { CharacterMaterialsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                string characterId = null;
                foreach (string id in ids)
                {
                    if (file.StartsWith(id + "_", System.StringComparison.Ordinal)
                        && (characterId == null || id.Length > characterId.Length))
                    {
                        characterId = id;
                    }
                }

                if (characterId != null)
                {
                    result.Add((path, characterId, file.Substring(characterId.Length + 1)));
                }
            }

            return result;
        }

        [Test]
        public void 全キャラに顔と肌と髪と目と顔の線のマテリアルがある()
        {
            string[] ids = CharacterIds();
            Assert.IsNotEmpty(ids, CharactersAssetFolder + " にキャラが無い");
            List<(string Path, string CharacterId, string Name)> materials = CharacterMaterials();
            var failures = new List<string>();
            foreach (string id in ids)
            {
                var counts = new Dictionary<ToonLook.Role, int>();
                foreach ((string Path, string CharacterId, string Name) material in materials)
                {
                    if (material.CharacterId != id)
                    {
                        continue;
                    }

                    ToonLook.Role role = ToonLook.RoleOf(material.Name);
                    counts[role] = counts.TryGetValue(role, out int n) ? n + 1 : 1;
                }

                int Count(ToonLook.Role role) => counts.TryGetValue(role, out int n) ? n : 0;
                if (Count(ToonLook.Role.Face) != 1)
                {
                    failures.Add(id + ": 顔（face）が " + Count(ToonLook.Role.Face) + " 枚（1 枚のはず）");
                }

                if (Count(ToonLook.Role.Skin) < 1)
                {
                    failures.Add(id + ": 肌（skin）が無い");
                }

                if (Count(ToonLook.Role.Hair) < 1)
                {
                    failures.Add(id + ": 髪（hair）が無い");
                }

                if (Count(ToonLook.Role.Eye) != 3)
                {
                    failures.Add(id + ": 目（eye_white, eye_l, eye_r）が " + Count(ToonLook.Role.Eye) + " 枚");
                }

                if (Count(ToonLook.Role.Line) < 2)
                {
                    failures.Add(id + ": 顔の線（lash, eye_rim, brow）が " + Count(ToonLook.Role.Line) + " 枚");
                }

                if (Count(ToonLook.Role.Outline) > 1)
                {
                    failures.Add(id + ": 輪郭（outline）が " + Count(ToonLook.Role.Outline) + " 枚");
                }
            }

            Assert.IsEmpty(failures, "Blender 側の名前が ToonLook.RoleOf の役割に当たらない:\n" + string.Join("\n", failures));
        }

        [Test]
        public void マテリアルのトゥーンの値が役割どおり()
        {
            var palettes = new Dictionary<string, Dictionary<string, Color>>();
            var failures = new List<string>();
            int checkedCount = 0;
            foreach ((string path, string characterId, string name) in CharacterMaterials())
            {
                var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (asset == null || asset.shader == null || asset.shader.name != ToonLook.ShaderName
                    || !ToonLook.LookOf(ToonLook.RoleOf(name)).Managed || !asset.HasProperty(BaseColorId))
                {
                    continue;
                }

                if (!palettes.TryGetValue(characterId, out Dictionary<string, Color> palette))
                {
                    palette = LoadPalette(characterId);
                    palettes[characterId] = palette;
                }

                Color color = palette.TryGetValue(name, out Color declared) ? declared : asset.GetColor(BaseColorId);
                var copy = new Material(asset);
                try
                {
                    // 役割どおりなら Apply は何も変えない。
                    if (ToonLook.Apply(copy, name, color))
                    {
                        failures.Add(path);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }

                checkedCount++;
            }

            Assert.Greater(checkedCount, 50, "確かめた KCD/Toon のキャラのマテリアルが少ない: " + checkedCount);
            Assert.IsEmpty(
                failures,
                "トゥーンの設定が役割と違う。SceneBuilder.BuildAll で塗り直す（" + failures.Count + " 件）:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// プロジェクトはリニア。シェーダは albedo と陰の係数をリニアで掛けるので、同じ掛け方で見た目の色を出す。
        /// </summary>
        private static Color Shaded(Color albedo, Color shade)
        {
            return (albedo.linear * shade.linear).gamma;
        }

        private static IEnumerable<(string CharacterId, string Name, Color Color)> PaletteColors()
        {
            foreach (string id in CharacterIds())
            {
                foreach (KeyValuePair<string, Color> pair in LoadPalette(id))
                {
                    yield return (id, pair.Key, pair.Value);
                }
            }
        }

        [Test]
        public void 肌の陰は赤みを増して暗くなる()
        {
            var failures = new List<string>();
            int skins = 0;
            foreach ((string id, string name, Color color) in PaletteColors())
            {
                if (ToonLook.RoleOf(name) != ToonLook.Role.Skin)
                {
                    continue;
                }

                skins++;
                Color.RGBToHSV(color, out float h0, out float s0, out float v0);
                Color.RGBToHSV(Shaded(color, ToonLook.SkinShade), out float h1, out float s1, out float v1);
                Color.RGBToHSV(Shaded(color, ToonLook.SkinShade2), out float h2, out float s2, out float v2);
                h0 *= 360f;
                h1 *= 360f;
                h2 *= 360f;
                bool redder = 0f < h2 && h2 < h1 && h1 < h0 && h0 < 60f;
                bool deeper = s1 > s0 && s2 > s0;
                bool darker = v2 < v1 && v1 < v0;
                if (!redder || !deeper || !darker)
                {
                    failures.Add(id + "/" + name + ": 色相 " + h0 + " → " + h1 + " → " + h2
                        + " / 彩度 " + s0 + " → " + s1 + " → " + s2 + " / 明度 " + v0 + " → " + v1 + " → " + v2);
                }
            }

            Assert.Greater(skins, 0, "palette.json に肌（skin）が無い");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// 陰の係数は sRGB の色相で作るので、リニアで掛けると色相が少し動く。15° までに収まる
        /// （今の色で一番動くのは mirai の cloth_ribbon_green 00843D の 13.3°）。
        /// </summary>
        [Test]
        public void 服と髪と金属の陰は元の色相のまま()
        {
            var failures = new List<string>();
            int checkedCount = 0;
            foreach ((string id, string name, Color color) in PaletteColors())
            {
                ToonLook.Role role = ToonLook.RoleOf(name);
                if (role != ToonLook.Role.Cloth && role != ToonLook.Role.Hair && role != ToonLook.Role.Metal)
                {
                    continue;
                }

                Color.RGBToHSV(color, out float hue, out float saturation, out float _);
                if (saturation < 0.2f)
                {
                    continue;
                }

                checkedCount++;
                foreach (Color shade in new[] { ToonLook.ClothShade(color), ToonLook.ClothShade2(color) })
                {
                    Color.RGBToHSV(Shaded(color, shade), out float shadedHue, out float _, out float _);
                    float distance = Mathf.Abs(Mathf.DeltaAngle(hue * 360f, shadedHue * 360f));
                    if (distance > 15f)
                    {
                        failures.Add(id + "/" + name + " #" + ColorUtility.ToHtmlStringRGB(color) + ": 陰で色相が " + distance + "° 動く");
                    }
                }
            }

            Assert.Greater(checkedCount, 0, "palette.json に彩度のある服・髪・金属が無い");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 輪郭の太さが画角55度と1080pで44番の表に入る()
        {
            var failures = new List<string>();
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null || material.shader.name != ToonLook.ShaderName
                    || material.GetFloat(OutlineWidthId) <= 0f)
                {
                    continue;
                }

                checkedCount++;
                float width = material.GetFloat(OutlineWidthId);
                float near = material.GetFloat(OutlineNearDistanceId);
                float fadeStart = material.GetFloat(OutlineFadeStartId);
                float fadeEnd = material.GetFloat(OutlineFadeEndId);
                float maxPixels = material.GetFloat(OutlineMaxPixelsId);
                float Pixels(float distance) =>
                    ToonLook.OutlinePixels(distance, width, near, fadeStart, fadeEnd, maxPixels, GameFov);

                var problems = new List<string>();
                if (Pixels(3f) < 4.5f || Pixels(3f) > 6f)
                {
                    problems.Add("3 m で " + Pixels(3f) + " px（4.5〜6）");
                }

                if (Pixels(5f) < 4.5f || Pixels(5f) > 6f)
                {
                    problems.Add("5 m で " + Pixels(5f) + " px（4.5〜6）");
                }

                if (Pixels(10f) < 2f || Pixels(10f) > 3f)
                {
                    problems.Add("10 m で " + Pixels(10f) + " px（2〜3）");
                }

                if (Pixels(20f) < 0.8f || Pixels(20f) > 1.6f)
                {
                    problems.Add("20 m で " + Pixels(20f) + " px（0.8〜1.6）");
                }

                if (Pixels(30f) >= 1f || Pixels(30f) >= Pixels(20f))
                {
                    problems.Add("30 m で " + Pixels(30f) + " px（1 未満で 20 m より細い）");
                }

                if (Pixels(45f) >= 0.5f)
                {
                    problems.Add("45 m で " + Pixels(45f) + " px（0.5 未満）");
                }

                if (Pixels(60f) != 0f)
                {
                    problems.Add("60 m で " + Pixels(60f) + " px（消える）");
                }

                for (int distance = 6; distance <= 60; distance++)
                {
                    if (Pixels(distance) > Pixels(distance - 1) + 1e-5f)
                    {
                        problems.Add(distance + " m で " + (distance - 1) + " m より太い");
                        break;
                    }
                }

                if (problems.Count > 0)
                {
                    failures.Add(path + ": " + string.Join(" / ", problems));
                }
            }

            Assert.Greater(checkedCount, 0, "輪郭のある KCD/Toon のマテリアルが無い");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static GameObject LoadModel(string characterId)
        {
            string path = CharactersAssetFolder + "/" + characterId + "/" + characterId + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(model, path + " が無い");
            return model;
        }

        /// <summary>輪郭のハル以外の体のメッシュ。</summary>
        private static IEnumerable<SkinnedMeshRenderer> Bodies(GameObject model)
        {
            return model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && !r.name.EndsWith("_outline", System.StringComparison.Ordinal));
        }

        /// <summary>役割が role のサブメッシュの頂点（CharacterImporter.BakeFaceFrame と同じ選び方）。</summary>
        private static HashSet<int> VerticesOf(SkinnedMeshRenderer renderer, string characterId, ToonLook.Role role)
        {
            Mesh mesh = renderer.sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            var vertices = new HashSet<int>();
            for (int i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
            {
                if (materials[i] != null && ToonLook.RoleOf(ToonLook.MaterialName(materials[i].name, characterId)) == role)
                {
                    vertices.UnionWith(mesh.GetIndices(i));
                }
            }

            return vertices;
        }

        private static Matrix4x4 ToRoot(GameObject model, SkinnedMeshRenderer renderer)
        {
            return model.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        }

        [Test]
        public void FBXの顔の頂点に頭の向きと左右の位置が焼いてある()
        {
            var failures = new List<string>();
            foreach (string id in CharacterIds())
            {
                GameObject model = LoadModel(id);
                int faceRenderers = 0;
                float maxSide = float.NegativeInfinity;
                float minSide = float.PositiveInfinity;
                foreach (SkinnedMeshRenderer renderer in Bodies(model))
                {
                    HashSet<int> face = VerticesOf(renderer, id, ToonLook.Role.Face);
                    if (face.Count == 0)
                    {
                        continue;
                    }

                    faceRenderers++;
                    Matrix4x4 toRoot = ToRoot(model, renderer);
                    Vector3[] vertices = renderer.sharedMesh.vertices;
                    Vector4[] tangents = renderer.sharedMesh.tangents;
                    if (tangents.Length != vertices.Length)
                    {
                        failures.Add(id + "/" + renderer.name + ": 接線が無い");
                        continue;
                    }

                    float minX = face.Min(i => toRoot.MultiplyPoint3x4(vertices[i]).x);
                    float maxX = face.Max(i => toRoot.MultiplyPoint3x4(vertices[i]).x);
                    float center = 0.5f * (minX + maxX);
                    float halfWidth = 0.5f * (maxX - minX);
                    int wrongSide = 0;
                    int wrongForward = 0;
                    foreach (int index in face)
                    {
                        Vector4 tangent = tangents[index];
                        float expected = ToonLook.FaceSide(toRoot.MultiplyPoint3x4(vertices[index]).x, center, halfWidth);
                        if (Mathf.Abs(tangent.w - expected) > 1e-3f || Mathf.Abs(tangent.w) >= ToonLook.FaceFrameMarker)
                        {
                            wrongSide++;
                        }

                        Vector3 forward = toRoot.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                        if (Vector3.Dot(forward, Vector3.forward) <= 0.999f)
                        {
                            wrongForward++;
                        }

                        maxSide = Mathf.Max(maxSide, tangent.w);
                        minSide = Mathf.Min(minSide, tangent.w);
                    }

                    if (wrongSide > 0)
                    {
                        failures.Add(id + "/" + renderer.name + ": 左右の位置が違う頂点が " + wrongSide + " / " + face.Count);
                    }

                    if (wrongForward > 0)
                    {
                        failures.Add(id + "/" + renderer.name + ": 頭の向きが +Z でない頂点が " + wrongForward + " / " + face.Count);
                    }
                }

                if (faceRenderers == 0)
                {
                    failures.Add(id + ": 顔（face）のサブメッシュが無い");
                    continue;
                }

                if (Mathf.Abs(maxSide - ToonLook.FaceEdgeSine) > 1e-3f || Mathf.Abs(minSide + ToonLook.FaceEdgeSine) > 1e-3f)
                {
                    failures.Add(id + ": 顔の縁の左右の位置が " + minSide + " 〜 " + maxSide + "（±" + ToonLook.FaceEdgeSine + "）");
                }
            }

            Assert.IsEmpty(
                failures,
                "FBX を取り込み直す（CharacterImporter.BakeFaceFrame）:\n" + string.Join("\n", failures));
        }

        [Test]
        public void 表情のブレンドシェイプは顔の頂点の接線を動かさない()
        {
            var failures = new List<string>();
            foreach (string id in CharacterIds())
            {
                GameObject model = LoadModel(id);
                foreach (SkinnedMeshRenderer renderer in Bodies(model))
                {
                    HashSet<int> face = VerticesOf(renderer, id, ToonLook.Role.Face);
                    Mesh mesh = renderer.sharedMesh;
                    if (face.Count == 0 || mesh.blendShapeCount == 0)
                    {
                        continue;
                    }

                    int vertexCount = mesh.vertexCount;
                    var deltaVertices = new Vector3[vertexCount];
                    var deltaNormals = new Vector3[vertexCount];
                    var deltaTangents = new Vector3[vertexCount];
                    for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                    {
                        for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                        {
                            mesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                            int moved = face.Count(i => deltaTangents[i].sqrMagnitude > 0f);
                            if (moved > 0)
                            {
                                failures.Add(id + "/" + mesh.GetBlendShapeName(shape) + ": 顔の頂点 " + moved + " 個の接線が動く");
                            }
                        }
                    }
                }
            }

            Assert.IsEmpty(
                failures,
                "FBX を取り込み直す（CharacterImporter.ClearFaceTangentDeltas）:\n" + string.Join("\n", failures));
        }

        /// <summary>顔の陰は「キャラは根元の +Z を向く」前提で頭の向きを焼く。顔が髪より前にあることで確かめる。</summary>
        [Test]
        public void キャラは根元の空間でZの正の向きを向く()
        {
            var failures = new List<string>();
            foreach (string id in CharacterIds())
            {
                GameObject model = LoadModel(id);
                var face = new List<float>();
                var hair = new List<float>();
                foreach (SkinnedMeshRenderer renderer in Bodies(model))
                {
                    Matrix4x4 toRoot = ToRoot(model, renderer);
                    Vector3[] vertices = renderer.sharedMesh.vertices;
                    face.AddRange(VerticesOf(renderer, id, ToonLook.Role.Face).Select(i => toRoot.MultiplyPoint3x4(vertices[i]).z));
                    hair.AddRange(VerticesOf(renderer, id, ToonLook.Role.Hair).Select(i => toRoot.MultiplyPoint3x4(vertices[i]).z));
                }

                if (face.Count == 0 || hair.Count == 0)
                {
                    failures.Add(id + ": 顔か髪のサブメッシュが無い（顔 " + face.Count + " / 髪 " + hair.Count + "）");
                    continue;
                }

                if (face.Average() <= hair.Average())
                {
                    failures.Add(id + ": 顔の z の平均 " + face.Average() + " が髪の " + hair.Average() + " より前にない");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
