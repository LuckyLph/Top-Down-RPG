#ifndef TOPDOWNRPG_PIXEL_LIGHT_INCLUDED
#define TOPDOWNRPG_PIXEL_LIGHT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightVariables.hlsl"
#include "Assets/Shaders/Include/PixelSnap.hlsl"
#include "Assets/Shaders/Include/SunShadow.hlsl"

// The screen position the 2D renderer's light textures are read at for a world position, as Lit2DCommon computes a
// sprite's.
float2 PixelLightScreenUV(float2 worldPosition)
{
    float4 clip = TransformWorldToHClip(float3(worldPosition, 0.0));
    return ComputeScreenPos(clip / clip.w).xy;
}

// A colour lit by the 2D lights of blend style 0 at a world position, read there once from that style's light texture,
// with the sun's light taken away by the share shadowed of it a sun shadow covers (SunShadowAt, SunShadowUnder), and
// combined as CombinedShapeLightShared combines them; unchanged where no light reaches its sorting layer (no
// USE_SHAPE_LIGHT_TYPE_0), as URP leaves it. Pass the centre of an art pixel, so the light changes only from one art
// pixel to the next. The lit pass declares the keyword: #pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0.
half3 PixelLitShadowed(half3 colour, float2 worldPosition, half shadowed)
{
#if USE_SHAPE_LIGHT_TYPE_0
    half3 light = SAMPLE_TEXTURE2D_LOD(_ShapeLightTexture0, sampler_ShapeLightTexture0, PixelLightScreenUV(worldPosition), 0).rgb;
    light = max(0.0, light - (half3)_SunColour.rgb * shadowed);
    half3 modulate = light * _ShapeLightBlendFactors0.x;
    half3 additive = light * _ShapeLightBlendFactors0.y;
    return max(0.0, _HDREmulationScale * (colour * modulate + additive));
#else
    return colour;
#endif
}

// A colour lit by the 2D lights of blend style 0 at a world position, out of any sun shadow (PixelLitShadowed).
half3 PixelLit(half3 colour, float2 worldPosition)
{
    return PixelLitShadowed(colour, worldPosition, 0.0);
}

// The flicker of glowing pixels (Docs/Lighting.md, Phase 5), which PropLighting sets: x the step it is at, y the share
// of flickering pixels shown in their flicker colour at a step. Zero, as it is in the editor, shows none.
float4 _GlowFlicker;

// A sprite pixel's colour before it is lit, from its glow (_GlowTex: alpha how much it glows, colour the one it flickers
// to, black for none): its flicker colour, tinted as the sprite is, at the steps a hash of its texel and the flicker's
// step picks, and its own colour otherwise.
half3 PixelGlowColour(half3 colour, half4 glow, half3 tint, float2 texel)
{
    if (glow.a <= 0.0 || max(glow.r, max(glow.g, glow.b)) <= 0.0)
    {
        return colour;
    }

    uint h = (uint)texel.x * 374761393u + (uint)texel.y * 668265263u + (uint)_GlowFlicker.x * 1440662683u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return (h & 0xFFFFFFu) / 16777216.0 < _GlowFlicker.y ? glow.rgb * tint : colour;
}

// A lit colour that glows: no less than its colour unlit times how much it glows.
half3 PixelGlowLit(half3 lit, half3 colour, half glow)
{
    return max(lit, colour * glow);
}

// The world position of the centre of the texel a sprite's fragment draws: the fragment's own, moved by its offset to
// that texel's centre through the mapping from texels to the world, which the screen-space derivatives of both give,
// so a flipped, scaled, rotated or atlas-packed sprite finds its texel's centre too. Call it before any discard.
float2 PixelLightTexelCentre(float2 uv, float2 texelCount, float2 worldPosition)
{
    float2 texel = uv * texelCount;
    float2 offset = floor(texel) + 0.5 - texel;
    float2 texelX = ddx(texel);
    float2 texelY = ddy(texel);
    float determinant = texelX.x * texelY.y - texelY.x * texelX.y;
    if (abs(determinant) < 1e-12)
    {
        return worldPosition;
    }

    float screenX = (texelY.y * offset.x - texelY.x * offset.y) / determinant;
    float screenY = (texelX.x * offset.y - texelX.y * offset.x) / determinant;
    return worldPosition + screenX * ddx(worldPosition) + screenY * ddy(worldPosition);
}

#endif
