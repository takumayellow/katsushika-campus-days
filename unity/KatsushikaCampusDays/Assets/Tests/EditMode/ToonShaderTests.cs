using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace KCD.Tests
{
    /// <summary>
    /// KCD/Toon シェーダがエラーなくコンパイルでき、SRP Batcher に乗る形を保っていることを守る (#14, #44)。
    ///
    /// Web（Mobile_RPAsset）は Forward で WebGL2、Windows（PC_RPAsset）は Forward+ で D3D。
    /// 追加ライト・ライトレイヤー・顔の陰の組み合わせを、それぞれの RP アセットが有効にするキーワードで実際にコンパイルする。
    /// テストは -nographics で走るので、SRP Batcher の判定はエディタの内部 API ではなく、
    /// ① 全プロパティが HLSLINCLUDE の CBUFFER(UnityPerMaterial) にあること（ソースの文字列）と
    /// ② D3D でコンパイルした各パス・各段の UnityPerMaterial の大きさが揃うこと、の 2 つで見る。
    /// </summary>
    public sealed class ToonShaderTests
    {
        private const string ShaderPath = "Assets/Shaders/KCD_Toon.shader";
        private const string PerMaterial = "UnityPerMaterial";

        private sealed class Target
        {
            public string Label;
            public ShaderCompilerPlatform Platform;
            public BuildTarget Build;
            public ShaderType[] Stages;
        }

        /// <summary>WebGL2。GLES 系は頂点と断片を 1 つのプログラムにまとめて返すので、Vertex だけ頼む。</summary>
        private static readonly Target Web = new Target
        {
            Label = "Web(GLES3x)",
            Platform = ShaderCompilerPlatform.GLES3x,
            Build = BuildTarget.WebGL,
            Stages = new[] { ShaderType.Vertex },
        };

        private static readonly Target Windows = new Target
        {
            Label = "Windows(D3D)",
            Platform = ShaderCompilerPlatform.D3D,
            Build = BuildTarget.StandaloneWindows64,
            Stages = new[] { ShaderType.Vertex, ShaderType.Fragment },
        };

        private sealed class Variant
        {
            public Target Target;
            public string Pass;
            public string[] Keywords;
        }

        private static Variant V(Target target, string pass, params string[] keywords)
        {
            return new Variant { Target = target, Pass = pass, Keywords = keywords };
        }

        /// <summary>
        /// コンパイルする組み合わせ。本体は RP アセットの設定に合わせる。
        /// Mobile_RPAsset: 平行光源の影 1 段（_MAIN_LIGHT_SHADOWS）、ソフトシャドウと追加ライトの影なし、Forward で 1 オブジェクト 4 灯。
        /// PC_RPAsset: 影 4 段（_MAIN_LIGHT_SHADOWS_CASCADE）、ソフトシャドウ、追加ライトの影、Forward+（_CLUSTER_LIGHT_LOOP）。
        /// どちらもライトレイヤー・クッキー・反射プローブの混合を有効にしている。
        /// </summary>
        private static readonly Variant[] Variants =
        {
            V(Web, "ToonForward", "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_LIGHT_LAYERS", "_LIGHT_COOKIES",
                "_REFLECTION_PROBE_BLENDING", "_FACE_SHADOW_ON"),
            V(Web, "ToonForward", "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_LIGHT_LAYERS", "_FACE_SHADOW_ON",
                "_PATTERN_ON", "_VERTEXCOLOR_ON", "_ALPHATEST_ON"),
            V(Web, "ToonForward"),
            V(Windows, "ToonForward", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_CLUSTER_LIGHT_LOOP",
                "_ADDITIONAL_LIGHT_SHADOWS", "_LIGHT_LAYERS", "_LIGHT_COOKIES", "_REFLECTION_PROBE_BLENDING", "_FACE_SHADOW_ON"),
            V(Windows, "ToonForward", "_MAIN_LIGHT_SHADOWS_CASCADE", "_CLUSTER_LIGHT_LOOP", "_FACE_SHADOW_ON",
                "_PATTERN_ON", "_VERTEXCOLOR_ON", "_ALPHATEST_ON"),
            V(Windows, "ToonForward", "_ADDITIONAL_LIGHTS", "_FACE_SHADOW_ON"),
            V(Windows, "ToonForward"),
            V(Web, "Outline"),
            V(Windows, "Outline"),
            V(Web, "ShadowCaster"),
            V(Web, "ShadowCaster", "_CASTING_PUNCTUAL_LIGHT_SHADOW"),
            V(Web, "ShadowCaster", "_ALPHATEST_ON"),
            V(Windows, "ShadowCaster"),
            V(Windows, "ShadowCaster", "_CASTING_PUNCTUAL_LIGHT_SHADOW"),
            V(Windows, "ShadowCaster", "_ALPHATEST_ON"),
            V(Web, "DepthOnly"),
            V(Web, "DepthOnly", "_ALPHATEST_ON"),
            V(Windows, "DepthOnly"),
            V(Windows, "DepthOnly", "_ALPHATEST_ON"),
            V(Web, "DepthNormals"),
            V(Web, "DepthNormals", "_ALPHATEST_ON"),
            V(Windows, "DepthNormals"),
            V(Windows, "DepthNormals", "_ALPHATEST_ON"),
        };

        /// <summary>1 回のコンパイルに数秒かかるので、テストをまたいで結果を使い回す。</summary>
        private static readonly Dictionary<string, ShaderData.VariantCompileInfo> Compiled =
            new Dictionary<string, ShaderData.VariantCompileInfo>();

        /// <summary>変更前からあったプロパティ。名前を変えると既存の .mat の値が黙って既定値に戻る。</summary>
        private static readonly (string Name, ShaderPropertyType Type)[] OriginalProperties =
        {
            ("_BaseMap", ShaderPropertyType.Texture),
            ("_BaseColor", ShaderPropertyType.Color),
            ("_ShadeColor", ShaderPropertyType.Color),
            ("_ShadeThreshold", ShaderPropertyType.Range),
            ("_ShadeSoftness", ShaderPropertyType.Range),
            ("_ShadeThreshold2", ShaderPropertyType.Range),
            ("_ShadeColor2", ShaderPropertyType.Color),
            ("_RimColor", ShaderPropertyType.Color),
            ("_RimPower", ShaderPropertyType.Range),
            ("_RimIntensity", ShaderPropertyType.Range),
            ("_SpecularColor", ShaderPropertyType.Color),
            ("_SpecularPower", ShaderPropertyType.Range),
            ("_SpecularIntensity", ShaderPropertyType.Range),
            ("_OutlineColor", ShaderPropertyType.Color),
            ("_OutlineWidth", ShaderPropertyType.Range),
            ("_OutlineNearDistance", ShaderPropertyType.Range),
            ("_OutlineFadeStart", ShaderPropertyType.Range),
            ("_OutlineFadeEnd", ShaderPropertyType.Range),
            ("_EmissionColor", ShaderPropertyType.Color),
            ("_Cutoff", ShaderPropertyType.Range),
            ("_Pattern", ShaderPropertyType.Float),
            ("_PatternMap", ShaderPropertyType.Texture),
            ("_PatternScale", ShaderPropertyType.Float),
            ("_PatternBlend", ShaderPropertyType.Range),
            ("_VertexColor", ShaderPropertyType.Float),
            ("_Surface", ShaderPropertyType.Float),
            ("_SrcBlend", ShaderPropertyType.Float),
            ("_DstBlend", ShaderPropertyType.Float),
            ("_ZWrite", ShaderPropertyType.Float),
            ("_Cull", ShaderPropertyType.Float),
        };

        /// <summary>#14 / #44 で足したプロパティ。ToonLook と MaterialLibrary がこの名前で書き込む。</summary>
        private static readonly (string Name, ShaderPropertyType Type)[] ToonProperties =
        {
            ("_ShadeByNdotL", ShaderPropertyType.Float),
            ("_ShadeSoftness2", ShaderPropertyType.Range),
            ("_RimBacklight", ShaderPropertyType.Range),
            ("_RimLightAlign", ShaderPropertyType.Range),
            ("_FaceShadow", ShaderPropertyType.Float),
            ("_FaceShadowSoftness", ShaderPropertyType.Range),
            ("_FaceShadowBias", ShaderPropertyType.Range),
            ("_HairHighlightColor", ShaderPropertyType.Color),
            ("_HairHighlightIntensity", ShaderPropertyType.Range),
            ("_HairHighlightHeight", ShaderPropertyType.Range),
            ("_HairHighlightWidth", ShaderPropertyType.Range),
            ("_HairHighlightSoftness", ShaderPropertyType.Range),
            ("_HairHighlightJag", ShaderPropertyType.Range),
            ("_OutlineMaxPixels", ShaderPropertyType.Range),
        };

        /// <summary>
        /// 足したプロパティの既定値。キャラ以外（建物・木・花）の .mat はこの値のまま読むので、
        /// 陰の付け方・リム・顔の陰・髪の輪は今までと同じ見た目になる値でなければならない。
        /// 輪郭の上限だけは全マテリアルに効かせる (#44)。
        /// </summary>
        private static readonly (string Name, float Value)[] Defaults =
        {
            ("_ShadeByNdotL", 0f),
            ("_RimBacklight", 0f),
            ("_RimLightAlign", 0f),
            ("_FaceShadow", 0f),
            ("_HairHighlightIntensity", 0f),
            ("_OutlineMaxPixels", 6f),
        };

        /// <summary>マテリアルごとに切り替えるキーワード（shader_feature_local）。</summary>
        private static readonly string[] LocalKeywords =
        {
            ToonLook.FaceShadowKeyword, "_PATTERN_ON", "_VERTEXCOLOR_ON", "_ALPHATEST_ON",
        };

        /// <summary>URP がパイプラインから有効にするキーワード（multi_compile）。local にすると URP の指定が届かない。</summary>
        private static readonly string[] GlobalKeywords =
        {
            "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_CLUSTER_LIGHT_LOOP", "_LIGHT_LAYERS",
            "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_CASTING_PUNCTUAL_LIGHT_SHADOW",
        };

        /// <summary>CBUFFER の中の 1 行（型 名前;）。</summary>
        private static readonly Regex MemberPattern =
            new Regex(@"\b(?:float|half|int|uint|fixed|real)(?:[1-4](?:x[1-4])?)?\s+(\w+)\s*;");

        private static Shader LoadShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath + " を読めない");
            return shader;
        }

        private static string ReadSource()
        {
            string path = Path.Combine(Application.dataPath, "Shaders", "KCD_Toon.shader");
            Assert.IsTrue(File.Exists(path), path + " が無い");
            string text = File.ReadAllText(path);
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(text, @"//[^\n]*", string.Empty);
        }

        private static ShaderData.Pass FindPass(Shader shader, string name)
        {
            ShaderData data = ShaderUtil.GetShaderData(shader);
            Assert.IsNotNull(data, "ShaderData を取れない");
            Assert.Greater(data.SerializedSubshaderCount, 0, "SubShader が無い");
            ShaderData.Subshader subshader = data.GetSerializedSubshader(0);
            var names = new List<string>();
            for (int i = 0; i < subshader.PassCount; i++)
            {
                ShaderData.Pass pass = subshader.GetPass(i);
                if (string.Equals(pass.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return pass;
                }

                names.Add(pass.Name);
            }

            Assert.Fail("パス " + name + " が無い（あるのは " + string.Join(", ", names) + "）");
            return null;
        }

        private static ShaderData.VariantCompileInfo Compile(ShaderData.Pass pass, Variant variant, ShaderType stage)
        {
            string key = string.Join(
                "|", variant.Pass, variant.Target.Platform.ToString(), stage.ToString(), string.Join(" ", variant.Keywords));
            if (!Compiled.TryGetValue(key, out ShaderData.VariantCompileInfo info))
            {
                info = pass.CompileVariant(stage, variant.Keywords, variant.Target.Platform, variant.Target.Build);
                Compiled[key] = info;
            }

            return info;
        }

        private static string Label(Variant variant, ShaderType stage)
        {
            string keywords = variant.Keywords.Length == 0 ? "(キーワードなし)" : string.Join(" ", variant.Keywords);
            return variant.Target.Label + " " + variant.Pass + " " + stage + " " + keywords;
        }

        private static string Describe(IEnumerable<ShaderMessage> messages)
        {
            if (messages == null)
            {
                return "  (メッセージなし)";
            }

            var lines = new List<string>();
            foreach (ShaderMessage message in messages)
            {
                if (message.severity != ShaderCompilerMessageSeverity.Error)
                {
                    continue;
                }

                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0} ({1}:{2}, {3}) {4}",
                    message.message,
                    message.file,
                    message.line,
                    message.platform,
                    message.messageDetails));
            }

            return lines.Count == 0 ? "  (エラーのメッセージなし)" : string.Join("\n", lines);
        }

        [Test]
        public void シェーダがKCD_Toonの名前で見つかる()
        {
            Shader shader = LoadShader();
            Assert.AreEqual(ToonLook.ShaderName, shader.name, "シェーダの名前と ToonLook.ShaderName が違う");
            Assert.AreSame(shader, Shader.Find(ToonLook.ShaderName), "Shader.Find で別のシェーダが返る");
        }

        [Test]
        public void 取り込みでエラーが出ていない()
        {
            Shader shader = LoadShader();
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            bool hasErrorMessage = messages != null
                && messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error);
            Assert.IsFalse(
                ShaderUtil.ShaderHasError(shader) || hasErrorMessage,
                "KCD/Toon の取り込みでエラーが出ている:\n" + Describe(messages));
        }

        [Test]
        public void 各パスがWebとWindowsでエラーなくコンパイルできる()
        {
            Shader shader = LoadShader();
            var failures = new List<string>();
            foreach (Variant variant in Variants)
            {
                ShaderData.Pass pass = FindPass(shader, variant.Pass);
                foreach (ShaderType stage in variant.Target.Stages)
                {
                    ShaderData.VariantCompileInfo info = Compile(pass, variant, stage);
                    bool hasError = info.Messages != null
                        && info.Messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error);
                    if (!info.Success || hasError)
                    {
                        failures.Add(Label(variant, stage) + ":\n" + Describe(info.Messages));
                    }
                    else if (info.ShaderData == null || info.ShaderData.Length == 0)
                    {
                        failures.Add(Label(variant, stage) + ": 成功扱いだがバイトコードが空（その段のプログラムが無い）");
                    }
                }
            }

            Assert.IsEmpty(failures, "コンパイルできない組み合わせがある:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// SRP Batcher は、同じシェーダの全パスで UnityPerMaterial の並びが同じことを求める。
        /// D3D のコンパイラは使っていない値も含めて定数バッファの並びを残すので、大きさが揃えば並びも揃っている。
        /// </summary>
        [Test]
        public void UnityPerMaterialの大きさが全パスと全段で揃う()
        {
            Shader shader = LoadShader();
            var failures = new List<string>();
            var sizes = new List<string>();
            int expected = -1;

            foreach (Variant variant in Variants.Where(v => v.Target == Windows))
            {
                ShaderData.Pass pass = FindPass(shader, variant.Pass);
                foreach (ShaderType stage in Windows.Stages)
                {
                    ShaderData.VariantCompileInfo info = Compile(pass, variant, stage);
                    string label = Label(variant, stage);
                    if (!info.Success)
                    {
                        failures.Add(label + ": コンパイルできないので確かめられない");
                        continue;
                    }

                    ShaderData.ConstantBufferInfo[] buffers = info.ConstantBuffers ?? new ShaderData.ConstantBufferInfo[0];
                    int index = System.Array.FindIndex(buffers, b => b.Name == PerMaterial);
                    bool required = variant.Pass == "ToonForward" || variant.Pass == "Outline";
                    if (index < 0)
                    {
                        if (required)
                        {
                            failures.Add(label + ": " + PerMaterial + " が無い");
                        }

                        continue;
                    }

                    int size = buffers[index].Size;
                    sizes.Add(label + " = " + size + " B");
                    if (expected < 0)
                    {
                        expected = size;
                    }
                    else if (size != expected)
                    {
                        failures.Add(label + ": " + size + " B（最初に見たのは " + expected + " B）");
                    }
                }
            }

            Assert.Greater(expected, 0, PerMaterial + " を 1 つも見つけられない");
            Assert.IsEmpty(
                failures,
                "UnityPerMaterial がパスや段で違う（SRP Batcher から外れる）:\n" + string.Join("\n", failures)
                + "\n見た大きさ:\n" + string.Join("\n", sizes));
        }

        [Test]
        public void 全プロパティがHLSLINCLUDEのUnityPerMaterialにある()
        {
            Shader shader = LoadShader();
            string text = ReadSource();

            MatchCollection starts = Regex.Matches(text, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)");
            Assert.AreEqual(1, starts.Count, "CBUFFER_START(UnityPerMaterial) はシェーダ全体で 1 つにする");

            int include = text.IndexOf("HLSLINCLUDE", System.StringComparison.Ordinal);
            int start = starts[0].Index;
            int end = text.IndexOf("CBUFFER_END", start, System.StringComparison.Ordinal);
            int endHlsl = end < 0 ? -1 : text.IndexOf("ENDHLSL", end, System.StringComparison.Ordinal);
            Match subShader = Regex.Match(text, @"\bSubShader\b");
            Assert.IsTrue(
                include >= 0 && include < start && start < end && end < endHlsl
                && subShader.Success && endHlsl < subShader.Index,
                "UnityPerMaterial は SubShader の前の HLSLINCLUDE に置き、全パスで同じ並びにする");

            string block = text.Substring(start, end - start);
            var members = new HashSet<string>(MemberPattern.Matches(block).Cast<Match>().Select(m => m.Groups[1].Value));
            string outside = text.Substring(0, start) + text.Substring(end);
            var outsideNames = new HashSet<string>(
                MemberPattern.Matches(outside).Cast<Match>().Select(m => m.Groups[1].Value));

            var failures = new List<string>();
            var known = new HashSet<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                known.Add(name);
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    known.Add(name + "_ST");
                    if (members.Contains(name))
                    {
                        failures.Add(name + ": テクスチャが UnityPerMaterial に入っている");
                    }

                    bool noScaleOffset = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.NoScaleOffset) != 0;
                    if (!noScaleOffset && !members.Contains(name + "_ST"))
                    {
                        failures.Add(name + "_ST が UnityPerMaterial に無い");
                    }
                }
                else if (!members.Contains(name))
                {
                    failures.Add(name + " が UnityPerMaterial に無い");
                }

                if (outsideNames.Contains(name))
                {
                    failures.Add(name + " が UnityPerMaterial の外でも宣言されている");
                }
            }

            foreach (string member in members)
            {
                if (!known.Contains(member))
                {
                    failures.Add(member + ": UnityPerMaterial にあるがプロパティに無い（綴りの違いでマテリアルの値が届かない）");
                }
            }

            Assert.Greater(members.Count, 30, "UnityPerMaterial の中身を読めていない");
            Assert.IsEmpty(failures, "SRP Batcher に乗らない書き方がある:\n" + string.Join("\n", failures));
        }

        [Test]
        public void 元のプロパティと足したプロパティが同じ名前と型である()
        {
            Shader shader = LoadShader();
            var failures = new List<string>();
            foreach ((string name, ShaderPropertyType type) in OriginalProperties.Concat(ToonProperties))
            {
                int index = shader.FindPropertyIndex(name);
                if (index < 0)
                {
                    failures.Add(name + " が無い");
                }
                else if (shader.GetPropertyType(index) != type)
                {
                    failures.Add(name + " の型が " + shader.GetPropertyType(index) + "（期待は " + type + "）");
                }
            }

            int face = shader.FindPropertyIndex("_FaceShadow");
            if (face >= 0 && !shader.GetPropertyAttributes(face).Contains("Toggle(" + ToonLook.FaceShadowKeyword + ")"))
            {
                failures.Add("_FaceShadow に [Toggle(" + ToonLook.FaceShadowKeyword + ")] が無い（インスペクタで切り替えてもキーワードが付かない）");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void 足したプロパティの既定値でキャラ以外の見た目が変わらない()
        {
            Shader shader = LoadShader();
            var failures = new List<string>();
            foreach ((string name, float value) in Defaults)
            {
                int index = shader.FindPropertyIndex(name);
                if (index < 0)
                {
                    failures.Add(name + " が無い");
                    continue;
                }

                float actual = shader.GetPropertyDefaultFloatValue(index);
                if (Mathf.Abs(actual - value) > 1e-6f)
                {
                    failures.Add(string.Format(CultureInfo.InvariantCulture, "{0} の既定値が {1}（期待は {2}）", name, actual, value));
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void マテリアルのキーワードはlocalでパイプラインのキーワードはglobalである()
        {
            Shader shader = LoadShader();
            LocalKeywordSpace space = shader.keywordSpace;
            var failures = new List<string>();
            foreach (string name in LocalKeywords)
            {
                LocalKeyword keyword = space.FindKeyword(name);
                if (!keyword.isValid)
                {
                    failures.Add(name + " が宣言されていない");
                }
                else if (keyword.isOverridable)
                {
                    failures.Add(name + " が global になっている（shader_feature_local にする）");
                }
            }

            foreach (string name in GlobalKeywords)
            {
                LocalKeyword keyword = space.FindKeyword(name);
                if (!keyword.isValid)
                {
                    failures.Add(name + " が宣言されていない（URP が有効にしても変わらない）");
                }
                else if (!keyword.isOverridable)
                {
                    failures.Add(name + " が local になっている（URP の指定が届かない）");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// ToonLook.FaceLit / OutlinePixels / FaceFrameMarker はシェーダの式を C# に写したもので、
        /// ToonLookTests はそちらで #44 の表と顔の陰を確かめる。シェーダだけ直して写しがずれないよう、式の行を突き合わせる。
        /// </summary>
        [Test]
        public void ToonLookに写した式がシェーダと同じである()
        {
            string text = Regex.Replace(ReadSource(), @"\s+", " ");
            string marker = ToonLook.FaceFrameMarker.ToString(CultureInfo.InvariantCulture) + "h";
            string[] anchors =
            {
                "return abs(faceFrame.w) < " + marker + ";",
                "float2 right = float2(forward.y, -forward.x);",
                "float2 lightN = lightH / max(lengthH, 1e-4);",
                "float front = dot(forward, lightN);",
                "float side = dot(right, lightN);",
                "float u = faceFrame.w;",
                "float facing = u * side + sqrt(saturate(1.0 - u * u)) * front;",
                "float threshold = lerp(-1.0 - 2.0 * _FaceShadowSoftness, -_FaceShadowBias * (1.0 - abs(front)), saturate(lengthH * 2.0));",
                "float lit = smoothstep(threshold - _FaceShadowSoftness, threshold + _FaceShadowSoftness, facing);",
                "float distanceScale = clamp(cameraDistance, 0.5, _OutlineNearDistance);",
                "float fade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, cameraDistance);",
                "float width = _OutlineWidth * distanceScale;",
                "float maxWidth = _OutlineMaxPixels * 2.0 * positionInputs.positionCS.w / (1080.0 * max(abs(UNITY_MATRIX_P[1][1]), 1e-4));",
                "float3 offsetWS = normalInputs.normalWS * (width * fade);",
            };

            var missing = anchors.Where(a => text.IndexOf(a, System.StringComparison.Ordinal) < 0).ToList();
            Assert.IsEmpty(
                missing,
                "シェーダの式が ToonLook の写しと違う。両方を同じ式に直す:\n" + string.Join("\n", missing));
        }
    }
}
