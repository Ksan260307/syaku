// しゃくとりの森: 光の筋（木漏れ日のビーム）
Shader "Shakutori/LightShaft"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.93, 0.72, 1)
        _Intensity ("Intensity", Float) = 0.28
        _FadeNear ("Fade Near", Float) = 4
        _EdgePower ("Edge Softness", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _FadeNear;
                half _EdgePower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                half facing = pow(abs(dot(normalize(i.normalWS), V)), _EdgePower);
                half along = smoothstep(0.0h, 0.25h, i.uv.y) * (1.0h - smoothstep(0.55h, 1.0h, i.uv.y));
                half flicker = 0.75h + 0.25h * sin(_Time.y * 0.8 + i.uv.x * 12.0 + i.positionWS.x * 0.2);
                half streak = 0.7h + 0.3h * sin(i.uv.x * 40.0 + _Time.y * 0.3);
                float dist = distance(_WorldSpaceCameraPos.xyz, i.positionWS);
                half camFade = saturate((dist - _FadeNear * 0.5) / _FadeNear);
                half3 col = _Color.rgb * _Intensity * facing * along * flicker * streak * camFade;
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
