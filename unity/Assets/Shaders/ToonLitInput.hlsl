#ifndef SHAKUTORI_TOON_LIT_INPUT_INCLUDED
#define SHAKUTORI_TOON_LIT_INPUT_INCLUDED

#include "ToonCommon.hlsl"

TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
TEXTURE2D(_DetailMap);  SAMPLER(sampler_DetailMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _VertexColorSRGB;
    half4 _ShadowTint;
    half _ShadowThreshold;
    half _ShadowSoftness;
    half _ShadowBrightness;
    half4 _RimColor;
    half _RimStrength;
    half _RimWidth;
    half4 _SpecularColor;
    half _SpecularStrength;
    half _SpecularSize;
    half4 _EmissionColor;
    half4 _OutlineColor;
    half _OutlineWidth;
    half _OutlineSmoothNormals;
    half _DetailScale;
    half _DetailStrength;
    half _WindStrength;
    half _NearFadeDistance;
    half _Cull;
    half _HueShift;
    half _SatMul;
    half _ValMul;
CBUFFER_END

ShakuToonParams ShakuGetParams()
{
    ShakuToonParams p;
    p.shadowTint = _ShadowTint.rgb;
    p.threshold = _ShadowThreshold;
    p.softness = _ShadowSoftness;
    p.shadowBrightness = _ShadowBrightness;
    p.rimColor = _RimColor.rgb;
    p.rimStrength = _RimStrength;
    p.rimWidth = _RimWidth;
    p.specColor = _SpecularColor.rgb;
    p.specStrength = _SpecularStrength;
    p.specSize = _SpecularSize;
    return p;
}

float3 ShakuDeformWS(float3 positionWS, half vertexAlpha)
{
#if defined(_WIND)
    positionWS = ShakuApplyWind(positionWS, vertexAlpha, _WindStrength, ShakuObjectPhase());
#endif
    return positionWS;
}

half3 ShakuAlbedo(half4 vcolor, float2 uv, float3 positionWS)
{
    half3 albedo = _BaseColor.rgb * ShakuVertexColor(vcolor.rgb, _VertexColorSRGB);
    albedo *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
#if defined(_HUE_SHIFT)
    albedo = ShakuHueShift(albedo, _HueShift, _SatMul, _ValMul);
#endif
#if defined(_TERRAIN)
    half d1 = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * _DetailScale).r;
    half d2 = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * _DetailScale * 0.173 + 0.31).g;
    half d = (d1 * 0.6h + d2 * 0.4h) - 0.5h;
    albedo *= 1.0h + d * _DetailStrength * 2.0h;
    albedo = lerp(albedo, albedo * half3(1.06h, 1.02h, 0.9h), saturate(d2 - 0.5h) * _DetailStrength);
#endif
    return albedo;
}

void ShakuNearFade(float3 positionWS, float4 positionCS)
{
#if defined(_NEAR_FADE)
    float dist = distance(positionWS, _WorldSpaceCameraPos.xyz);
    float fade = saturate((dist - _NearFadeDistance * 0.4) / max(_NearFadeDistance, 0.001));
    clip(fade - ShakuBayer4(positionCS.xy));
#endif
}

#endif
