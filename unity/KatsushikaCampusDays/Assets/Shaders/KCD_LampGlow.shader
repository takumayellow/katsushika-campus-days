// Katsushika Campus Days — 夜の街灯の明かり (URP 17 / Unity 6)
// 実ライトを使わずに、灯具の下の光だまり・灯具の発光面・まわりの暈を加算合成で描く (#8)。
// メッシュは StreetLampStage が 22 本ぶんを 1 つにまとめる。TEXCOORD0 の z で部品を分ける:
//   0 = 地面の光だまり（xy = 灯具の真下からの水平のずれ ÷ 半径）
//   1 = 灯具の下面の発光面（HDR で Bloom が掛かる明るさ）
//   2 = 暈（xy = 板の四隅。頂点シェーダでカメラへ向け、灯具より手前へ出す）
// 明るさは StreetLights が _Glow（0〜1）で渡す。昼はレンダラーごと止めるので描画コールは夜だけ。
// テクスチャは読まず、ループも無い。霧の中では黒へ寄せて（加算なので）遠くの灯を霧に沈める。
Shader "KCD/LampGlow"
{
    Properties
    {
        _LampColor("Lamp Color", Color) = (1.0, 0.80, 0.52, 1)
        _Glow("Glow (0 = off, 1 = on)", Range(0, 1)) = 1
        _PoolStrength("Pool Strength", Range(0, 1)) = 0.2
        _EmitterStrength("Emitter Strength", Range(0, 8)) = 3
        _HaloStrength("Halo Strength", Range(0, 1)) = 0.3
        _HaloSize("Halo Radius (m)", Range(0, 4)) = 1.1
        _HaloLift("Halo Lift Toward Camera (m)", Range(0, 1)) = 0.4
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _LampColor;
        half  _Glow;
        half  _PoolStrength;
        half  _EmitterStrength;
        half  _HaloStrength;
        float _HaloSize;
        float _HaloLift;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LampGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            // 光だまりは地面から数 cm 浮かせてあるが、遠くで地面と奥行きが競らないよう手前へ寄せる。
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex LampVertex
            #pragma fragment LampFragment
            #pragma target 2.0
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 shape      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 shape      : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LampVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float halo = step(1.5, input.shape.z);
                float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                // 暈はカメラへ向けた板にし、灯具の箱に半分隠れないよう手前（ビュー空間の +z）へ出す。
                positionVS.xy += input.shape.xy * (_HaloSize * halo);
                positionVS.z += _HaloLift * halo;

                output.positionCS = TransformWViewToHClip(positionVS);
                output.shape = input.shape.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 LampFragment(Varyings input) : SV_Target
            {
                float kind = input.shape.z;
                float isEmitter = step(0.5, kind) * (1.0 - step(1.5, kind));
                float isHalo = step(1.5, kind);
                float isPool = 1.0 - step(0.5, kind);

                // 中心で 1、半径で 0 になるなめらかな減衰。xy から画素ごとに求めるので輪郭が多角形にならない。
                float falloff = saturate(1.0 - dot(input.shape.xy, input.shape.xy));
                falloff *= falloff;

                half strength = isPool * _PoolStrength * falloff
                    + isEmitter * _EmitterStrength
                    + isHalo * _HaloStrength * falloff * falloff;
                half3 color = _LampColor.rgb * (strength * _Glow);

                // 加算なので霧の色ではなく黒へ寄せる（霧の奥の灯は霧に沈む）。
                color = MixFogColor(color, half3(0.0, 0.0, 0.0), input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
