using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CompilerPlatform = UnityEditor.Rendering.ShaderCompilerPlatform;
using MessageSeverity = UnityEditor.Rendering.ShaderCompilerMessageSeverity;
using ProgramType = UnityEditor.Rendering.ShaderType;

namespace KCD.Tests
{
    /// <summary>
    /// 屋外の水面（図書館の堀と池、site_water）を KCD/Water で描くことを守る (#58)。
    ///
    /// 以前の水は URP Lit の半透明で、平らな板に空が映るだけの氷のような見た目だった。
    /// KCD/Water は縁からの距離で色を段に変え、縁石の際に暗い帯と白い線を引き、さざ波を 2 枚ずらして流し、
    /// 空をフレネルで映し、太陽のきらめきを足す。Web の URP 設定は深度テクスチャを切っているので、
    /// 縁からの距離はワールド位置と水盤の矩形（マテリアルの _Basin0〜3）から計算する。
    ///
    /// EditMode テストのアセンブリは KCD.Runtime しか参照していないので、Editor の WaterMaterial は呼べない。
    /// SceneBuilder が作ったマテリアル・テクスチャ・シーンと、ソースの数値を読んで確かめる。
    /// </summary>
    public sealed class WaterMaterialTests
    {
        private const string ShaderName = "KCD/Water";
        private const string WaterMaterialPath = "Assets/Materials/Campus/water.mat";
        private const string RippleTexturePath = "Assets/Generated/Textures/water_ripple.png";
        private const string ScenePath = "Assets/Scenes/Campus.unity";
        private const string WaterObjectName = "site_water";
        private const int RippleSize = 128;

        /// <summary>水面の頂点が水盤の角に乗っているとみなす距離（m）。FBX の書き出しと取り込みの丸めを見込む。</summary>
        private const float CornerTolerance = 0.01f;

        /// <summary>シェーダが受け取る水盤の矩形。WaterMaterial.ShaderBasins が入れる。</summary>
        private static readonly string[] BasinProperties = { "_Basin0", "_Basin1", "_Basin2", "_Basin3" };

        /// <summary>シェーダの既定値へ戻さず、MaterialLibrary と WaterMaterial が決めて入れるプロパティ。</summary>
        private static readonly string[] ComputedProperties =
        {
            "_BaseColor", "_RippleMap", "_CampusAxis", "_Basin0", "_Basin1", "_Basin2", "_Basin3",
        };

        private const string Num = @"(-?\d+(?:\.\d+)?)";

        [Test]
        public void 水のシェーダはエラー無くコンパイルできる()
        {
            Shader shader = LoadShader();
            var material = new Material(shader);
            try
            {
                for (int pass = 0; pass < shader.passCount; pass++)
                {
                    ShaderUtil.CompilePass(material, pass, true);
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
            }

            var errors = new List<string>();
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                if (message.severity == MessageSeverity.Error)
                {
                    errors.Add(message.message + " (" + message.file + ":" + message.line + ", " + message.platform + ")");
                }
            }

            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), ShaderName + " にエラーがある:\n" + string.Join("\n", errors));
            Assert.IsEmpty(errors, ShaderName + " のコンパイルでエラーが出た");
        }

        [Test]
        public void 水のシェーダは_WebGL_2_で使う全ての変種がエラー無くコンパイルできる()
        {
            // テストは -nographics で走るので、描画デバイスに頼らずコンパイラを直に呼ぶ。
            // 変種はシェーダの multi_compile の組み合わせ（インスタンシング 2 x 霧 4 x 主光源の影 4 = 32）。
            Shader shader = LoadShader();
            ShaderData.Pass pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            string[] instancing = { null, "INSTANCING_ON" };
            string[] fog = { null, "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
            string[] shadows = { null, "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" };

            var failures = new List<string>();
            int variants = 0;
            foreach (string i in instancing)
            {
                foreach (string f in fog)
                {
                    foreach (string s in shadows)
                    {
                        string[] keywords = Keywords(i, f, s);
                        // GLES3x（WebGL 2）は頂点の指定で全ての段をまとめてコンパイルする。
                        failures.AddRange(CompileErrors(pass, ProgramType.Vertex, keywords, CompilerPlatform.GLES3x, BuildTarget.WebGL));
                        variants++;
                    }
                }
            }

            // エディタと Windows 版（D3D11）でも、影の変種ごとに頂点とピクセルの両方を通す。
            foreach (string s in shadows)
            {
                string[] keywords = Keywords(null, "FOG_EXP2", s);
                failures.AddRange(CompileErrors(pass, ProgramType.Vertex, keywords, CompilerPlatform.D3D11, BuildTarget.StandaloneWindows64));
                failures.AddRange(CompileErrors(pass, ProgramType.Fragment, keywords, CompilerPlatform.D3D11, BuildTarget.StandaloneWindows64));
            }

            Assert.AreEqual(32, variants, "WebGL 2 の変種の数");
            Assert.IsEmpty(failures, ShaderName + " のコンパイルでエラーが出た:\n" + string.Join("\n", failures));
        }

        [Test]
        public void 水のシェーダは_1_パスで_テクスチャ読みが少なく_SRP_Batcher_に乗る()
        {
            Shader shader = LoadShader();
            Assert.AreEqual(1, shader.passCount, "水面は 1 パスで描く（影や深度のパスを持たない）");
            Assert.AreEqual("UniversalForward", shader.FindPassTagValue(0, new ShaderTagId("LightMode")).name,
                "水面のパスが URP の前方描画で呼ばれない");

            string source = ReadAsset("Shaders", "KCD_Water.shader");
            Assert.IsTrue(Regex.IsMatch(source, @"^\s*Fallback\s+Off\b", RegexOptions.Multiline),
                "Fallback があると Lit から影や深度のパスを借りて、描画のパスが増える");

            // WebGL 2 の中位の画質でも重くならないよう、1 画素あたりのテクスチャ読みを 4 回以下に抑える。
            // さざ波 2 回 + 主光源の影 1 回（硬い影）= 3 回。柔らかい影は 1 回で 4〜9 回読むので入れない。
            Assert.AreEqual(2, Regex.Matches(source, @"SAMPLE_TEXTURE2D\s*\(").Count, "さざ波のテクスチャ読みは 2 回");
            Assert.IsFalse(Regex.IsMatch(source, @"^\s*#pragma[^\n]*_SHADOWS_SOFT", RegexOptions.Multiline),
                "柔らかい影のキーワードを入れるとテクスチャ読みが増える");
            Assert.IsFalse(Regex.IsMatch(source, @"^\s*#pragma[^\n]*_ADDITIONAL_LIGHT", RegexOptions.Multiline),
                "水面は追加ライトを拾わない（変種と計算を増やさない）");

            // SRP Batcher に乗せるには、テクスチャ以外のプロパティを全部 UnityPerMaterial に置く。
            Assert.AreEqual(1, Regex.Matches(source, @"CBUFFER_START\(UnityPerMaterial\)").Count,
                "UnityPerMaterial の CBUFFER が 1 つでない");
            Match cbuffer = Regex.Match(source, @"CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END");
            Assert.IsTrue(cbuffer.Success, "UnityPerMaterial の CBUFFER を読み取れない");
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    continue;
                }

                string name = shader.GetPropertyName(i);
                Assert.IsTrue(Regex.IsMatch(cbuffer.Groups[1].Value, @"\b(?:float|half)[234]?\s+" + Regex.Escape(name) + @"\s*;"),
                    name + " が UnityPerMaterial に無い（SRP Batcher から外れる）");
            }
        }

        [Test]
        public void シェーダの既定値で_縁の暗い帯_白い線_ゆっくり流れるさざ波_空の映り込み_きらめきが出る()
        {
            Shader shader = LoadShader();

            // 色は縁（浅い）から中央（深い）へ段を付けて暗く、濃くする。
            Color shallow = DefaultVector(shader, "_BaseColor");
            Color deep = DefaultVector(shader, "_DeepColor");
            AssertSameRgb(DeclaredWaterColor(), shallow, "既定の浅い色が MaterialLibrary の water の色と違う");
            Assert.Less(Luminance(deep), Luminance(shallow), "中央が縁より明るい");
            float shallowAlpha = DefaultFloat(shader, "_ShallowAlpha");
            float deepAlpha = DefaultFloat(shader, "_DeepAlpha");
            Assert.Greater(shallowAlpha, 0.3f, "縁の水が薄すぎて見えない");
            Assert.Less(shallowAlpha, deepAlpha, "中央のほうが透けている");
            Assert.LessOrEqual(deepAlpha, 1f);
            Assert.GreaterOrEqual(DefaultFloat(shader, "_GradientSteps"), 2f, "段が 1 つだと縁から中央まで同じ色になる");

            // 縁石の際の少し暗い帯。白い線はその内側に収まる。
            float rimShade = DefaultFloat(shader, "_RimShade");
            float rimWidth = DefaultFloat(shader, "_RimShadeWidth");
            Assert.That(rimShade, Is.InRange(0.1f, 0.5f), "縁石の際の帯が見えないか、暗すぎる");
            Assert.That(rimWidth, Is.InRange(0.2f, 1f), "縁石の際の帯の幅");

            Color foam = DefaultVector(shader, "_FoamColor");
            float foamReach = DefaultFloat(shader, "_FoamWidth") * (1f + DefaultFloat(shader, "_FoamWobble"));
            Assert.Greater(Luminance(foam), 0.9f, "縁の線が白くない");
            Assert.Greater(foam.a, 0.5f, "縁の線が薄すぎる");
            Assert.That(DefaultFloat(shader, "_FoamWidth"), Is.InRange(0.03f, 0.3f), "縁の白い線の太さ");
            Assert.Less(foamReach, rimWidth, "揺れた白い線が暗い帯より太い");

            // いちばん細い水盤の中央までに、いちばん深い色へ届く。
            float narrowestHalf = float.PositiveInfinity;
            foreach (Vector4 basin in CampusBasins())
            {
                narrowestHalf = Mathf.Min(narrowestHalf, 0.5f * Mathf.Min(basin.z - basin.x, basin.w - basin.y));
            }

            Assert.LessOrEqual(rimWidth + DefaultFloat(shader, "_DeepDistance"), narrowestHalf,
                "いちばん細い堀の中央でも深い色に届かない");

            // さざ波は大きさの違う 2 枚を、違う向きへゆっくり流す。
            float scaleA = DefaultFloat(shader, "_RippleScaleA");
            float scaleB = DefaultFloat(shader, "_RippleScaleB");
            Assert.Greater(scaleA, 0f);
            Assert.Greater(scaleB, 0f);
            Assert.Greater(Mathf.Abs(Mathf.Log(scaleB / scaleA)), 0.3f, "2 枚のさざ波の大きさがほぼ同じで、模様が重なる");
            Assert.That(DefaultFloat(shader, "_RippleStrengthA"), Is.InRange(0.05f, 0.5f), "さざ波 A の強さ");
            Assert.That(DefaultFloat(shader, "_RippleStrengthB"), Is.InRange(0.05f, 0.5f), "さざ波 B の強さ");
            Vector4 flow = DefaultVector(shader, "_RippleFlow");
            var flowA = new Vector2(flow.x, flow.y);
            var flowB = new Vector2(flow.z, flow.w);
            Assert.That(flowA.magnitude, Is.InRange(0.01f, 0.1f), "さざ波 A の流れ（m/s）が止まっているか速すぎる");
            Assert.That(flowB.magnitude, Is.InRange(0.01f, 0.1f), "さざ波 B の流れ（m/s）が止まっているか速すぎる");
            Assert.Greater(Vector2.Angle(flowA, flowB), 30f, "2 枚が同じ向きに流れると、模様が 1 枚で動いて見える");

            // 空の映り込みは真上から見ると弱く、斜めから見ると強い（フレネル）。
            Assert.That(DefaultFloat(shader, "_FresnelBias"), Is.InRange(0f, 0.2f), "真上から見ても空が強く映る");
            Assert.GreaterOrEqual(DefaultFloat(shader, "_FresnelPower"), 2f, "角度による映り込みの差が小さい");
            Assert.That(DefaultFloat(shader, "_ReflectionStrength"), Is.InRange(0.5f, 1f), "空の映り込みが弱い");

            // 太陽のきらめきは、光の反射の向きに近い細い範囲だけ。
            Assert.That(DefaultFloat(shader, "_SparkleThreshold"), Is.InRange(0.99f, 0.9999f), "きらめきの範囲");
            Assert.Greater(DefaultFloat(shader, "_SparkleIntensity"), 1f, "きらめきが水の色に埋もれる");
            Assert.Greater(DefaultFloat(shader, "_SparkleFadeDistance"), 10f, "きらめきが手前でしか出ない");
            Assert.That(DefaultFloat(shader, "_ShadowDim"), Is.InRange(0.1f, 0.9f), "建物の影が水面に落ちない、または真っ黒");
        }

        [Test]
        public void 水のマテリアルは_KCD_Water_で描き_値はシェーダの既定値にそろう()
        {
            Material material = LoadWaterMaterial();
            Shader shader = LoadShader();
            Assert.AreEqual(ShaderName, material.shader.name, WaterMaterialPath + " が KCD/Water を使っていない（SceneBuilder で作り直す）");
            Assert.AreEqual((int)RenderQueue.Transparent, material.renderQueue, "水面が半透明の順番で描かれない");
            Assert.IsEmpty(material.shaderKeywords, "URP Lit の頃のキーワードが残っている");

            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                if (System.Array.IndexOf(ComputedProperties, name) >= 0)
                {
                    continue;
                }

                string what = name + " がシェーダの既定値と違う";
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        AssertNear(shader.GetPropertyDefaultVectorValue(i), material.GetColor(name), 1e-5f, what);
                        break;
                    case ShaderPropertyType.Vector:
                        AssertNear(shader.GetPropertyDefaultVectorValue(i), material.GetVector(name), 1e-5f, what);
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        Assert.AreEqual(shader.GetPropertyDefaultFloatValue(i), material.GetFloat(name), 1e-6f, what);
                        break;
                    case ShaderPropertyType.Int:
                        Assert.AreEqual(shader.GetPropertyDefaultIntValue(i), material.GetInteger(name), what);
                        break;
                }
            }

            AssertSameRgb(DeclaredWaterColor(), material.GetColor("_BaseColor"), "水の色が MaterialLibrary の water の色と違う");

            Texture ripple = material.GetTexture("_RippleMap");
            Assert.IsNotNull(ripple, "さざ波のテクスチャが貼られていない（水面が平らに見える）");
            Assert.AreEqual(RippleTexturePath, AssetDatabase.GetAssetPath(ripple), "さざ波のテクスチャが違う");

            Vector2 axis = CampusAxisU();
            Vector4 materialAxis = material.GetVector("_CampusAxis");
            Assert.AreEqual(axis.x, materialAxis.x, 1e-5f, "_CampusAxis が CampusProps.AxisU と違う");
            Assert.AreEqual(axis.y, materialAxis.y, 1e-5f, "_CampusAxis が CampusProps.AxisU と違う");
        }

        [Test]
        public void 縁からの距離は水際で_0_になり_矩形の継ぎ目に白い線も暗い帯も出ない()
        {
            Material material = LoadWaterMaterial();
            List<Vector4> basins = CampusBasins();
            var rects = new List<Vector4>();
            foreach (string name in BasinProperties)
            {
                rects.Add(material.GetVector(name));
            }

            // 水盤 i の矩形は、マテリアルの i 枚目に含まれる。空きの枠は面積 0。
            Assert.LessOrEqual(basins.Count, BasinProperties.Length, "水盤の矩形がシェーダの枠より多い");
            for (int i = 0; i < rects.Count; i++)
            {
                if (i < basins.Count)
                {
                    Vector4 b = basins[i];
                    Vector4 r = rects[i];
                    Assert.IsTrue(r.x <= b.x + 1e-4f && r.y <= b.y + 1e-4f && r.z >= b.z - 1e-4f && r.w >= b.w - 1e-4f,
                        BasinProperties[i] + " " + r + " が水盤 " + b + " を含まない");
                }
                else
                {
                    Assert.LessOrEqual(rects[i].z - rects[i].x, 0f, BasinProperties[i] + " は空きの枠のはず");
                }
            }

            // 水盤の中を 10 cm おきに調べ、シェーダの計算した距離を本当の水際までの距離と比べる。
            List<Edge> shore = Shoreline(basins);
            float rimWidth = material.GetFloat("_RimShadeWidth");
            float foamReach = material.GetFloat("_FoamWidth") * (1f + material.GetFloat("_FoamWobble"));
            float uMin = float.PositiveInfinity, vMin = float.PositiveInfinity;
            float uMax = float.NegativeInfinity, vMax = float.NegativeInfinity;
            foreach (Vector4 b in basins)
            {
                uMin = Mathf.Min(uMin, b.x);
                vMin = Mathf.Min(vMin, b.y);
                uMax = Mathf.Max(uMax, b.z);
                vMax = Mathf.Max(vMax, b.w);
            }

            const float step = 0.1f;
            int nu = Mathf.CeilToInt((uMax - uMin) / step);
            int nv = Mathf.CeilToInt((vMax - vMin) / step);
            int samples = 0;
            float worstOver = 0f;
            Vector2 worstOverAt = Vector2.zero;
            float nearestRimAway = float.PositiveInfinity;
            Vector2 nearestRimAwayAt = Vector2.zero;
            float nearestFoamAway = float.PositiveInfinity;
            Vector2 nearestFoamAwayAt = Vector2.zero;
            for (int iu = 0; iu < nu; iu++)
            {
                for (int iv = 0; iv < nv; iv++)
                {
                    var p = new Vector2(uMin + (iu + 0.5f) * step, vMin + (iv + 0.5f) * step);
                    if (!InsideAny(p, basins))
                    {
                        continue;
                    }

                    samples++;
                    float truth = DistanceToShore(p, shore);
                    float estimate = ShaderEdgeDistance(p, rects);
                    if (estimate - truth > worstOver)
                    {
                        worstOver = estimate - truth;
                        worstOverAt = p;
                    }

                    if (truth >= 2f * rimWidth && estimate < nearestRimAway)
                    {
                        nearestRimAway = estimate;
                        nearestRimAwayAt = p;
                    }

                    if (truth >= 2f * foamReach && estimate < nearestFoamAway)
                    {
                        nearestFoamAway = estimate;
                        nearestFoamAwayAt = p;
                    }
                }
            }

            Assert.Greater(samples, 10000, "水盤の中を調べた点が少なすぎる");
            // 大きく見積もると、白い線と暗い帯が水際から離れて水の上に浮く。
            Assert.LessOrEqual(worstOver, 1e-3f, "縁からの距離を本当より " + worstOver + " m 大きく見積もっている (u, v) = " + worstOverAt);
            // 小さく見積もると、水際でない所（矩形の継ぎ目や入り隅の先）に帯と線が出る。
            Assert.Greater(nearestRimAway, rimWidth,
                "水際から " + (2f * rimWidth) + " m 以上離れた (u, v) = " + nearestRimAwayAt + " に縁石の暗い帯が出る");
            Assert.Greater(nearestFoamAway, foamReach,
                "水際から " + (2f * foamReach) + " m 以上離れた (u, v) = " + nearestFoamAwayAt + " に縁の白い線が出る");
        }

        [Test]
        public void さざ波のテクスチャは継ぎ目なく並び_平均すると水面を傾けない()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RippleTexturePath));
            Assert.IsTrue(File.Exists(path), RippleTexturePath + " が無い（SceneBuilder で作り直す）");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), RippleTexturePath + " を読めない");
                Assert.AreEqual(RippleSize, texture.width, "さざ波のテクスチャの幅");
                Assert.AreEqual(RippleSize, texture.height, "さざ波のテクスチャの高さ");
                Color32[] pixels = texture.GetPixels32();

                // RG は傾き。1 枚ぶんの平均が 0（0.5 に詰めた値）なら、並べても水面全体が一方へ傾かない。
                double r = 0.0;
                double g = 0.0;
                foreach (Color32 pixel in pixels)
                {
                    r += pixel.r;
                    g += pixel.g;
                }

                Assert.AreEqual(0.5, r / pixels.Length / 255.0, 0.01, "傾き（R）の平均が 0 でない");
                Assert.AreEqual(0.5, g / pixels.Length / 255.0, 0.01, "傾き（G）の平均が 0 でない");

                // A は高さ。端から端への段が、中の隣り合う画素の段と同じくらいなら、並べても継ぎ目が見えない。
                int n = RippleSize;
                double insideX = 0.0, insideY = 0.0, wrapX = 0.0, wrapY = 0.0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n - 1; x++)
                    {
                        insideX += Mathf.Abs(pixels[y * n + x + 1].a - pixels[y * n + x].a);
                        insideY += Mathf.Abs(pixels[(x + 1) * n + y].a - pixels[x * n + y].a);
                    }

                    wrapX += Mathf.Abs(pixels[y * n].a - pixels[y * n + n - 1].a);
                    wrapY += Mathf.Abs(pixels[y].a - pixels[(n - 1) * n + y].a);
                }

                insideX /= n * (n - 1);
                insideY /= n * (n - 1);
                wrapX /= n;
                wrapY /= n;
                Assert.LessOrEqual(wrapX, insideX * 1.5 + 1.0, "左右の端で高さが飛ぶ（横に並べると継ぎ目が見える）");
                Assert.LessOrEqual(wrapY, insideY * 1.5 + 1.0, "上下の端で高さが飛ぶ（縦に並べると継ぎ目が見える）");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            var importer = AssetImporter.GetAtPath(RippleTexturePath) as TextureImporter;
            Assert.IsNotNull(importer, RippleTexturePath + " が取り込まれていない");
            Assert.IsFalse(importer.sRGBTexture, "傾きと高さの数値に sRGB の変換がかかる");
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, "圧縮すると傾きが段になる");
            Assert.IsTrue(importer.mipmapEnabled, "ミップが無いと遠くのさざ波ときらめきがちらつく");
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapMode, "さざ波を繰り返して貼れない");
        }

        [Test]
        public void キャンパスの水面は_KCD_Water_の_1_枚で描き_影を落とさず_水盤の角に頂点がある()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var waters = new List<MeshRenderer>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (renderer.gameObject.name == WaterObjectName)
                        {
                            waters.Add(renderer);
                        }
                    }
                }

                Assert.AreEqual(1, waters.Count, ScenePath + " の " + WaterObjectName + " が 1 つでない");
                MeshRenderer water = waters[0];
                Assert.AreEqual(1, water.sharedMaterials.Length, "水面のマテリアルが 1 つでない（描画コールが増える）");
                Assert.AreEqual(LoadWaterMaterial(), water.sharedMaterial, "水面に " + WaterMaterialPath + " が貼られていない");
                Assert.AreEqual(ShaderName, water.sharedMaterial.shader.name, "水面が KCD/Water で描かれていない");
                Assert.AreEqual(ShadowCastingMode.Off, water.shadowCastingMode, "水面が影を落とす（影のパスを持たないのに影の描画に数えられる）");

                MeshFilter filter = water.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, WaterObjectName + " に MeshFilter が無い");
                Mesh mesh = filter.sharedMesh;
                Assert.IsNotNull(mesh, WaterObjectName + " のメッシュが無い");

                // シェーダはワールドの xz を _CampusAxis で (u, v) に直し、水盤の矩形と比べる。
                // メッシュの頂点をその式で直すと水盤の角に乗る = シェーダの縁と見た目の縁が一致する。
                Vector2 axis = CampusAxisU();
                List<Vector4> basins = CampusBasins();
                Matrix4x4 toWorld = water.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Assert.Greater(vertices.Length, 0, WaterObjectName + " に頂点が無い");
                var corners = new List<Vector2>();
                foreach (Vector4 b in basins)
                {
                    corners.Add(new Vector2(b.x, b.y));
                    corners.Add(new Vector2(b.z, b.y));
                    corners.Add(new Vector2(b.z, b.w));
                    corners.Add(new Vector2(b.x, b.w));
                }

                var hit = new bool[corners.Count];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(vertices[i]);
                    var campus = new Vector2(
                        world.x * axis.x + world.z * axis.y,
                        -world.x * axis.y + world.z * axis.x);
                    // 2 枚の水盤が角を共有するところは、同じ頂点が両方の角に当たる。
                    float nearestDistance = float.PositiveInfinity;
                    for (int c = 0; c < corners.Count; c++)
                    {
                        float d = Vector2.Distance(campus, corners[c]);
                        nearestDistance = Mathf.Min(nearestDistance, d);
                        if (d < CornerTolerance)
                        {
                            hit[c] = true;
                        }
                    }

                    Assert.Less(nearestDistance, CornerTolerance, "水面の頂点 (u, v) = " + campus + " が水盤の角に乗らない");

                    if (i < normals.Length)
                    {
                        Vector3 up = toWorld.MultiplyVector(normals[i]).normalized;
                        Assert.Greater(up.y, 0.99f, "水面が水平でない（シェーダは上向きの水面として光を計算する）");
                    }
                }

                for (int c = 0; c < corners.Count; c++)
                {
                    Assert.IsTrue(hit[c], "水盤の角 (u, v) = " + corners[c] + " に水面の頂点が無い");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // --- コンパイル ---

        private static string[] Keywords(params string[] candidates)
        {
            var keywords = new List<string>();
            foreach (string keyword in candidates)
            {
                if (!string.IsNullOrEmpty(keyword))
                {
                    keywords.Add(keyword);
                }
            }

            return keywords.ToArray();
        }

        private static List<string> CompileErrors(ShaderData.Pass pass, ProgramType stage, string[] keywords,
            CompilerPlatform platform, BuildTarget target)
        {
            ShaderData.VariantCompileInfo info = pass.CompileVariant(stage, keywords, platform, target);
            string label = platform + " " + stage + " [" + string.Join(" ", keywords) + "]";
            var errors = new List<string>();
            if (info.Messages != null)
            {
                foreach (ShaderMessage message in info.Messages)
                {
                    if (message.severity == MessageSeverity.Error)
                    {
                        errors.Add(label + ": " + message.message + " (" + message.file + ":" + message.line + ")");
                    }
                }
            }

            if (!info.Success && errors.Count == 0)
            {
                errors.Add(label + ": コンパイルに失敗した");
            }

            return errors;
        }

        // --- 読み取り ---

        private static string ReadAsset(params string[] parts)
        {
            string path = Path.Combine(Application.dataPath, Path.Combine(parts));
            Assert.IsTrue(File.Exists(path), path + " が無い");
            return File.ReadAllText(path);
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>CampusStage.cs の「Basins = { new Vector4(u0, v0, u1, v1), ... };」。</summary>
        private static List<Vector4> CampusBasins()
        {
            string cs = ReadAsset("Scripts", "Editor", "CampusStage.cs");
            Match list = Regex.Match(cs, @"Vector4\[\]\s*Basins\s*=\s*\{([\s\S]*?)\};");
            Assert.IsTrue(list.Success, "CampusStage.Basins を読み取れない");
            var rects = new List<Vector4>();
            foreach (Match m in Regex.Matches(list.Groups[1].Value,
                         @"new Vector4\(\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*,\s*" + Num + @"f?\s*\)"))
            {
                rects.Add(new Vector4(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value)));
            }

            Assert.Greater(rects.Count, 0, "水盤の矩形が 1 つも読めない");
            return rects;
        }

        /// <summary>CampusProps.cs の「AxisU = new Vector2(x, z)」。キャンパスの u 軸のワールドの向き。</summary>
        private static Vector2 CampusAxisU()
        {
            string cs = ReadAsset("Scripts", "Editor", "CampusProps.cs");
            Match m = Regex.Match(cs, @"AxisU\s*=\s*new Vector2\(\s*" + Num + @"f\s*,\s*" + Num + @"f\s*\)");
            Assert.IsTrue(m.Success, "CampusProps.AxisU を読み取れない");
            return new Vector2(F(m.Groups[1].Value), F(m.Groups[2].Value));
        }

        /// <summary>MaterialLibrary.cs の CampusColors にある water の色。</summary>
        private static Color DeclaredWaterColor()
        {
            string cs = ReadAsset("Scripts", "Editor", "MaterialLibrary.cs");
            Match m = Regex.Match(cs, @"\{\s*""water""\s*,\s*""([0-9A-Fa-f]{6})""\s*\}");
            Assert.IsTrue(m.Success, "MaterialLibrary.CampusColors の water を読み取れない");
            Assert.IsTrue(ColorUtility.TryParseHtmlString("#" + m.Groups[1].Value, out Color color), "water の色を読めない");
            return color;
        }

        private static Shader LoadShader()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, ShaderName + " が見つからない（Assets/Shaders/KCD_Water.shader）");
            return shader;
        }

        private static Material LoadWaterMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            Assert.IsNotNull(material, WaterMaterialPath + " が無い（SceneBuilder で作り直す）");
            return material;
        }

        private static int PropertyIndex(Shader shader, string name)
        {
            int index = shader.FindPropertyIndex(name);
            Assert.GreaterOrEqual(index, 0, ShaderName + " に " + name + " が無い");
            return index;
        }

        private static float DefaultFloat(Shader shader, string name)
        {
            return shader.GetPropertyDefaultFloatValue(PropertyIndex(shader, name));
        }

        private static Vector4 DefaultVector(Shader shader, string name)
        {
            return shader.GetPropertyDefaultVectorValue(PropertyIndex(shader, name));
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>8 bit に戻したとき同じ色か（MaterialLibrary.Same と同じ幅）。</summary>
        private static void AssertSameRgb(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.002f, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, 0.002f, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, 0.002f, what + " (b)");
        }

        private static void AssertNear(Vector4 expected, Vector4 actual, float tolerance, string what)
        {
            for (int k = 0; k < 4; k++)
            {
                Assert.AreEqual(expected[k], actual[k], tolerance, what + " [" + k + "] 期待 " + expected + " 実際 " + actual);
            }
        }

        // --- 縁からの距離 ---

        /// <summary>シェーダの EdgeDistance と同じ計算。矩形ごとの内側の距離の最大（0 未満は 0）。</summary>
        private static float ShaderEdgeDistance(Vector2 p, List<Vector4> rects)
        {
            float d = float.NegativeInfinity;
            foreach (Vector4 r in rects)
            {
                float inside = Mathf.Min(Mathf.Min(p.x - r.x, r.z - p.x), Mathf.Min(p.y - r.y, r.w - p.y));
                d = Mathf.Max(d, inside);
            }

            return Mathf.Max(d, 0f);
        }

        private static bool InsideAny(Vector2 p, List<Vector4> rects)
        {
            foreach (Vector4 r in rects)
            {
                if (p.x > r.x && p.x < r.z && p.y > r.y && p.y < r.w)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>水際の 1 本の線分。ConstantU なら u = At の上を v = From〜To、そうでなければ v = At の上を u = From〜To。</summary>
        private readonly struct Edge
        {
            public readonly bool ConstantU;
            public readonly float At;
            public readonly float From;
            public readonly float To;

            public Edge(bool constantU, float at, float from, float to)
            {
                ConstantU = constantU;
                At = at;
                From = from;
                To = to;
            }
        }

        /// <summary>水盤を合わせた形の水際。矩形の辺から、となりの矩形へ水が続いている部分を除いたもの。</summary>
        private static List<Edge> Shoreline(List<Vector4> basins)
        {
            var edges = new List<Edge>();
            for (int i = 0; i < basins.Count; i++)
            {
                Vector4 r = basins[i];
                AddExposed(edges, basins, i, true, r.z, r.y, r.w, 1f);
                AddExposed(edges, basins, i, true, r.x, r.y, r.w, -1f);
                AddExposed(edges, basins, i, false, r.w, r.x, r.z, 1f);
                AddExposed(edges, basins, i, false, r.y, r.x, r.z, -1f);
            }

            return edges;
        }

        private static void AddExposed(List<Edge> edges, List<Vector4> basins, int self, bool constantU,
            float at, float from, float to, float outward)
        {
            // 辺のすぐ外側に別の水盤があれば、そこは水際ではない。
            float probe = at + outward * 1e-3f;
            var pieces = new List<Vector2> { new Vector2(from, to) };
            for (int j = 0; j < basins.Count; j++)
            {
                if (j == self)
                {
                    continue;
                }

                Vector4 o = basins[j];
                bool across = constantU ? (o.x < probe && probe < o.z) : (o.y < probe && probe < o.w);
                if (!across)
                {
                    continue;
                }

                float c0 = constantU ? o.y : o.x;
                float c1 = constantU ? o.w : o.z;
                var next = new List<Vector2>();
                foreach (Vector2 piece in pieces)
                {
                    if (c1 <= piece.x || c0 >= piece.y)
                    {
                        next.Add(piece);
                        continue;
                    }

                    if (piece.x < c0)
                    {
                        next.Add(new Vector2(piece.x, c0));
                    }

                    if (c1 < piece.y)
                    {
                        next.Add(new Vector2(c1, piece.y));
                    }
                }

                pieces = next;
            }

            foreach (Vector2 piece in pieces)
            {
                if (piece.y - piece.x > 1e-4f)
                {
                    edges.Add(new Edge(constantU, at, piece.x, piece.y));
                }
            }
        }

        private static float DistanceToShore(Vector2 p, List<Edge> shore)
        {
            float best = float.PositiveInfinity;
            foreach (Edge e in shore)
            {
                Vector2 nearest = e.ConstantU
                    ? new Vector2(e.At, Mathf.Clamp(p.y, e.From, e.To))
                    : new Vector2(Mathf.Clamp(p.x, e.From, e.To), e.At);
                best = Mathf.Min(best, Vector2.Distance(p, nearest));
            }

            return best;
        }
    }
}
