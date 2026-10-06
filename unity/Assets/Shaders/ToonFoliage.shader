// しゃくとりの森: 草花用シェーダー（両面・風・かき分け・透過光・カメラ付近ディザ）
Shader "Shakutori/ToonFoliage"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Toggle] _VertexColorSRGB ("Vertex Color is sRGB", Float) = 1
        _ShadowTint ("Shadow Tint", Color) = (0.7, 0.78, 0.92, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.12
        _ShadowBrightness ("Shadow Brightness", Range(0, 2)) = 1.05
        _NormalUp ("Normal Toward Up", Range(0, 1)) = 0.55
        _TranslucencyColor ("Translucency", Color) = (0.85, 1.0, 0.45, 1)
        _TranslucencyStrength ("Translucency Strength", Range(0, 2)) = 0.5
        _Variation ("Per-instance Color Variation", Range(0, 0.5)) = 0.12
        _TipBrightness ("Tip Brightness", Range(0, 1)) = 0.2
        _WindStrength ("Wind Strength", Float) = 0.4
        _NearFadeDistance ("Near Fade Distance", Float) = 1.2
        _FarFadeStart ("Far Fade Start", Float) = 45
        _FarFadeEnd ("Far Fade End", Float) = 60
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "ToonCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _VertexColorSRGB;
            half4 _ShadowTint;
            half _ShadowThreshold;
            half _ShadowSoftness;
            half _ShadowBrightness;
            half _NormalUp;
            half4 _TranslucencyColor;
            half _TranslucencyStrength;
            half _Variation;
            half _TipBrightness;
            half _WindStrength;
            half _NearFadeDistance;
            half _FarFadeStart;
            half _FarFadeEnd;
        CBUFFER_END

        float3 FoliageDeform(float3 positionOS, half weight, out float3 objectPos)
        {
            objectPos = GetObjectToWorldMatrix()._m03_m13_m23;
            float3 posWS = TransformObjectToWorld(positionOS);
            float phase = dot(objectPos, float3(0.371, 0.113, 0.719));
            return ShakuApplyWind(posWS, weight, _WindStrength, phase);
        }

        void FoliageFade(float3 positionWS, float4 positionCS)
        {
            float dist = distance(positionWS, _WorldSpaceCameraPos.xyz);
            float nearFade = saturate((dist - _NearFadeDistance * 0.35) / max(_NearFadeDistance, 0.001));
            float farFade = 1.0 - saturate((dist - _FarFadeStart) / max(_FarFadeEnd - _FarFadeStart, 0.001));
            clip(min(nearFade, farFade) - ShakuBayer4(positionCS.xy));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardFoliage"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half3 variation : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 objPos;
                float3 posWS = FoliageDeform(v.positionOS.xyz, v.color.a, objPos);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                float h = frac(sin(dot(objPos.xz, float2(12.9898, 78.233))) * 43758.5453);
                float h2 = frac(h * 7.13 + 0.37);
                o.variation = half3(1.0 + (h - 0.5) * _Variation * 2.0, 1.0 + (h2 - 0.5) * _Variation, 1.0 - (h - 0.5) * _Variation);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                FoliageFade(i.positionWS, i.positionCS);
                half3 albedo = _BaseColor.rgb * ShakuVertexColor(i.color.rgb, _VertexColorSRGB) * i.variation;
                albedo *= 1.0h + i.color.a * _TipBrightness;
                float3 N = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                N = normalize(lerp(N, float3(0, 1, 0), _NormalUp));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);

                ShakuToonParams p;
                p.shadowTint = _ShadowTint.rgb;
                p.threshold = _ShadowThreshold;
                p.softness = _ShadowSoftness;
                p.shadowBrightness = _ShadowBrightness;
                p.rimColor = half3(1, 1, 0.9);
                p.rimStrength = 0.0h;
                p.rimWidth = 0.2h;
                p.specColor = half3(1, 1, 1);
                p.specStrength = 0.0h;
                p.specSize = 0.05h;
                half lit;
                half3 col = ShakuToonLighting(albedo, i.positionWS, N, V, p, lit);

                // 逆光で葉が透ける
                Light mainLight = GetMainLight();
                half back = pow(saturate(dot(V, -mainLight.direction)), 3.0h);
                col += albedo * _TranslucencyColor.rgb * back * _TranslucencyStrength * (0.4h + 0.6h * i.color.a) * mainLight.color;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 objPos;
                float3 posWS = FoliageDeform(v.positionOS.xyz, v.color.a, objPos);
                o.positionCS = ShakuShadowPositionCS(posWS, TransformObjectToWorldNormal(v.normalOS), _LightDirection, _LightPosition);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 objPos;
                float3 posWS = FoliageDeform(v.positionOS.xyz, v.color.a, objPos);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                FoliageFade(i.positionWS, i.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
