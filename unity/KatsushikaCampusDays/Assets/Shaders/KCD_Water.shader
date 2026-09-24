// Katsushika Campus Days — 屋外の水面（図書館の堀と池）(URP 17 / Unity 6)
// 縁からの距離で色を段に変え、縁石の際に暗い帯と白い線を引く。さざ波は同じテクスチャを 2 枚ずらして流し、
// 空の色をフレネルで映して、太陽のきらめきを足す (#58)。
//
// Web の URP 設定（Mobile_RPAsset）は深度テクスチャを切っている。水の深さも 3 cm しかない。そこで縁からの距離は
// 深度ではなく、ワールド位置と水盤の矩形 _Basin0〜3（キャンパス軸 _CampusAxis の u, v）から計算で出す。
// 矩形はとなりの矩形と接する辺を向こう側まで延ばして渡す（WaterMaterial.ShaderBasins）ので、継ぎ目に線は出ない。
//
// 1 パスだけ。テクスチャ読みは 3 回（さざ波 2 回 + 主光源の影 1 回）。
// SRP Batcher 対応（テクスチャ以外の全プロパティを UnityPerMaterial に置く）。昼夜は主光源の色と環境光（SH）で追う。
Shader "KCD/Water"
{
    Properties
    {
        // 色と透け（縁の浅いところ → 中央の深いところ）
        [MainColor] _BaseColor("Shallow Color", Color) = (0.5607843, 0.7215686, 0.7843137, 1)
        _DeepColor("Deep Color", Color) = (0.18, 0.44, 0.56, 1)
        _ShallowAlpha("Shallow Alpha", Range(0, 1)) = 0.55
        _DeepAlpha("Deep Alpha", Range(0, 1)) = 0.85
        _DeepDistance("Distance To Deepest (m)", Range(0.1, 20)) = 4
        _GradientSteps("Gradient Steps", Range(1, 8)) = 3
        _GradientSoftness("Gradient Step Softness", Range(0.001, 0.5)) = 0.06

        // 縁石の際の暗い帯
        _RimShade("Curb Shade", Range(0, 1)) = 0.3
        _RimShadeWidth("Curb Shade Width (m)", Range(0, 2)) = 0.5

        // 縁の白い線（泡）
        _FoamColor("Edge Line Color", Color) = (0.96, 0.98, 1, 0.95)
        _FoamWidth("Edge Line Width (m)", Range(0, 1)) = 0.12
        _FoamWobble("Edge Line Wobble", Range(0, 1)) = 0.5

        // 空の映り込み（フレネル）
        _SkyHorizonColor("Sky Horizon Color", Color) = (0.80, 0.89, 0.93, 1)
        _SkyZenithColor("Sky Zenith Color", Color) = (0.42, 0.64, 0.84, 1)
        _SkyBrightness("Sky Brightness From Ambient", Range(0, 10)) = 4
        _FresnelBias("Fresnel Bias", Range(0, 1)) = 0.06
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.85

        // さざ波。RG = 高さの傾き、B = ゆらぎ、A = 高さ。WaterMaterial が生成した water_ripple.png を貼る。
        [NoScaleOffset] _RippleMap("Ripple Map", 2D) = "gray" {}
        _RippleScaleA("Ripple Scale A (1/m)", Float) = 0.1666667
        _RippleScaleB("Ripple Scale B (1/m)", Float) = 0.3846154
        _RippleStrengthA("Ripple Strength A", Range(0, 1)) = 0.22
        _RippleStrengthB("Ripple Strength B", Range(0, 1)) = 0.14
        _RippleFlow("Ripple Flow (A.uv, B.uv) m/s", Vector) = (0.02, 0.07, -0.045, 0.025)
        _CrestColor("Crest Line Color", Color) = (1, 1, 1, 0.35)
        _CrestThreshold("Crest Height", Range(0, 1)) = 0.68
        _CrestWidth("Crest Line Width", Range(0.001, 0.2)) = 0.02

        // 太陽のきらめき
        _SparkleThreshold("Sparkle Threshold (N.H)", Range(0.9, 1)) = 0.996
        _SparkleSoftness("Sparkle Softness", Range(0.0001, 0.01)) = 0.002
        _SparkleIntensity("Sparkle Intensity", Range(0, 8)) = 3
        _SparkleFadeDistance("Sparkle Fade Distance (m)", Range(1, 200)) = 60

        _ShadowDim("Shadow Dim", Range(0, 1)) = 0.4

        // 水盤の形。WaterMaterial が CampusProps の軸と CampusStage.Basins から入れ直す。
        [HideInInspector] _CampusAxis("Campus Axis U (x, z)", Vector) = (0.90958899, -0.41550916, 0, 0)
        [HideInInspector] _Basin0("Basin 0 (u0, v0, u1, v1)", Vector) = (-59.5, -16.4, -50, 24)
        [HideInInspector] _Basin1("Basin 1 (u0, v0, u1, v1)", Vector) = (-59.5, -78, -50, -31.6)
        [HideInInspector] _Basin2("Basin 2 (u0, v0, u1, v1)", Vector) = (-100, -78, -50, -66)
        [HideInInspector] _Basin3("Basin 3 (u0, v0, u1, v1)", Vector) = (0, 0, 0, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _Basin0;
        float4 _Basin1;
        float4 _Basin2;
        float4 _Basin3;
        float4 _CampusAxis;
        float4 _RippleFlow;
        half4  _BaseColor;
        half4  _DeepColor;
        half4  _FoamColor;
        half4  _SkyHorizonColor;
        half4  _SkyZenithColor;
        half4  _CrestColor;
        float  _RippleScaleA;
        float  _RippleScaleB;
        float  _DeepDistance;
        float  _RimShadeWidth;
        float  _FoamWidth;
        float  _SparkleThreshold;
        float  _SparkleSoftness;
        float  _SparkleFadeDistance;
        half   _ShallowAlpha;
        half   _DeepAlpha;
        half   _GradientSteps;
        half   _GradientSoftness;
        half   _RimShade;
        half   _FoamWobble;
        half   _SkyBrightness;
        half   _FresnelBias;
        half   _FresnelPower;
        half   _ReflectionStrength;
        half   _RippleStrengthA;
        half   _RippleStrengthB;
        half   _CrestThreshold;
        half   _CrestWidth;
        half   _SparkleIntensity;
        half   _ShadowDim;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            // 影は硬い 1 回読みだけ（_SHADOWS_SOFT は入れない）。追加ライトも拾わない。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            // 透過物として扱う。画面空間の影（_MAIN_LIGHT_SHADOWS_SCREEN）が来ても影マップを直接読む。
            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RippleMap);
            SAMPLER(sampler_RippleMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 campusUV   : TEXCOORD1;
                float4 rippleUV   : TEXCOORD2;
                half3  ambientUp  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 矩形 r = (u0, v0, u1, v1) の中で、いちばん近い辺までの距離。外なら負。
            float BasinInterior(float2 p, float4 r)
            {
                float2 d = min(p - r.xy, r.zw - p);
                return min(d.x, d.y);
            }

            // 水際からの距離（m）。矩形ごとの内側の距離の最大を取る。
            float EdgeDistance(float2 p)
            {
                float d = max(BasinInterior(p, _Basin0), BasinInterior(p, _Basin1));
                d = max(d, BasinInterior(p, _Basin2));
                d = max(d, BasinInterior(p, _Basin3));
                return max(d, 0.0);
            }

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;

                // ワールドの xz をキャンパス軸 (u, v) に直す。v 軸は u 軸を 90 度回したもの。
                float2 axisU = _CampusAxis.xy;
                float2 xz = positionInputs.positionWS.xz;
                float2 campus = float2(dot(xz, axisU), dot(xz, float2(-axisU.y, axisU.x)));
                output.campusUV = campus;

                // 流れの量は frac で畳み、長く遊んでも UV の精度を落とさない。
                // B は u と v を入れ替えて貼り、A と模様の向きが重ならないようにする。
                float time = _Time.y;
                output.rippleUV.xy = campus * _RippleScaleA - frac(time * _RippleFlow.xy * _RippleScaleA);
                output.rippleUV.zw = campus.yx * _RippleScaleB - frac(time * _RippleFlow.wz * _RippleScaleB)
                    + float2(0.37, 0.61);

                // 真上から来る環境光。DayNightCycle が昼夜で塗り替えるので、空の映り込みの明るさと色味に使う。
                output.ambientUp = max(SampleSH(half3(0.0h, 1.0h, 0.0h)), half3(0.0h, 0.0h, 0.0h));
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // --- さざ波（テクスチャ読み 2 回）---
                half4 rippleA = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, input.rippleUV.xy);
                half4 rippleB = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, input.rippleUV.zw);
                float2 slope = (float2(rippleA.rg) * 2.0 - 1.0) * _RippleStrengthA
                    + (float2(rippleB.gr) * 2.0 - 1.0) * _RippleStrengthB;
                float2 axisU = _CampusAxis.xy;
                float2 gradWS = slope.x * axisU + slope.y * float2(-axisU.y, axisU.x);
                float3 normalWS = normalize(float3(-gradWS.x, 1.0, -gradWS.y));

                // --- 縁からの距離で色を段に分ける ---
                float edge = EdgeDistance(input.campusUV);
                float edgeAA = max(fwidth(edge), 1e-4);
                half steps = max(_GradientSteps, 1.0h);
                float depth01 = saturate((edge - _RimShadeWidth) / _DeepDistance);
                float g = depth01 * steps;
                float gw = min(max(_GradientSoftness, fwidth(g)), 0.5);
                half stepped = half((floor(g) + smoothstep(0.5 - gw, 0.5 + gw, frac(g))) / steps);

                // 縁石の際の暗い帯。色を落として少し濃く見せる。
                half rimBand = half(1.0 - smoothstep(_RimShadeWidth - edgeAA, _RimShadeWidth + edgeAA, edge));
                half3 body = lerp(_BaseColor.rgb, _DeepColor.rgb, stepped) * (1.0h - _RimShade * rimBand);
                half alpha = lerp(_ShallowAlpha, _DeepAlpha, stepped);
                alpha = lerp(alpha, 1.0h, _RimShade * rimBand);

                // --- 光（主光源 + 環境光）。影は硬い 1 回読み ---
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation;
                half3 sun = mainLight.color * saturate(mainLight.direction.y * 4.0h);
                half3 diffuse = sun * lerp(1.0h - _ShadowDim, 1.0h, shadow) + input.ambientUp * 0.6h;
                half3 color = body * diffuse;

                // --- 空の映り込み（フレネル）。空の明るさと色味は真上の環境光から取る ---
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 reflected = reflect(-float3(viewDirWS), normalWS);
                half skyT = smoothstep(0.0h, 0.7h, half(saturate(reflected.y)));
                half3 skyTint = input.ambientUp * _SkyBrightness;
                skyTint /= max(1.0h, max(skyTint.r, max(skyTint.g, skyTint.b)));
                half3 sky = lerp(_SkyHorizonColor.rgb, _SkyZenithColor.rgb, skyT) * skyTint;
                half ndv = half(saturate(dot(normalWS, float3(viewDirWS))));
                half fresnel = _FresnelBias + (1.0h - _FresnelBias) * pow(max(1.0h - ndv, 1e-4h), _FresnelPower);
                fresnel = saturate(fresnel * _ReflectionStrength);
                color = lerp(color, sky, fresnel);
                alpha = lerp(alpha, 1.0h, fresnel);

                // --- 波の頭の線（2 枚の高さを足した等高線）。遠くで線が詰まったら消す ---
                float height = (rippleA.a + rippleB.a) * 0.5;
                float heightAA = max(fwidth(height), 1e-4);
                half crest = half(saturate(1.0 - abs(height - _CrestThreshold) / max(_CrestWidth, heightAA))
                    * saturate(_CrestWidth / heightAA)) * _CrestColor.a;
                color = lerp(color, _CrestColor.rgb * diffuse, crest);

                // --- 縁の白い線。ゆらぎ（B チャンネル）で幅を揺らす ---
                float foamEdge = _FoamWidth * (1.0 + (rippleA.b * 2.0 - 1.0) * _FoamWobble);
                half foam = half(1.0 - smoothstep(foamEdge - edgeAA, foamEdge + edgeAA, edge)) * _FoamColor.a;
                color = lerp(color, _FoamColor.rgb * diffuse, foam);
                alpha = lerp(alpha, 1.0h, foam);

                // --- 太陽のきらめき。影の中と遠くでは出さない ---
                float3 halfDir = normalize(float3(mainLight.direction) + float3(viewDirWS));
                float sparkle = smoothstep(_SparkleThreshold, _SparkleThreshold + _SparkleSoftness, dot(normalWS, halfDir));
                float cameraDistance = length(GetCameraPositionWS() - input.positionWS);
                sparkle *= saturate((_SparkleFadeDistance - cameraDistance) / (0.5 * _SparkleFadeDistance));
                sparkle *= shadow * _SparkleIntensity;
                color += sun * half(sparkle);
                alpha = max(alpha, half(saturate(sparkle)));

                color = MixFog(color, input.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    // Lit から影や深度のパスを借りない（水は 1 パスで描き、影も落とさない）。
    Fallback Off
}
