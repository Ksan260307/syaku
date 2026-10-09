// しゃくとりの森: 空（樹冠の葉の隙間から見える空・太陽・地平線のもや）
Shader "Shakutori/ForestSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.42, 0.7, 0.93, 1)
        _HorizonColor ("Horizon", Color) = (0.82, 0.93, 0.92, 1)
        _SunColor ("Sun", Color) = (1.0, 0.95, 0.8, 1)
        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.03
        _CanopyColor ("Canopy Dark", Color) = (0.13, 0.28, 0.16, 1)
        _CanopyLightColor ("Canopy Lit", Color) = (0.55, 0.78, 0.32, 1)
        _CanopyCoverage ("Canopy Coverage", Range(0, 1)) = 0.52
        _CanopyScale ("Canopy Scale", Float) = 2.2
        _CanopyStart ("Canopy Start Height", Range(0, 1)) = 0.12
        _HazeStrength ("Horizon Haze", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _SunColor;
                half _SunSize;
                half4 _CanopyColor;
                half4 _CanopyLightColor;
                half _CanopyCoverage;
                half _CanopyScale;
                half _CanopyStart;
                half _HazeStrength;
            CBUFFER_END
            // 0 = 樹冠の葉の隙間から見る空（森・川辺・公園）、1 = ひらけた空と雲（山）。エリアごとに AreaAtmosphere が全体へ設定する
            float _SkyOpen;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash2(i).x;
                float b = hash2(i + float2(1, 0)).x;
                float c = hash2(i + float2(0, 1)).x;
                float d = hash2(i + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += vnoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // 葉のかたまり（ボロノイの丸いセル）
            float leaves(float2 p, out float edge)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float md = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = hash2(i + g);
                    float2 r = g + o - f;
                    md = min(md, dot(r, r));
                }
                float d = sqrt(md);
                edge = d;
                return d;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(h), 0.55));

                float3 L = _MainLightPosition.xyz;
                float sd = dot(d, L);
                sky += _SunColor.rgb * (pow(saturate(sd), 48.0) * 0.6 + smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, sd) * 2.5);

                // 樹冠
                float3 col = sky;
                if (h > 0.0)
                {
                    float2 p = d.xz / (h + 0.18) * _CanopyScale;
                    p += float2(sin(_Time.y * 0.05), cos(_Time.y * 0.04)) * 0.05;
                    float big = fbm(p * 0.35);
                    float edge;
                    float cell = leaves(p * 2.5, edge);
                    float small = vnoise(p * 9.0);
                    float density = big * 1.15 + (0.55 - cell) * 0.45 + (small - 0.5) * 0.12;
                    float cov = lerp(_CanopyCoverage - 0.25, _CanopyCoverage + 0.18, smoothstep(_CanopyStart, 0.9, h));
                    float m = smoothstep(1.0 - cov - 0.03, 1.0 - cov + 0.03, density);
                    m *= smoothstep(_CanopyStart - 0.05, _CanopyStart + 0.15, h);
                    m *= 1.0 - _SkyOpen;
                    float rim = smoothstep(1.0 - cov - 0.03, 1.0 - cov + 0.12, density);
                    half3 leafCol = lerp(_CanopyLightColor.rgb, _CanopyColor.rgb, saturate(rim * 1.2 - 0.1));
                    leafCol += _CanopyLightColor.rgb * pow(saturate(sd), 6.0) * 0.6 * (1.0 - rim);
                    leafCol *= 0.85 + small * 0.3;
                    col = lerp(col, leafCol, m);
                    // ひらけた空：ゆっくり流れる、白い雲
                    if (_SkyOpen > 0.001)
                    {
                        float2 q = d.xz / (h + 0.25) * 0.9 + float2(_Time.y * 0.004, _Time.y * 0.0025);
                        float cl = fbm(q);
                        float cm = smoothstep(0.5, 0.7, cl) * smoothstep(0.02, 0.22, h);
                        half3 cloudCol = lerp(half3(0.84, 0.88, 0.95), half3(1.0, 1.0, 1.0), saturate((cl - 0.5) * 3.0));
                        col = lerp(col, cloudCol, cm * _SkyOpen * 0.85);
                    }
                }
                // 地平線のもや（フォグ色へなじませる）
                half haze = saturate(1.0 - h * 3.5) * _HazeStrength;
                col = lerp(col, unity_FogColor.rgb, haze);
                if (h < 0.0) col = unity_FogColor.rgb;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
