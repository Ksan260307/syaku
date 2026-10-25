// 地面に落ちる、あかりの光だまり（ランプ・窓・電球のまわりの雪を、あたたかく照らす）。
// 地面にそわせた、うすい円ばん（頂点の色 × 頂点のアルファ＝光の強さ）を、加算で重ねる。霧の中では消える
Shader "Shakutori/GlowPool"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Offset -1, -4

        Pass
        {
            Name "GlowPool"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half fogFactor : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half k = i.color.a * i.color.a;   // まん中ほど強く、ふちは、やわらかく消える
                half3 col = i.color.rgb * k * _Intensity;
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
