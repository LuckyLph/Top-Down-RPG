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

// The light of blend style 0 at a world position as its light texture holds it, the light's colours times their
// intensities: read from the one texel holding that point (see PixelLitShadowed); black where no light reaches the
// sorting layer drawn (no USE_SHAPE_LIGHT_TYPE_0).
half3 PixelLightAt(float2 worldPosition)
{
#if USE_SHAPE_LIGHT_TYPE_0
    uint lightWidth;
    uint lightHeight;
    _ShapeLightTexture0.GetDimensions(lightWidth, lightHeight);
    int2 lightTexel = clamp((int2)floor(PixelLightScreenUV(worldPosition) * float2(lightWidth, lightHeight)), int2(0, 0), int2(lightWidth, lightHeight) - 1);
    return LOAD_TEXTURE2D_LOD(_ShapeLightTexture0, lightTexel, 0).rgb;
#else
    return half3(0.0, 0.0, 0.0);
#endif
}

// A colour lit by the 2D lights of blend style 0 at a world position, read there once from that style's light texture,
// with the sun's light taken away by the share shadowed of it a sun shadow covers (SunShadowAt, SunShadowUnder), and
// combined as CombinedShapeLightShared combines them; unchanged where no light reaches its sorting layer (no
// USE_SHAPE_LIGHT_TYPE_0), as URP leaves it. Pass the centre of an art pixel, so the light changes only from one art
// pixel to the next. The light is read from the one texel holding that point, not blended from four: the light
// textures are the screen's size (Docs/LightingGaps.md, Phase 4), so at any whole number of screen pixels to an art
// pixel that texel's centre lies in the same art pixel, and a light's shadow, drawn by the art pixel each texel lies in
// (LightShadowProjected.shader), reaches every art pixel whole and never moves against them as the camera does. The lit pass declares the keyword: #pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0.
half3 PixelLitShadowed(half3 colour, float2 worldPosition, half shadowed)
{
#if USE_SHAPE_LIGHT_TYPE_0
    half3 light = max(0.0, PixelLightAt(worldPosition) - (half3)_SunColour.rgb * shadowed);
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

// The flicker of glowing pixels (Docs/Lighting.md, Phase 5), which PropLighting sets: x the step it is at, which moving
// flames step through their frames by too (Phase 6), y the share of flickering pixels shown in their flicker colour at a
// step, z 1 to draw no pixel glowing (the lighting dev panel). Zero, as it is in the editor, shows no flicker, every
// glow and every flame on its first frame.
float4 _GlowFlicker;

// A hash in [0, 2^24) of a pixel and a step, as PropLighting hashes a light's place (step 0).
uint PixelLightHash(int2 pixel, uint step)
{
    uint h = asuint(pixel.x) * 374761393u + asuint(pixel.y) * 668265263u + step * 1440662683u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return h & 0xFFFFFFu;
}

// A sprite pixel's colour before it is lit, from its glow (_GlowTex: alpha how much it glows, colour the one it flickers
// to, black for none): its flicker colour, tinted as the sprite is, at the steps a hash of its texel and the flicker's
// step picks, and its own colour otherwise.
half3 PixelGlowColour(half3 colour, half4 glow, half3 tint, float2 texel)
{
    if (glow.a <= 0.0 || max(glow.r, max(glow.g, glow.b)) <= 0.0)
    {
        return colour;
    }

    return PixelLightHash((int2)texel, (uint)_GlowFlicker.x) / 16777216.0 < _GlowFlicker.y ? glow.rgb * tint : colour;
}

// The texture coordinates of a moving flame's frame (Docs/Lighting.md, Phase 6): its sheet holds `frames` frames in
// rows, the first at the bottom where the sprite is sliced, and the sprite standing at `origin` shows the frame the
// flicker's step has reached from the one a hash of the art pixel it stands on starts it at, as
// PropLighting.FlameFrameAt picks it; the first while nothing flickers (the editor) and for a still sprite.
float2 PixelFlameFrame(float2 uv, float2 origin, float frames)
{
    if (frames <= 1.0 || _GlowFlicker.y <= 0.0)
    {
        return uv;
    }

    uint start = PixelLightHash((int2)round(origin * ART_PIXELS_PER_UNIT), 0u);
    uint frame = ((uint)_GlowFlicker.x + start) % (uint)frames;
    return float2(uv.x, uv.y + frame / frames);
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
