// しゃくとりの森: 滝（下へ流れ落ちる筋と、滝つぼの白い泡）
Shader "Shakutori/Waterfall"
{
    Properties
    {
        _Color ("Water", Color) = (0.36, 0.62, 0.66, 0.82)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _Speed ("Fall Speed", Float) = 1.4
        _StreakScale ("Streak Scale", Float) = 9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FoamColor;
                half _Speed;
                half _StreakScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fogFactor : TEXCOORD1; };

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

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float s1 = vnoise(float2(i.uv.x * _StreakScale, i.uv.y * 3.0 - t * 2.0));
                float s2 = vnoise(float2(i.uv.x * _StreakScale * 2.3 + 7.0, i.uv.y * 6.0 - t * 3.2));
                half streak = saturate(s1 * 0.6 + s2 * 0.4);
                half3 col = lerp(_Color.rgb, _FoamColor.rgb, smoothstep(0.6h, 0.88h, streak) * 0.85h);   // 水の色の中に、白い筋
                half foam = smoothstep(0.7h, 1.0h, i.uv.y) * (0.6h + 0.4h * vnoise(float2(i.uv.x * 20.0, t * 4.0)));
                col = lerp(col, _FoamColor.rgb, foam);
                half lip = 1.0h - smoothstep(0.0h, 0.08h, i.uv.y);
                col = lerp(col, _FoamColor.rgb, lip * 0.45h);
                col *= _MainLightColor.rgb * 0.45h + 0.7h;
                half a = saturate(_Color.a + streak * 0.25h + foam * 0.5h) * i.color.a;
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
