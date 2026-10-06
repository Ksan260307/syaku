// しゃくとりの森: 水たまりの水面（屈折・深さによる色・岸の泡・トゥーンな照り返し）
Shader "Shakutori/ToonWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.55, 0.9, 0.85, 0.35)
        _DeepColor ("Deep Color", Color) = (0.12, 0.42, 0.55, 0.92)
        _DepthRange ("Depth Range", Float) = 2.2
        _ReflectColor ("Reflection Color", Color) = (0.8, 0.93, 1.0, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.55
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDepth ("Foam Depth", Float) = 0.35
        _RippleScale ("Ripple Scale", Float) = 0.45
        _RippleSpeed ("Ripple Speed", Float) = 0.35
        _RippleStrength ("Ripple Strength", Float) = 0.35
        _Refraction ("Refraction", Float) = 0.025
        _SpecStrength ("Specular", Float) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Water"
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
                half _RippleScale;
                half _RippleSpeed;
                half _RippleStrength;
                half _Refraction;
                half _SpecStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            float RippleHeight(float2 p, float t)
            {
                float h = sin(p.x * 1.7 + t * 1.1) * 0.5 + sin(p.y * 2.3 - t * 0.9) * 0.5;
                h += sin((p.x + p.y) * 3.1 + t * 1.7) * 0.3 + sin((p.x - p.y * 0.7) * 5.3 - t * 2.3) * 0.15;
                return h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _RippleSpeed;
                float2 p = i.positionWS.xz * _RippleScale;
                float e = 0.05;
                float h0 = RippleHeight(p, t);
                float hx = RippleHeight(p + float2(e, 0), t);
                float hz = RippleHeight(p + float2(0, e), t);
                float3 N = normalize(float3(-(hx - h0) / e * _RippleStrength * 0.1, 1.0, -(hz - h0) / e * _RippleStrength * 0.1));
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
                half spec = smoothstep(0.992h, 0.996h, saturate(dot(N, H)));
                col += spec * _SpecStrength * light.color * light.shadowAttenuation;

                // 岸辺の泡（にじむ帯）
                half edge = 1.0h - saturate(depthDiff / _FoamDepth);
                half band = sin(depthDiff * 25.0 - _Time.y * 2.0 + h0 * 2.0) * 0.5h + 0.5h;
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
