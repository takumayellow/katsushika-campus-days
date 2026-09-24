// Katsushika Campus Days — アニメ調セルシェーダ (URP 17 / Unity 6)
// 2 段階の陰 + リムライト + 反転法線アウトライン。SRP Batcher 対応（全プロパティを UnityPerMaterial に統一）。
Shader "KCD/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1,1,1,1)
        _ShadeColor("Shade Color", Color) = (0.62, 0.60, 0.72, 1)
        _ShadeThreshold("Shade Threshold", Range(-1, 1)) = 0.1
        _ShadeSoftness("Shade Softness", Range(0.001, 0.5)) = 0.03
        _ShadeThreshold2("Shade Threshold 2", Range(-1, 1)) = -0.25
        _ShadeColor2("Shade Color 2", Color) = (0.42, 0.40, 0.55, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.5, 16)) = 4
        _RimIntensity("Rim Intensity", Range(0, 2)) = 0.35
        _SpecularColor("Specular Color", Color) = (1,1,1,1)
        _SpecularPower("Specular Power", Range(1, 256)) = 48
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 0
        _OutlineColor("Outline Color", Color) = (0.12, 0.10, 0.14, 1)
        _OutlineWidth("Outline Width (m)", Range(0, 0.05)) = 0.006
        _OutlineNearDistance("Outline Near Distance (m)", Range(0.5, 30)) = 5
        _OutlineFadeStart("Outline Fade Start (m)", Range(0, 200)) = 30
        _OutlineFadeEnd("Outline Fade End (m)", Range(0, 200)) = 60
        _EmissionColor("Emission Color", Color) = (0,0,0,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5

        // 陰の付け方 (#14)。1 にすると N・L をそのまま 2 段の閾値にかけ、2 段目は _ShadeSoftness2 でぼかす。
        // 0 のマテリアル（木や花）は従来の half-lambert × 影のまま、リムも従来どおり光の色を掛けない。
        _ShadeByNdotL("Shade By N dot L", Float) = 0
        _ShadeSoftness2("Shade Softness 2", Range(0.001, 0.5)) = 0.08
        // 逆光（光がカメラの奥から来る）ほど縁を明るくする倍率と、光の来る側の縁に寄せる度合い。
        _RimBacklight("Rim Backlight Boost", Range(0, 4)) = 0
        _RimLightAlign("Rim Light Side Only", Range(0, 1)) = 0
        // 顔の陰を法線ではなく頭の向きで決める。CharacterImporter が顔の頂点の tangent に
        // xyz = 頭の前方向、w = 顔の左右の位置（キャラの右が +、-1〜1）を焼く。
        [Toggle(_FACE_SHADOW_ON)] _FaceShadow("Face Shadow (Head Direction)", Float) = 0
        _FaceShadowSoftness("Face Shadow Softness", Range(0.001, 0.5)) = 0.04
        _FaceShadowBias("Face Shadow Bias", Range(-1, 1)) = 0
        // 髪の天使の輪。視線と法線の高さから、頭の上側を横切る帯を描く。
        _HairHighlightColor("Hair Highlight Color", Color) = (1,1,1,1)
        _HairHighlightIntensity("Hair Highlight Intensity", Range(0, 1)) = 0
        _HairHighlightHeight("Hair Highlight Height", Range(0, 3)) = 1
        _HairHighlightWidth("Hair Highlight Width", Range(0, 0.5)) = 0.05
        _HairHighlightSoftness("Hair Highlight Softness", Range(0.001, 0.2)) = 0.02
        _HairHighlightJag("Hair Highlight Jag", Range(0, 0.1)) = 0.02
        // アウトラインの画面上の太さの上限（高さ 1080 px の画面に換算した px）。0 で上限なし (#44)。
        _OutlineMaxPixels("Outline Max Pixels (at 1080p)", Range(0, 20)) = 6

        // 和柄（絣・ハート柄）。Blender の「Generated 座標 × 倍率 → 画像のボックス投影」と同じ貼り方 (#55)。
        // Generated 座標と bind 時の法線は CharacterImporter が UV2 / UV3 に焼く。
        [Toggle(_PATTERN_ON)] _Pattern("Pattern", Float) = 0
        [NoScaleOffset] _PatternMap("Pattern Map", 2D) = "white" {}
        _PatternScale("Pattern Scale", Float) = 1
        _PatternBlend("Pattern Box Blend", Range(0, 1)) = 0.25

        // 頂点カラーを基本色に掛ける（木の葉。1 マテリアルのまま面ごとに明暗と色を変える, #51）。
        // FBX の頂点カラーは sRGB で入ってくるので、リニア空間では頂点段でリニアに直す。
        [Toggle(_VERTEXCOLOR_ON)] _VertexColor("Vertex Color", Float) = 0

        // Surface / blending state（マテリアル側から差し替える）
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4  _BaseColor;
        half4  _ShadeColor;
        half4  _ShadeColor2;
        half4  _RimColor;
        half4  _SpecularColor;
        half4  _OutlineColor;
        half4  _EmissionColor;
        half4  _HairHighlightColor;
        half   _ShadeThreshold;
        half   _ShadeThreshold2;
        half   _ShadeSoftness;
        half   _RimPower;
        half   _RimIntensity;
        half   _SpecularPower;
        half   _SpecularIntensity;
        half   _OutlineWidth;
        half   _OutlineNearDistance;
        half   _OutlineFadeStart;
        half   _OutlineFadeEnd;
        half   _OutlineMaxPixels;
        half   _ShadeByNdotL;
        half   _ShadeSoftness2;
        half   _RimBacklight;
        half   _RimLightAlign;
        half   _FaceShadow;
        half   _FaceShadowSoftness;
        half   _FaceShadowBias;
        half   _HairHighlightIntensity;
        half   _HairHighlightHeight;
        half   _HairHighlightWidth;
        half   _HairHighlightSoftness;
        half   _HairHighlightJag;
        half   _Cutoff;
        half   _Pattern;
        float  _PatternScale;
        half   _PatternBlend;
        half   _VertexColor;
        half   _Surface;
        half   _SrcBlend;
        half   _DstBlend;
        half   _ZWrite;
        half   _Cull;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // ------------------------------------------------------------------
        // アウトライン（背面押し出し）。透過マテリアルでは _OutlineWidth = 0 にする。
        // ------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                half   fogFactor  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            OutlineVaryings OutlineVertex(OutlineAttributes input)
            {
                OutlineVaryings output = (OutlineVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                // _OutlineNearDistance までは距離に比例させて画面上の太さを一定にし、それより遠くでは
                // ワールド上の太さを固定して、遠くの人ほど細く描く（画面上で一定のままだと、遠くの小さな人物が
                // 輪郭で太って見えた）。さらに _OutlineFadeStart〜_OutlineFadeEnd で消す。
                float cameraDistance = length(GetCameraPositionWS() - positionInputs.positionWS);
                float distanceScale = clamp(cameraDistance, 0.5, _OutlineNearDistance);
                float fade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, cameraDistance);
                float width = _OutlineWidth * distanceScale;
                if (_OutlineMaxPixels > 0.0)
                {
                    // 画面上の太さの上限。視野角を狭めた寄りのカメラ（タイトルやプレビュー）で線が太りすぎないように、
                    // 奥行き w で 1080p 換算 _OutlineMaxPixels px になるワールド上の幅で頭打ちにする。
                    // 1 px = 2w / (1080 * P[1][1])。レンダーターゲットの上下反転で P[1][1] が負になるので abs を取る。
                    float maxWidth = _OutlineMaxPixels * 2.0 * positionInputs.positionCS.w
                        / (1080.0 * max(abs(UNITY_MATRIX_P[1][1]), 1e-4));
                    width = min(width, maxWidth);
                }
                float3 offsetWS = normalInputs.normalWS * (width * fade);

                output.positionCS = TransformWorldToHClip(positionInputs.positionWS + offsetWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, _OutlineColor.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // 本体（セルシェード）
        // ------------------------------------------------------------------
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _PATTERN_ON
            #pragma shader_feature_local _VERTEXCOLOR_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            // 追加ライトの当たり方 (#14)。Web（Mobile_RPAsset）は Forward で 1 オブジェクト 4 灯まで
            // （_ADDITIONAL_LIGHTS）、Windows（PC_RPAsset）は Forward+（_CLUSTER_LIGHT_LOOP）で数の制限なし。
            // どちらの RP アセットもライトレイヤーを有効にしているので、URP の Lit と同じく _LIGHT_LAYERS も受ける。
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma shader_feature_local _FACE_SHADOW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PatternMap);
            SAMPLER(sampler_PatternMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                #if defined(_FACE_SHADOW_ON)
                    float4 tangentOS  : TANGENT;
                #endif
                float2 uv         : TEXCOORD0;
                #if defined(_PATTERN_ON)
                    float3 generated  : TEXCOORD2;
                    float3 bindNormal : TEXCOORD3;
                #endif
                #if defined(_VERTEXCOLOR_ON)
                    half4 color : COLOR;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half3  vertexSH   : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                #if defined(_FACE_SHADOW_ON)
                    half4  faceFrame  : TEXCOORD8;
                #endif
                #if defined(_PATTERN_ON)
                    float3 generated  : TEXCOORD5;
                    half3  bindNormal : TEXCOORD6;
                #endif
                #if defined(_VERTEXCOLOR_ON)
                    half3  vertexColor : TEXCOORD7;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vertexSH = SampleSHVertex(normalInputs.normalWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                #if defined(_FACE_SHADOW_ON)
                    // スキニングで頭の骨と一緒に回った tangent を、そのまま頭の前方向として使う。
                    output.faceFrame = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                #endif
                #if defined(_PATTERN_ON)
                    output.generated = input.generated * _PatternScale;
                    output.bindNormal = input.bindNormal;
                #endif
                #if defined(_VERTEXCOLOR_ON)
                    #if defined(UNITY_COLORSPACE_GAMMA)
                        output.vertexColor = input.color.rgb;
                    #else
                        output.vertexColor = SRGBToLinear(input.color.rgb);
                    #endif
                #endif
                return output;
            }

            // Blender の画像テクスチャ「ボックス」投影（Cycles の svm_image_texture の BOX と同じ重み）。
            // co と n は Blender のオブジェクト軸（Z が上）。X を向く面は (y, z)、Y は (x, z)、Z は (y, x) で引く。
            half3 SamplePatternBox(float3 co, half3 n, half blend)
            {
                n = abs(n);
                n /= max(n.x + n.y + n.z, 1e-5h);
                half limit = 0.5h * (1.0h + blend);
                half3 w = half3(0.0h, 0.0h, 0.0h);
                if (n.x > limit * (n.x + n.y) && n.x > limit * (n.x + n.z)) { w.x = 1.0h; }
                else if (n.y > limit * (n.x + n.y) && n.y > limit * (n.y + n.z)) { w.y = 1.0h; }
                else if (n.z > limit * (n.x + n.z) && n.z > limit * (n.y + n.z)) { w.z = 1.0h; }
                else if (blend > 0.0h)
                {
                    if (n.z < (1.0h - limit) * (n.y + n.x))
                    {
                        w.x = saturate((n.x / (n.x + n.y) - 0.5h * (1.0h - blend)) / blend);
                        w.y = 1.0h - w.x;
                    }
                    else if (n.x < (1.0h - limit) * (n.y + n.z))
                    {
                        w.y = saturate((n.y / (n.y + n.z) - 0.5h * (1.0h - blend)) / blend);
                        w.z = 1.0h - w.y;
                    }
                    else if (n.y < (1.0h - limit) * (n.x + n.z))
                    {
                        w.x = saturate((n.x / (n.x + n.z) - 0.5h * (1.0h - blend)) / blend);
                        w.z = 1.0h - w.x;
                    }
                    else
                    {
                        w = ((2.0h - limit) * n + (limit - 1.0h)) / (2.0h * limit - 1.0h);
                    }
                }
                else
                {
                    w.x = 1.0h;
                }

                half3 color = 0.0h;
                if (w.x > 0.0h) { color += w.x * SAMPLE_TEXTURE2D(_PatternMap, sampler_PatternMap, co.yz).rgb; }
                if (w.y > 0.0h) { color += w.y * SAMPLE_TEXTURE2D(_PatternMap, sampler_PatternMap, co.xz).rgb; }
                if (w.z > 0.0h) { color += w.z * SAMPLE_TEXTURE2D(_PatternMap, sampler_PatternMap, co.yx).rgb; }
                return color;
            }

            // 2 段階のトゥーンランプ。明部 → 1 段目の陰（_ShadeColor）→ 2 段目の陰（_ShadeColor2）。
            // 陰の色は albedo に掛ける係数なので、肌は赤み、服は元の色相のまま暗くなる（MaterialLibrary が決める）。
            // _ShadeByNdotL = 1 のマテリアルは N・L をそのまま閾値にかけ、落ち影は 1 段目の陰にする。
            // litAmount は明部の割合（1 = 明部）。
            half3 ToonRamp(half ndotl, half shadowAttenuation, half3 albedo, out half litAmount)
            {
                half step1;
                half step2;
                if (_ShadeByNdotL > 0.5h)
                {
                    step1 = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndotl) * shadowAttenuation;
                    step2 = smoothstep(_ShadeThreshold2 - _ShadeSoftness2, _ShadeThreshold2 + _ShadeSoftness2, ndotl);
                }
                else
                {
                    half lambert = ndotl * 0.5h + 0.5h;      // half-lambert にすると顔の陰が硬くなりすぎない
                    lambert *= lerp(0.55h, 1.0h, shadowAttenuation);
                    step1 = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, lambert);
                    step2 = smoothstep(_ShadeThreshold2 - _ShadeSoftness, _ShadeThreshold2 + _ShadeSoftness, lambert);
                }

                litAmount = step1;
                half3 darkest = albedo * _ShadeColor2.rgb;
                half3 mid = albedo * _ShadeColor.rgb;
                half3 lit = albedo;

                half3 color = lerp(darkest, mid, step2);
                color = lerp(color, lit, step1);
                return color;
            }

            #if defined(_FACE_SHADOW_ON)
            // 顔の頂点には CharacterImporter が頭の向きを焼いてある（焼いていない頂点の tangent.w は ±1）。
            bool HasFaceFrame(half4 faceFrame)
            {
                return abs(faceFrame.w) < 0.995h;
            }

            // 顔の明部の割合を、法線ではなく頭の向きで決める（法線で塗ると鼻や頬の凹凸で陰がまだらになる）。
            // 顔を縦の円柱とみなし、焼いた左右の位置 w = sinθ の面が水平方向の光へ向く度合い cos(θ-φ) で明暗を分ける。
            // 真横からなら顔の中央、正面からなら顔の外（全部明部）、真後ろからなら全部陰。
            // 光が真上に近い（水平成分が小さい）ほど境目を顔の外へ出して、顔全体を明部にする
            // （真後ろの高い光では両方の縁から中央へ明部が広がる。光が顔の正面の左右を跨いでも明暗は跳ばない）。
            // _FaceShadowBias は斜めの光で境目を陰の側へずらす量。正面と真後ろでは効かせない
            // （真後ろからの光で顔の縁に細い明部が残らない）。ToonLook.FaceLit が同じ式を C# で持つ。
            half FaceLit(half4 faceFrame, half3 lightDirWS, half shadowAttenuation)
            {
                float2 forward = faceFrame.xz;
                forward *= rsqrt(max(dot(forward, forward), 1e-6));
                float2 right = float2(forward.y, -forward.x);    // +Z を向くキャラの右は +X
                float2 lightH = lightDirWS.xz;
                float lengthH = length(lightH);
                float2 lightN = lightH / max(lengthH, 1e-4);
                float front = dot(forward, lightN);
                float side = dot(right, lightN);
                float u = faceFrame.w;
                float facing = u * side + sqrt(saturate(1.0 - u * u)) * front;
                float threshold = lerp(-1.0 - 2.0 * _FaceShadowSoftness, -_FaceShadowBias * (1.0 - abs(front)), saturate(lengthH * 2.0));
                float lit = smoothstep(threshold - _FaceShadowSoftness, threshold + _FaceShadowSoftness, facing);
                return (half)lit * shadowAttenuation;
            }
            #endif

            // 追加ライト 1 灯の当たり方（光の色に掛ける係数）。lit には明部の割合を返す。
            // 明部は光の色そのまま、陰の側は 1 段目の陰の色で半分だけ、2 段目より奥には当てない。
            // 顔は頭の向きで明部を決め、陰の側は一様に半分。_ShadeByNdotL = 0 のマテリアルは従来の素朴な 2 値。
            half3 AdditionalLightResponse(half3 normalWS, Light light, half4 faceFrame, out half lit)
            {
                #if defined(_FACE_SHADOW_ON)
                    if (HasFaceFrame(faceFrame))
                    {
                        lit = FaceLit(faceFrame, light.direction, light.shadowAttenuation);
                        return lerp(_ShadeColor.rgb * 0.5h, half3(1.0h, 1.0h, 1.0h), lit);
                    }
                #endif

                half ndotl = dot(normalWS, light.direction);
                if (_ShadeByNdotL < 0.5h)
                {
                    lit = smoothstep(_ShadeThreshold, _ShadeThreshold + _ShadeSoftness * 4.0h, saturate(ndotl)) * light.shadowAttenuation;
                    return half3(lit, lit, lit);
                }

                lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndotl) * light.shadowAttenuation;
                half mid = smoothstep(_ShadeThreshold2 - _ShadeSoftness2, _ShadeThreshold2 + _ShadeSoftness2, ndotl);
                return lerp(_ShadeColor.rgb * (mid * 0.5h), half3(1.0h, 1.0h, 1.0h), lit);
            }

            // 追加ライト 1 灯ぶんを color に足し、髪の輪とリムに使う光の量を lightSum に貯める。
            void AddAdditionalLight(Light light, half3 normalWS, half3 albedo, half4 faceFrame, inout half3 color, inout half3 lightSum)
            {
                half lit;
                half3 response = AdditionalLightResponse(normalWS, light, faceFrame, lit);
                half3 lightColor = light.color * light.distanceAttenuation;
                color += albedo * lightColor * response;
                lightSum += lightColor * lit;
            }

            half4 ToonFragment(Varyings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = baseSample.rgb;
                half alpha = baseSample.a;

                #if defined(_PATTERN_ON)
                    albedo *= SamplePatternBox(input.generated, input.bindNormal, _PatternBlend);
                #endif
                #if defined(_VERTEXCOLOR_ON)
                    albedo *= input.vertexColor;
                #endif

                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif

                // 両面描画のとき、裏から見えている面は法線を裏返して外向きの陰影にする
                half3 normalWS = normalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(isFrontFace, normalWS, -normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // LIGHT_LOOP_BEGIN（Forward+ のクラスタ走査）は inputData という名前の変数を読む。
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half4 shadowMask = CalculateShadowMask(inputData);
                uint meshRenderingLayers = GetMeshRenderingLayer();

                half4 faceFrame = half4(0.0h, 0.0h, 0.0h, 1.0h);
                #if defined(_FACE_SHADOW_ON)
                    faceFrame = input.faceFrame;
                #endif

                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask);
                half3 mainColor = mainLight.color * mainLight.distanceAttenuation;
                #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
                    {
                        mainColor = half3(0.0h, 0.0h, 0.0h);
                    }
                #endif

                half ndotl = dot(normalWS, mainLight.direction);
                half litAmount;
                half3 color = ToonRamp(ndotl, mainLight.shadowAttenuation, albedo, litAmount);
                #if defined(_FACE_SHADOW_ON)
                    if (HasFaceFrame(faceFrame))
                    {
                        // 顔は 1 段だけ。鼻や頬の法線で 2 段目の陰がまだらに出ないようにする。
                        litAmount = FaceLit(faceFrame, mainLight.direction, mainLight.shadowAttenuation);
                        color = lerp(albedo * _ShadeColor.rgb, albedo, litAmount);
                    }
                #endif
                color *= mainColor;

                // 環境光（SH）は明部にも暗部にも乗せて、影が真っ黒になるのを防ぐ。
                half3 ambientLight = SampleSHPixel(input.vertexSH, normalWS) * 0.6h;
                color += ambientLight * albedo;

                // 追加ライト（屋内の照明や街灯）。Forward は 1 オブジェクト 4 灯まで、Forward+ はクラスタから引く。
                half3 additionalLight = half3(0.0h, 0.0h, 0.0h);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();

                    #if USE_CLUSTER_LIGHT_LOOP
                        // Forward+ では平行光源の追加ライトはクラスタに入らないので、先に数ぶん回す。
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

                            Light directionalLight = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
                            #ifdef _LIGHT_LAYERS
                            if (IsMatchingLightLayer(directionalLight.layerMask, meshRenderingLayers))
                            #endif
                            {
                                AddAdditionalLight(directionalLight, normalWS, albedo, faceFrame, color, additionalLight);
                            }
                        }
                    #endif

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask);
                        #ifdef _LIGHT_LAYERS
                        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                        #endif
                        {
                            AddAdditionalLight(light, normalWS, albedo, faceFrame, color, additionalLight);
                        }
                    LIGHT_LOOP_END
                #endif

                // _ShadeByNdotL = 1 のマテリアルは、スペキュラ・リム・髪の輪にも光の色と強さを掛ける
                // （夜や屋内で縁だけ白く光らない）。0 のマテリアルは従来どおり白のまま。
                bool shadeByNdotL = _ShadeByNdotL > 0.5h;

                // スペキュラ（既定では 0）
                if (_SpecularIntensity > 0.0h)
                {
                    half3 halfVector = normalize(mainLight.direction + viewDirWS);
                    half specular = pow(saturate(dot(normalWS, halfVector)), _SpecularPower);
                    specular = smoothstep(0.35h, 0.45h, specular);
                    half3 specularLight = shadeByNdotL ? mainColor : half3(1.0h, 1.0h, 1.0h);
                    color += _SpecularColor.rgb * specularLight * (specular * _SpecularIntensity * mainLight.shadowAttenuation);
                }

                // リムライト（輪郭の抜け）。逆光（光がカメラから見て奥）ほど強め、光の来る側の縁に寄せる。
                half rim = 1.0h - saturate(dot(normalWS, viewDirWS));
                rim = pow(rim, _RimPower) * _RimIntensity;
                rim *= 1.0h + _RimBacklight * saturate(dot(-viewDirWS, mainLight.direction));
                rim *= lerp(1.0h, saturate(ndotl * 0.5h + 0.5h), _RimLightAlign);
                half3 rimLight = shadeByNdotL
                    ? mainColor * lerp(0.3h, 1.0h, mainLight.shadowAttenuation) + additionalLight
                    : half3(1.0h, 1.0h, 1.0h);
                color += _RimColor.rgb * rimLight * rim;

                // 髪の天使の輪。法線の高さが「視線を上へ傾けた向き」の高さと揃う帯を、画面の横方向に少しぎざぎざにして描く。
                if (_HairHighlightIntensity > 0.0h)
                {
                    half3 ringDir = normalize(viewDirWS + half3(0.0h, _HairHighlightHeight, 0.0h));
                    half3 normalVS = TransformWorldToViewNormal(normalWS);
                    half offset = normalWS.y - ringDir.y + sin(normalVS.x * (half)(PI * 6.0)) * _HairHighlightJag;
                    half ring = 1.0h - smoothstep(_HairHighlightWidth, _HairHighlightWidth + _HairHighlightSoftness, abs(offset));
                    ring *= saturate(dot(normalWS, viewDirWS) * 2.0h);
                    half3 ringLight = mainColor * lerp(0.35h, 1.0h, litAmount) + additionalLight + ambientLight;
                    color = lerp(color, _HairHighlightColor.rgb * ringLight, ring * _HairHighlightIntensity);
                }

                color += _EmissionColor.rgb;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthOnlyVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthOnlyFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return half4(normalize(input.normalWS) * 0.5h + 0.5h, 0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
