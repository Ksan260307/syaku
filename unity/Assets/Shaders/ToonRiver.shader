// しゃくとりの森: 流れる川の水面
//  ・水の深さ（水面から川底までのたての深さ）で色が変わる：浅い所は川底が緑がかって見え、深い所は深い青緑
//    （赤い光から先に水にすわれる）
//  ・流れ：頂点色 R = 流れの速さ（まん中は速く、岸ぎわ・よどみはゆっくり）。2 つの位相をまぜて、模様がのびない
//  ・流れにそった細い筋と、さざ波。岸ぎわ・石のまわり・滝の下（頂点色 G = 白くあわ立つ所）は白い泡
Shader "Shakutori/ToonRiver"
{
    Properties
    {
        _ShallowColor ("Shallow Tint", Color) = (0.42, 0.70, 0.58, 1)
        _DeepColor ("Deep Color", Color) = (0.05, 0.26, 0.30, 1)
        _Absorb ("Absorption (RGB / depth)", Vector) = (2.4, 1.0, 0.75, 0)
        _Clarity ("Clarity", Float) = 1.8
        _ReflectColor ("Reflection Color", Color) = (0.58, 0.74, 0.82, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.38
        _FoamColor ("Foam Color", Color) = (0.93, 0.98, 1, 1)
        _FoamDepth ("Foam Depth", Float) = 0.12
        _StreakColor ("Streak Color", Color) = (0.72, 0.9, 0.9, 1)
        _FlowSpeed ("Flow Speed", Float) = 0.55
        _StreakScale ("Streak Scale", Float) = 6
        _RippleStrength ("Ripple Strength", Float) = 0.6
        _Refraction ("Refraction", Float) = 0.025
        _SpecStrength ("Specular", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "River"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "ToonCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _Absorb;
                half _Clarity;
                half4 _ReflectColor;
                half _FresnelStrength;
                half4 _FoamColor;
                half _FoamDepth;
                half4 _StreakColor;
                half _FlowSpeed;
                half _StreakScale;
                half _RippleStrength;
                half _Refraction;
                half _SpecStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half4 color : COLOR;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            // さざ波の高さ（流れの向き = v にのびた形）
            float Ripple(float2 uv)
            {
                return vnoise(float2(uv.x * 11.0, uv.y * 2.6)) * 0.6 + vnoise(float2(uv.x * 23.0 + 3.1, uv.y * 5.5)) * 0.4;
            }

            // 流れにそった細い筋（明るい筋）
            float Streak(float2 uv)
            {
                float a = vnoise(float2(uv.x * _StreakScale * 14.0, uv.y * 0.9));
                float b = vnoise(float2(uv.x * _StreakScale * 29.0 + 5.0, uv.y * 1.7));
                return smoothstep(0.8, 0.93, a * 0.65 + b * 0.35);
            }

            // 川底のたての深さ（水面から、その画面の点の川底まで）
            float WaterDepth(float2 suv, float3 surfWS)
            {
                #if UNITY_REVERSED_Z
                    float raw = SampleSceneDepth(suv);
                #else
                    float raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(suv));
                #endif
                float3 bed = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                return surfWS.y - bed.y;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // ---- 流れ：2 つの位相の模様をまぜる（速さがちがう所でも模様がのびない） ----
                float speed = _FlowSpeed * lerp(0.3, 1.3, i.color.r);
                float t = _Time.y * 0.5;
                float ph0 = frac(t), ph1 = frac(t + 0.5);
                half w0 = 1.0h - abs(1.0h - 2.0h * ph0);
                half w1 = 1.0h - w0;
                float2 uv0 = i.uv + float2(0.0, -ph0 * speed * 2.0);
                float2 uv1 = i.uv + float2(0.37, -ph1 * speed * 2.0);
                float e = 0.004;
                float h0 = Ripple(uv0) * w0 + Ripple(uv1) * w1;
                float hx = Ripple(uv0 + float2(e, 0)) * w0 + Ripple(uv1 + float2(e, 0)) * w1;
                float hz = Ripple(uv0 + float2(0, e)) * w0 + Ripple(uv1 + float2(0, e)) * w1;
                half rough = _RippleStrength * (0.6h + 0.8h * i.color.r + 1.2h * i.color.g);   // 速い所・あわ立つ所は波立つ
                float3 N = normalize(float3(-(hx - h0) / e * rough * 0.01, 1.0, (hz - h0) / e * rough * 0.01));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);

                // ---- 水の深さと、川底の見え方（屈折） ----
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float depth = WaterDepth(suv, i.positionWS);
                float2 ruv = suv + N.xz * _Refraction * saturate(depth * 2.0);
                float rdepth = WaterDepth(ruv, i.positionWS);
                if (rdepth < 0.0) { ruv = suv; rdepth = depth; }
                half3 bed = SampleSceneColor(ruv);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half shadow = lerp(0.6h, 1.0h, light.shadowAttenuation);
                half3 lightK = (_MainLightColor.rgb * 0.6h + 0.4h) * shadow;

                // 水を通る光は、深いほどすわれる（赤から先に）→ 浅い所は川底が緑がかって見え、深い所は水の色
                float path = max(rdepth, 0.0) / max(abs(V.y), 0.4) / max(_Clarity, 0.05);   // ななめに見るほど、水の中を長く通る
                half3 trans = exp(-_Absorb.rgb * path);
                half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), _ShallowColor.rgb * 1.25h, saturate(path * 3.0));
                half3 col = bed * trans * tint + _DeepColor.rgb * lightK * (1.0h - trans);

                // ---- 空の映りこみ・日の光のきらめき ----
                half fres = pow(1.0h - saturate(dot(N, V)), 4.0h);
                col = lerp(col, _ReflectColor.rgb * (_MainLightColor.rgb * 0.5h + 0.5h), fres * _FresnelStrength);
                half3 H = normalize(light.direction + V);
                col += smoothstep(0.985h, 0.995h, saturate(dot(N, H))) * _SpecStrength * light.color * light.shadowAttenuation;

                // ---- 流れにそった細い筋 ----
                half streak = Streak(uv0) * w0 + Streak(uv1) * w1;
                col = lerp(col, _StreakColor.rgb * lightK, streak * (0.12h + 0.2h * i.color.r));

                // ---- 白い泡：岸ぎわ・石のまわり（とても浅い所）と、滝の下 ----
                half edge = 1.0h - saturate(depth / _FoamDepth);
                half band = sin(depth * 60.0 - _Time.y * 2.5 + h0 * 3.0) * 0.5h + 0.5h;
                half shore = saturate(step(0.55h, edge * (0.6h + band * 0.6h)) + step(0.9h, edge));
                float bub = vnoise(float2(uv0.x * 30.0, uv0.y * 9.0)) * w0 + vnoise(float2(uv1.x * 30.0, uv1.y * 9.0)) * w1;
                half white = smoothstep(0.25h, 0.75h, i.color.g * (0.55h + bub * 0.9h));
                half foam = saturate(shore * 0.8h + white);
                col = lerp(col, _FoamColor.rgb * (_MainLightColor.rgb * 0.5h + 0.55h), foam * 0.85h);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
