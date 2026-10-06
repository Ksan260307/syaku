// しゃくとりの森: 流れる川の水面（UV の v 方向へ流れる波・泡の筋・岸の泡・屈折）
Shader "Shakutori/ToonRiver"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.62, 0.92, 0.9, 0.3)
        _DeepColor ("Deep Color", Color) = (0.14, 0.46, 0.58, 0.9)
        _DepthRange ("Depth Range", Float) = 2.4
        _ReflectColor ("Reflection Color", Color) = (0.82, 0.94, 1.0, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.5
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDepth ("Foam Depth", Float) = 0.4
        _FlowSpeed ("Flow Speed", Float) = 0.35
        _StreakScale ("Streak Scale", Float) = 6
        _RippleStrength ("Ripple Strength", Float) = 0.45
        _Refraction ("Refraction", Float) = 0.03
        _SpecStrength ("Specular", Float) = 1.5
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
                half _DepthRange;
                half4 _ReflectColor;
                half _FresnelStrength;
                half4 _FoamColor;
                half _FoamDepth;
                half _FlowSpeed;
                half _StreakScale;
                half _RippleStrength;
                half _Refraction;
                half _SpecStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
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

            float Ripple(float2 uv, float t)
            {
                float2 a = float2(uv.x * 9.0, uv.y * 3.0 - t * 1.2);
                float2 b = float2(uv.x * 17.0 + 3.1, uv.y * 6.0 - t * 1.9);
                return vnoise(a) * 0.65 + vnoise(b) * 0.35;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 uv = i.uv;
                float e = 0.004;
                float h0 = Ripple(uv, t * 4.0);
                float hx = Ripple(uv + float2(e, 0), t * 4.0);
                float hz = Ripple(uv + float2(0, e), t * 4.0);
                float3 N = normalize(float3(-(hx - h0) / e * _RippleStrength * 0.01, 1.0, (hz - h0) / e * _RippleStrength * 0.01));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float surfDepth = i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float depthDiff = sceneDepth - surfDepth;
                float2 ruv = suv + N.xz * _Refraction * saturate(depthDiff);
                float rDepth = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams) - surfDepth;
                if (rDepth < 0.0) { ruv = suv; rDepth = depthDiff; }
                half3 scene = SampleSceneColor(ruv);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half shadow = lerp(0.6h, 1.0h, light.shadowAttenuation);

                half dt = saturate(rDepth / _DepthRange);
                half3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, dt) * (_MainLightColor.rgb * 0.6h + 0.4h) * shadow;
                half alpha = lerp(_ShallowColor.a, _DeepColor.a, dt);
                half3 col = lerp(scene, waterCol, alpha);

                half fres = pow(1.0h - saturate(dot(N, V)), 4.0h);
                col = lerp(col, _ReflectColor.rgb * (_MainLightColor.rgb * 0.5h + 0.5h), fres * _FresnelStrength);
                half3 H = normalize(light.direction + V);
                col += smoothstep(0.99h, 0.995h, saturate(dot(N, H))) * _SpecStrength * light.color * light.shadowAttenuation;

                // 流れの筋
                float streak = vnoise(float2(uv.x * _StreakScale * 3.0, uv.y * 2.0 - t * 6.0));
                float streak2 = vnoise(float2(uv.x * _StreakScale * 7.0 + 5.0, uv.y * 4.0 - t * 9.0));
                half lines = step(0.78h, streak * 0.6h + streak2 * 0.4h);
                col = lerp(col, _FoamColor.rgb, lines * 0.35h * (1.0h - dt * 0.5h));

                // 岸の泡
                half edge = 1.0h - saturate(depthDiff / _FoamDepth);
                half band = sin(depthDiff * 22.0 - _Time.y * 2.5 + h0 * 3.0) * 0.5h + 0.5h;
                half foam = saturate(step(0.55h, edge * (0.6h + band * 0.6h)) + step(0.85h, edge));
                col = lerp(col, _FoamColor.rgb * (_MainLightColor.rgb * 0.5h + 0.6h), foam * 0.85h);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
