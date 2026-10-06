// しゃくとりの森: 森のしずく（ガラスのような水滴・内側から光る）
Shader "Shakutori/Dewdrop"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.92, 1.0, 0.45)
        [HDR] _GlowColor ("Glow", Color) = (0.6, 1.6, 2.0, 1)
        _RimColor ("Rim", Color) = (1, 1, 1, 1)
        _Pulse ("Pulse Speed", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _GlowColor;
                half4 _RimColor;
                half _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                Light light = GetMainLight();
                half fres = 1.0h - saturate(dot(N, V));
                half pulse = 0.75h + 0.25h * sin(_Time.y * _Pulse + i.positionWS.x + i.positionWS.z);
                half3 col = _Color.rgb * (0.55h + 0.45h * N.y);
                col += _RimColor.rgb * smoothstep(0.55h, 0.85h, fres) * 0.8h;
                half3 R = reflect(-V, N);
                half spec = smoothstep(0.93h, 0.95h, dot(R, light.direction));
                half spec2 = smoothstep(0.96h, 0.975h, dot(R, normalize(float3(-0.4, 0.8, 0.3))));
                col += (spec + spec2 * 0.7h) * 1.6h;
                col += _GlowColor.rgb * pulse * (0.35h + 0.65h * (1.0h - fres) * (1.0h - fres));
                half a = saturate(_Color.a + fres * 0.5h + spec + spec2);
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
