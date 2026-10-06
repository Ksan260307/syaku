// しゃくとりの森: アニメ調トゥーンシェーダー（頂点カラー + 2階調 + リム + 輪郭線）
Shader "Shakutori/ToonLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle] _VertexColorSRGB ("Vertex Color is sRGB", Float) = 1

        [Header(Shading)]
        _ShadowTint ("Shadow Tint", Color) = (0.72, 0.7, 0.95, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.05
        _ShadowBrightness ("Shadow Brightness", Range(0, 2)) = 1
        _RimColor ("Rim Color", Color) = (1, 0.97, 0.88, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.35
        _RimWidth ("Rim Width", Range(0, 1)) = 0.28
        _SpecularColor ("Specular Color", Color) = (1,1,1,1)
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0
        _SpecularSize ("Specular Size", Range(0, 0.5)) = 0.05
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)

        [Header(Outline)]
        _OutlineColor ("Outline Color (multiplies albedo)", Color) = (0.35, 0.27, 0.3, 1)
        _OutlineWidth ("Outline Width", Range(0, 2)) = 0.45
        [Toggle] _OutlineSmoothNormals ("Use Smooth Normals (UV3)", Float) = 1

        [Header(Terrain)]
        [Toggle(_TERRAIN)] _Terrain ("Terrain Detail", Float) = 0
        _DetailMap ("Detail Noise", 2D) = "gray" {}
        _DetailScale ("Detail Scale", Float) = 0.35
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.3

        [Header(Wind and Fade)]
        [Toggle(_WIND)] _Wind ("Wind", Float) = 0
        _WindStrength ("Wind Strength", Float) = 0.3
        [Toggle(_NEAR_FADE)] _NearFade ("Fade Near Camera", Float) = 0
        _NearFadeDistance ("Near Fade Distance", Float) = 1.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Skin)]
        [Toggle(_HUE_SHIFT)] _HueShiftOn ("Hue Shift", Float) = 0
        _HueShift ("Hue Shift (deg)", Float) = 0
        _SatMul ("Saturation", Float) = 1
        _ValMul ("Value", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _TERRAIN
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _NEAR_FADE
            #pragma shader_feature_local _HUE_SHIFT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "ToonLitInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float2 uv : TEXCOORD3;
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
                float3 posWS = ShakuDeformWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                ShakuNearFade(i.positionWS, i.positionCS);
                half3 albedo = ShakuAlbedo(i.color, i.uv, i.positionWS);
                float3 N = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                half lit;
                half3 col = ShakuToonLighting(albedo, i.positionWS, N, V, ShakuGetParams(), lit);
                col += _EmissionColor.rgb * (albedo * 0.5h + 0.5h);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _TERRAIN
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _NEAR_FADE
            #pragma shader_feature_local _HUE_SHIFT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "ToonLitInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 smoothNormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 nOS = v.normalOS;
                if (_OutlineSmoothNormals > 0.5 && dot(v.smoothNormal, v.smoothNormal) > 0.01)
                    nOS = v.smoothNormal;
                float3 posWS = ShakuDeformWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                float3 nWS = normalize(TransformObjectToWorldNormal(nOS));
                float dist = distance(posWS, _WorldSpaceCameraPos.xyz);
                float width = _OutlineWidth * 0.0042 * clamp(dist, 0.25, 14.0);
                posWS += nWS * width;
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                ShakuNearFade(i.positionWS, i.positionCS);
                if (_OutlineWidth <= 0.001h) discard;
                half3 albedo = ShakuAlbedo(i.color, i.uv, i.positionWS);
                half3 col = albedo * _OutlineColor.rgb * (_MainLightColor.rgb * 0.5h + 0.5h);
                col += _EmissionColor.rgb * 0.3h;
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "ToonLitInput.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = ShakuDeformWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = ShakuShadowPositionCS(posWS, nWS, _LightDirection, _LightPosition);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _NEAR_FADE
            #pragma multi_compile_instancing

            #include "ToonLitInput.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 posWS = ShakuDeformWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                ShakuNearFade(i.positionWS, i.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
