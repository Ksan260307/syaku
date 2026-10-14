// しゃくとりの森: 海の水面
//  ・波：沖から波打ちぎわへ、ゆっくりよせてくる。波打ちぎわでは高くなり、ぬれた砂の上を、水がのぼったり下りたりする
//    （UV.y = 波打ちぎわから沖へのきょり、頂点色 R = 波の強さ。小川と干潟の中は 0 で、しずか）
//  ・色：浅い所は砂の底がすけて、エメラルド色。深い所は、こい青（赤い光から先に、水にすわれる）
//  ・波の頭は少し明るく、くだける所（頂点色 B = 波打ちぎわの近さ）にだけ、うすい泡のすじ。岸ぎわのふちにも、細い泡
//  ・映りこみ：低い所から見ても白くならないよう、水平に近い映りこみは、海の青
Shader "Shakutori/ToonSea"
{
    Properties
    {
        _ShallowColor ("Shallow Tint", Color) = (0.42, 0.82, 0.78, 1)
        _DeepColor ("Deep Color", Color) = (0.04, 0.22, 0.38, 1)
        _Absorb ("Absorption (RGB / depth)", Vector) = (1.6, 0.55, 0.38, 0)
        _Clarity ("Clarity", Float) = 1.6
        _ReflectColor ("Reflection Color", Color) = (0.55, 0.74, 0.88, 1)
        _HorizonReflect ("Horizon Reflection Color", Color) = (0.2, 0.42, 0.58, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.5
        _FoamColor ("Foam Color", Color) = (0.93, 0.97, 0.98, 1)
        _FoamDepth ("Foam Depth", Float) = 0.05
        _CrestColor ("Crest Color", Color) = (0.62, 0.9, 0.9, 1)
        _WaveK ("Wave Number", Float) = 0.5
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveHeight ("Wave Height (shore)", Float) = 0.07
        _RippleStrength ("Ripple Strength", Float) = 0.7
        _Refraction ("Refraction", Float) = 0.02
        _SpecStrength ("Specular", Float) = 1.6
        _Glint ("Glint", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Sea"
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
                half4 _HorizonReflect;
                half _FresnelStrength;
                half4 _FoamColor;
                half _FoamDepth;
                half4 _CrestColor;
                float _WaveK;
                float _WaveSpeed;
                float _WaveHeight;
                half _RippleStrength;
                half _Refraction;
                half _SpecStrength;
                half _Glint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float3 wave : TEXCOORD2;     // x = 波の位相, y = 波の傾き（沖向き）, z = 沖へのきょり
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

            float Ripple(float2 p)
            {
                return vnoise(p * 1.7) * 0.6 + vnoise(p * 3.9 + 3.1) * 0.4;
            }

            // 波の位相：沖へのきょり off が小さくなる向き（波打ちぎわの方）へ進む。波の線は、少しうねる
            float WavePhase(float off, float3 ws)
            {
                return off * _WaveK + _Time.y * _WaveSpeed + 0.6 * sin(ws.x * 0.045 + _Time.y * 0.07);
            }

            // 波の高さ：波打ちぎわでは高く、沖ではゆるいうねり。遠くの格子のはしでは 0（沖の海とつながる）
            float WaveAmp(float off, float3 ws, float strength)
            {
                float fade = saturate((100.0 - max(abs(ws.x), -ws.z)) / 12.0);
                return strength * fade * _WaveHeight * lerp(1.0, 0.35, saturate(off / 25.0));
            }

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
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float off = v.uv.y;
                float ph = WavePhase(off, ws);
                float amp = WaveAmp(off, ws, v.color.r);
                ws.y += amp * sin(ph);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.wave = float3(ph, amp * _WaveK * cos(ph), off);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.positionWS.xz;
                // さざ波（ゆっくり流れる 2 つの模様）と、よせる波の傾き（沖向き = -z のあたり）
                float2 q0 = p * 0.55 + float2(t * 0.05, t * 0.11);
                float2 q1 = p * 0.55 * 1.37 + float2(-t * 0.07, t * 0.04) + 7.3;
                float e = 0.05;
                float h0 = Ripple(q0) + Ripple(q1);
                float hx = Ripple(q0 + float2(e, 0)) + Ripple(q1 + float2(e, 0));
                float hz = Ripple(q0 + float2(0, e)) + Ripple(q1 + float2(0, e));
                half rough = _RippleStrength * (0.5h + 0.6h * i.color.r);
                float3 N = normalize(float3(-(hx - h0) / e * rough * 0.06, 1.0, -(hz - h0) / e * rough * 0.06 + i.wave.y));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);

                // ---- 水の深さと、底の見え方（屈折） ----
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float depth = WaterDepth(suv, i.positionWS);
                float2 ruv = suv + N.xz * _Refraction * saturate(depth * 2.0);
                float rdepth = WaterDepth(ruv, i.positionWS);
                if (rdepth < 0.0) { ruv = suv; rdepth = depth; }
                half3 bed = SampleSceneColor(ruv);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half shadow = lerp(0.65h, 1.0h, light.shadowAttenuation);
                half3 lightK = (_MainLightColor.rgb * 0.6h + 0.4h) * shadow;

                // 水を通る光は、深いほどすわれる（赤から先に）→ 浅い所はエメラルド、深い所はこい青
                float path = max(rdepth, 0.0) / max(abs(V.y), 0.35) / max(_Clarity, 0.05);
                half3 trans = exp(-_Absorb.rgb * path);
                half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), _ShallowColor.rgb * 1.2h, saturate(path * 2.5));
                half3 col = bed * trans * tint + _DeepColor.rgb * lightK * (1.0h - trans);

                // ---- 波の頭は、少し明るく（光がすけて、明るい青緑） ----
                half crest = smoothstep(0.55h, 1.0h, sin(i.wave.x)) * i.color.r * (0.4h + 0.6h * vnoise(p * 0.35 + 2.0));
                col = lerp(col, _CrestColor.rgb * lightK, crest * 0.08h);

                // ---- 空の映りこみ・日の光のきらめき ----
                half fres = pow(1.0h - saturate(dot(N, V)), 4.0h);
                float3 R = reflect(-V, N);
                half3 env = lerp(_HorizonReflect.rgb, _ReflectColor.rgb, smoothstep(0.02h, 0.6h, R.y));
                col = lerp(col, env * (_MainLightColor.rgb * 0.5h + 0.5h), fres * _FresnelStrength);
                half3 H = normalize(light.direction + V);
                col += smoothstep(0.985h, 0.995h, saturate(dot(N, H))) * _SpecStrength * light.color * light.shadowAttenuation;
                if (_Glint > 0.0)
                {
                    float g = vnoise(p * 6.0 + float2(t * 0.7, -t * 0.5)) * vnoise(p * 9.5 - t * 0.9);
                    // 遠くでは、きらめきの点が画面の点より小さくなって、海が白っぽく見えるので、近くだけ
                    half nearK = saturate((36.0 - distance(_WorldSpaceCameraPos.xyz, i.positionWS)) / 24.0);
                    col += smoothstep(0.6h, 0.82h, g) * _Glint * nearK * (0.4h + fres) * light.color * light.shadowAttenuation;
                }

                // ---- 泡：波がくだける所（波打ちぎわの近く）の、うすい泡のすじと、岸ぎわのふちの細い泡（全部は白くしない） ----
                half b = i.color.b;
                half breakK = b * b * b * smoothstep(0.9h, 0.99h, sin(i.wave.x + 0.5));
                half lace = smoothstep(0.45h, 0.8h, vnoise(p * 2.2 + float2(0.0, t * 0.3)) * 0.7h + vnoise(p * 5.0 - t * 0.2) * 0.5h);
                half edge = 1.0h - saturate(depth / max(_FoamDepth, 1e-3));
                half shore = step(0.65h, edge * (0.6h + 0.5h * lace)) * i.color.r;
                half foam = saturate(breakK * lace * 0.8h + shore * 0.6h);
                col = lerp(col, _FoamColor.rgb * (_MainLightColor.rgb * 0.45h + 0.55h), foam * 0.45h);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
