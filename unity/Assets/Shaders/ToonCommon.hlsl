#ifndef SHAKUTORI_TOON_COMMON_INCLUDED
#define SHAKUTORI_TOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// ---- グローバル（スクリプトから設定） ----
float4 _ShakuPlayerPos;   // xyz = しゃくとりむしの位置, w = 草をかき分ける半径
float4 _ShakuWind;        // xy = 風向き(XZ), z = 強さ, w = 速さ

// 4x4 ベイヤー行列によるディザ（カメラ近くのフェード用）
static const float kShakuBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
float ShakuBayer4(float2 pixel)
{
    uint2 p = uint2(pixel) & 3u;
    uint idx = p.x + p.y * 4u;
    return (kShakuBayer[idx] + 0.5) / 16.0;
}

// 風と、しゃくとりむしが通ったときの「かき分け」
float3 ShakuApplyWind(float3 positionWS, float weight, float strength, float phase)
{
    float w = weight * weight;
    float t = _Time.y * max(_ShakuWind.w, 0.01);
    float2 dir = _ShakuWind.xy;
    float wave = sin(t * 1.3 + positionWS.x * 0.21 + positionWS.z * 0.17 + phase) * 0.65
               + sin(t * 2.9 + positionWS.x * 0.73 - positionWS.z * 0.41 + phase * 1.7) * 0.25
               + 0.35;
    float3 offset = float3(dir.x, 0.0, dir.y) * wave * strength * _ShakuWind.z * w;
    // 細かいゆらぎ
    offset.y += sin(t * 4.3 + phase * 3.1 + positionWS.y * 2.0) * 0.03 * strength * w;

    float3 d = positionWS - _ShakuPlayerPos.xyz;
    float distXZ = length(d.xz);
    float radius = max(_ShakuPlayerPos.w, 0.001);
    float push = saturate(1.0 - distXZ / radius) * saturate(1.0 - abs(d.y) / (radius * 3.0)) * w;
    float2 pushDir = distXZ > 1e-4 ? d.xz / distXZ : float2(0, 0);
    offset.xz += pushDir * push * radius * 0.8;
    offset.y -= push * radius * 0.35;
    return positionWS + offset;
}

// 色相・彩度・明度をずらす（しゃくとりむしのきせかえ用）
half3 ShakuHueShift(half3 c, half hueDeg, half sat, half val)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-6;
    float3 hsv = float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    hsv.x = frac(hsv.x + hueDeg / 360.0);
    hsv.y = saturate(hsv.y * sat);
    hsv.z *= val;
    float4 K2 = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 pp = abs(frac(hsv.xxx + K2.xyz) * 6.0 - K2.www);
    return hsv.z * lerp(K2.xxx, saturate(pp - K2.xxx), hsv.y);
}

float ShakuObjectPhase()
{
    float3 o = GetObjectToWorldMatrix()._m03_m13_m23;
    return dot(o, float3(0.371, 0.113, 0.719));
}

half3 ShakuVertexColor(half3 c, half isSRGB)
{
    return isSRGB > 0.5h ? SRGBToLinear(c) : c;
}

struct ShakuToonParams
{
    half3 shadowTint;
    half threshold;
    half softness;
    half shadowBrightness;
    half3 rimColor;
    half rimStrength;
    half rimWidth;
    half3 specColor;
    half specStrength;
    half specSize;
};

// アニメ調ライティング（2階調 + 柔らかい境界 + リム + ハイライト）
half3 ShakuToonLighting(half3 albedo, float3 positionWS, float3 N, float3 V, ShakuToonParams p, out half litMask)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));
    half3 L = mainLight.direction;
    half NdotL = dot(N, L);
    half halfL = NdotL * 0.5h + 0.5h;
    half ramp = smoothstep(p.threshold - p.softness, p.threshold + p.softness, halfL);
    half shadow = smoothstep(0.25h, 0.75h, mainLight.shadowAttenuation);
    half lit = ramp * shadow;
    litMask = lit;

    half3 ambient = max(SampleSH(N), half3(0.05h, 0.05h, 0.05h));
    half3 baseLight = _MainLightColor.rgb;
    half3 shadowLight = (baseLight * 0.42h + ambient * 0.85h) * p.shadowTint * p.shadowBrightness;
    half3 directLight = mainLight.color + ambient * 0.28h;
    half3 lighting = lerp(shadowLight, directLight, lit);
    half3 col = albedo * lighting;

    // リムライト（光の当たる側を強く）
    half fres = 1.0h - saturate(dot(N, V));
    half rim = smoothstep(1.0h - p.rimWidth, 1.0h - p.rimWidth + 0.06h, fres);
    col += rim * p.rimColor * p.rimStrength * lerp(0.25h, 1.0h, lit) * (albedo * 0.6h + 0.4h);

    // アニメ風ハイライト
    half3 H = normalize(L + V);
    half spec = smoothstep(1.0h - p.specSize, 1.0h - p.specSize + 0.015h, saturate(dot(N, H)));
    col += spec * lit * p.specStrength * p.specColor * mainLight.color;
    return col;
}

float4 ShakuShadowPositionCS(float3 positionWS, float3 normalWS, float3 lightDir, float3 lightPos)
{
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(lightPos - positionWS);
#else
    float3 lightDirectionWS = lightDir;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

#endif
