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
    half _HueAlong;
    half _Stripes;
    half _StripeDark;
    half _Dots;
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
    // しゃくとりむしのきせかえ：uv.x は体のどこか（尾 0 → 頭 1）、uv.y は体のまわりの角度（0〜1）
    albedo = ShakuHueShift(albedo, _HueShift + _HueAlong * uv.x, _SatMul, _ValMul);
    // しま：体の前後に、こい帯
    albedo *= 1.0h - _StripeDark * step(0.5h, frac(uv.x * _Stripes)) * step(0.5h, _Stripes);
    // 水玉：体のまわりに 8 つ、前後に 26 の、まるい白い点
    float2 cell = frac(float2(uv.x * 26.0, uv.y * 8.0)) - 0.5;
    half dotK = smoothstep(0.24h, 0.18h, length(cell)) * _Dots;
    albedo = lerp(albedo, half3(0.95h, 0.92h, 0.93h), dotK);
#endif
#if defined(_TERRAIN)
    half d1 = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * _DetailScale).r;
    half d2 = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * _DetailScale * 0.173 + 0.31).g;
    half d = (d1 * 0.6h + d2 * 0.4h) - 0.5h;
    albedo *= 1.0h + d * _DetailStrength * 2.0h;
    albedo = lerp(albedo, albedo * half3(1.06h, 1.02h, 0.9h), saturate(d2 - 0.5h) * _DetailStrength);
    // 近くの地面の、こまかな模様（しゃくとりむしの目の高さで、手前の地面がのっぺりしないように）。
    // 遠くは、ちらつかないように消す。色で、草・砂・雪を見分ける
    float camDist = distance(positionWS, _WorldSpaceCameraPos.xyz);
    half nearK = saturate(1.6h - camDist / 28.0h);
    half4 fa = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 0.55);
    half4 fb = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 1.7 + 0.37);
    half greenK = saturate((albedo.g - max(albedo.r, albedo.b)) * 10.0h);
    half whiteK = saturate((min(albedo.r, min(albedo.g, albedo.b)) - 0.5h) * 5.0h);
    half sandK = saturate((albedo.r - albedo.b) * 4.0h - 0.35h) * (1.0h - greenK) * (1.0h - whiteK);
    // つぶのざらつきと、小石・土くれ（緑でない所に、こい点）
    half grain = (fa.r * 0.5h + fb.r * 0.5h) - 0.5h;
    albedo *= 1.0h + grain * _ShakuGround.x * nearK * 1.4h;
    half pb = max(SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 0.13).b,
                  SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 0.29 + 0.61).b * 0.75h);
    albedo *= 1.0h - pb * (1.0h - greenK * 0.7h) * (1.0h - whiteK) * _ShakuGround.x * nearK * 0.45h;
    // 草の株のむら（緑の所：明るい株と、こいすき間。細長くすると、手前で流れたように見える）
    half st = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, float2(positionWS.x * 1.25 + positionWS.z * 0.2, positionWS.z * 1.05 - positionWS.x * 0.15)).g - 0.5h;
    albedo *= 1.0h + st * greenK * _ShakuGround.y * nearK * 1.6h;
    // 砂紋（風のつけた、ゆるい波の線）
    half rip = sin(dot(positionWS.xz, float2(0.83, 0.55)) * 2.6 + d2 * 10.0) * 0.5h + 0.5h;
    albedo *= 1.0h - rip * rip * sandK * _ShakuGround.z * saturate(1.4h - camDist / 50.0h) * 0.16h;
    // 雪のきらめき（見る所がかわると、光る点もかわる）
    // （光る点は動かさず、見る所によって、点く点がかわる）
    half sp = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 0.07).a;
    half tw = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, positionWS.xz * 0.21 + _WorldSpaceCameraPos.xz * 0.08).g;
    albedo += sp * smoothstep(0.5h, 0.65h, tw) * whiteK * _ShakuGround.w * nearK * 2.0h;
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
