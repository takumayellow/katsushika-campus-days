// Katsushika Campus Days — 時刻で色が変わるトゥーン調の空 (URP 17 / Unity 6)
// 天頂〜地平線のグラデーション、太陽のまわりの色、太陽と月の円盤、星、軽い雲、遠くの街並みのシルエット (#8)。
// テクスチャは読まず、ループも無い（WebGL2 でも 1 画素あたり百数十命令）。色は DayNightCycle が SkyPalette から毎フレーム渡す。
//
// 地平線より下は _HorizonColor の一色にする。DayNightCycle が霧の色を同じ値にそろえるので、霧に溶けた地面と、
// カメラの遠クリップの先に見える空の下半分とが継ぎ目なくつながる（上空から見たときの灰色の帯 #40）。
Shader "KCD/Sky"
{
    Properties
    {
        _ZenithColor("Zenith Color", Color) = (0.26, 0.53, 0.90, 1)
        _HorizonColor("Horizon Color", Color) = (0.70, 0.83, 0.95, 1)
        _SunGlowColor("Sun Glow Color (a = strength)", Color) = (1, 0.96, 0.88, 0.45)
        _CloudColor("Cloud Color (a = opacity)", Color) = (1, 1, 1, 0.9)
        _SkylineColor("Skyline Color", Color) = (0.54, 0.66, 0.81, 1)
        _SunDirection("Sun Direction", Vector) = (0.3, 0.6, -0.74, 0)
        _MoonDirection("Moon Direction (w = visibility)", Vector) = (-0.3, 0.6, 0.74, 0)
        _Stars("Stars", Range(0, 1)) = 0
        _SunDisk("Sun Disk Intensity", Range(0, 8)) = 3
        _CloudCover("Cloud Cover", Range(0, 1)) = 0.42
        _CloudSpeed("Cloud Speed", Float) = 0.006
        _SkylineHeight("Skyline Height", Range(0, 0.1)) = 0.03
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4  _ZenithColor;
        half4  _HorizonColor;
        half4  _SunGlowColor;
        half4  _CloudColor;
        half4  _SkylineColor;
        float4 _SunDirection;
        float4 _MoonDirection;
        half   _Stars;
        half   _SunDisk;
        half   _CloudCover;
        float  _CloudSpeed;
        float  _SkylineHeight;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Sky"

            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex SkyVertex
            #pragma fragment SkyFragment
            #pragma target 3.0

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // sin を使わないハッシュ（Dave Hoskins）。WebGL の GPU でも精度で縞が出にくい。
            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i);
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings SkyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // スカイボックスのメッシュはカメラ中心・ワールド軸そろえで描かれるので、頂点位置がそのまま視線の向き。
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            // 天頂〜地平線のグラデーションと太陽のまわりの色。glowWide は雲の照り返しにも使う。
            half3 SkyGradient(float3 dir, float up, float3 sunDir, out float glowWide)
            {
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(max(up, 1e-4), 0.45));

                float sunDot = saturate(dot(dir, sunDir));
                glowWide = pow(sunDot, 6.0);
                float glowCore = pow(sunDot, 48.0);

                // 太陽の側の地平線ほど染まる帯。地平線ちょうど (up = 0) では 0 にして、霧の色との境目を出さない。
                float2 flatDir = dir.xz / max(length(dir.xz), 1e-4);
                float2 flatSun = sunDir.xz / max(length(sunDir.xz), 1e-4);
                float facing = saturate(dot(flatDir, flatSun) * 0.5 + 0.5);
                float band = pow(1.0 - up, 5.0) * facing * facing * smoothstep(0.0, 0.08, up);

                float glow = saturate(glowWide * 0.55 + glowCore * 0.6 + band * 0.8) * _SunGlowColor.a;
                return lerp(color, _SunGlowColor.rgb, glow);
            }

            // 格子ごとに 1 つまでの星。地平線ぎわは消し、ゆっくり瞬かせる。
            float Stars(float3 dir, float up)
            {
                float3 p = dir * 110.0;
                float3 cell = floor(p);
                float3 local = frac(p) - 0.5;
                float rnd = Hash13(cell);
                float star = step(0.965, rnd) * (1.0 - smoothstep(0.06, 0.16, length(local)));
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + rnd * 3.0) + rnd * 40.0);
                return star * twinkle * smoothstep(0.03, 0.25, up) * _Stars;
            }

            // 2 オクターブの値ノイズを段で切ったトゥーン調の雲。地平線に向かって薄くする。
            half3 Clouds(half3 color, float3 dir, float up, float glowWide)
            {
                float2 uv = dir.xz / (up + 0.18) * 1.4 + _Time.y * _CloudSpeed * float2(1.0, 0.35);
                float n = ValueNoise(uv) * 0.65 + ValueNoise(uv * 2.3 + 7.3) * 0.35;
                float threshold = 1.0 - _CloudCover;
                float cover = smoothstep(threshold, threshold + 0.05, n) * smoothstep(0.03, 0.22, up) * _CloudColor.a;

                // 雲の縁は少し暗く、芯は明るく。太陽の側は太陽のまわりの色で照らす。
                float core = smoothstep(threshold + 0.05, threshold + 0.22, n);
                half3 cloud = _CloudColor.rgb * lerp(0.82, 1.0, core);
                cloud = lerp(cloud, _SunGlowColor.rgb, saturate(glowWide * 0.6 * _SunGlowColor.a));
                return lerp(color, cloud, cover);
            }

            // 遠くの街並み。方位角を約 1.6° ずつの建物に割り、高さを乱数と地区ごとのうねりで決める。夜は窓が灯る。
            // edge は fwidth(up)。分岐の前に求めて渡す（分岐の中の微分は画素ごとに不定になる）。
            half3 Skyline(half3 color, float3 dir, float up, float edge)
            {
                float azimuth = atan2(dir.x, dir.z) * INV_TWO_PI + 0.5;
                float a = azimuth * 220.0;
                float building = floor(a);
                float height = (0.25 + 0.75 * Hash12(float2(building, 3.7)))
                    * (0.35 + 0.65 * ValueNoise(float2(a * 0.045, 1.3)));
                height += step(0.93, Hash12(float2(building, 9.1))) * 0.6;
                float top = _SkylineHeight * height + 0.003;

                float inside = 1.0 - smoothstep(top - edge, top + edge, up);

                float2 windowUV = float2(a * 4.0, up / max(_SkylineHeight, 1e-3) * 16.0);
                float2 windowCell = floor(windowUV);
                float2 windowLocal = frac(windowUV);
                float lit = step(0.78, Hash12(windowCell + 0.5))
                    * step(0.25, windowLocal.x) * step(windowLocal.x, 0.75)
                    * step(0.30, windowLocal.y) * step(windowLocal.y, 0.80)
                    * step(up, top - 0.002);
                half3 city = _SkylineColor.rgb + half3(1.0, 0.80, 0.50) * (lit * _Stars * 0.9);
                return lerp(color, city, inside);
            }

            half4 SkyFragment(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.directionOS);
                float up = dir.y;
                float edge = fwidth(up) + 1e-5;

                // 地平線より下は霧と同じ色の一色。
                if (up < 0.0)
                {
                    return half4(_HorizonColor.rgb, 1.0);
                }

                float3 sunDir = normalize(_SunDirection.xyz);
                float glowWide;
                half3 color = SkyGradient(dir, up, sunDir, glowWide);

                // 月（淡い暈つき）と星は雲の後ろ。
                float3 moonDir = normalize(_MoonDirection.xyz);
                float moonDot = dot(dir, moonDir);
                float moon = smoothstep(0.99965, 0.99980, moonDot) * _MoonDirection.w;
                float halo = pow(saturate(moonDot), 300.0) * 0.25 * _MoonDirection.w;
                color += half3(0.75, 0.80, 0.95) * halo;
                color = lerp(color, half3(1.05, 1.05, 1.15), moon);
                color += Stars(dir, up) * half3(1.0, 0.97, 0.90);

                // 太陽の円盤（約 1.5°）。HDR で Bloom が掛かる明るさにする。
                float disk = smoothstep(0.99955, 0.99970, dot(dir, sunDir));
                color = lerp(color, lerp(_SunGlowColor.rgb, half3(1.0, 1.0, 1.0), 0.6) * _SunDisk, disk);

                color = Clouds(color, dir, up, glowWide);
                color = Skyline(color, dir, up, edge);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
