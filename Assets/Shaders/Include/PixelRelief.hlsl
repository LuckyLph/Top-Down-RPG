#ifndef TOPDOWNRPG_PIXEL_RELIEF_INCLUDED
#define TOPDOWNRPG_PIXEL_RELIEF_INCLUDED

#include "Assets/Shaders/Include/PixelLight.hlsl"

#define PIXEL_RELIEF_STEP 8.0
#define PIXEL_RELIEF_RISE 1.0
#define PIXEL_RELIEF_FROM 0.02
#define PIXEL_RELIEF_SPRITES 1.0

// Relief under the lights (Docs/Lighting.md, Phase 11), set by DayNightPresenter: xyz the light the global light of the
// world shown puts in the light texture, the sky every other light adds to, and w 1 while relief is drawn, 0 for none,
// as in the editor.
float4 _LightRelief;

// The brightest channel of a light.
half PixelBrightest(half3 light)
{
    return max(light.r, max(light.g, light.b));
}

// The light the lights other than the sky put at a world position (PixelLightAt less _LightRelief.xyz).
half3 PixelLocalLight(float2 worldPosition)
{
    return max(0.0, PixelLightAt(worldPosition) - (half3)_LightRelief.rgb);
}

// Whether the lights other than the sky reach a world position whose local light (PixelLocalLight) has brightest
// channel here: above PIXEL_RELIEF_FROM, which leaves out the light texture's rounding of the sky (its 10- and 11-bit
// floats round a sky of 0.85 by up to 0.008).
bool PixelLocalLightReaches(half here)
{
    return here > PIXEL_RELIEF_FROM;
}

// Where the local light at a world position comes from, as a unit vector: on the ground toward where it grows
// brightest, read PIXEL_RELIEF_STEP art pixels to either side (fewer crawled as the camera moved while the light
// textures were half the screen's size, Docs/Lighting.md's Phase 11), and its z how much it shines from above rather than from the side, its slope over the
// distance divided by how bright it is there (here, its brightest channel) and scaled by PIXEL_RELIEF_RISE, so a light
// shines from the side at the edge of its pool and from above at its middle.
float3 PixelLocalLightDirection(float2 worldPosition, half here)
{
    float step = PIXEL_RELIEF_STEP / ART_PIXELS_PER_UNIT;
    half right = PixelBrightest(PixelLocalLight(worldPosition + float2(step, 0.0)));
    half left = PixelBrightest(PixelLocalLight(worldPosition - float2(step, 0.0)));
    half up = PixelBrightest(PixelLocalLight(worldPosition + float2(0.0, step)));
    half down = PixelBrightest(PixelLocalLight(worldPosition - float2(0.0, step)));
    float2 gradient = float2(right - left, up - down) / (2.0 * step) / max(here, 0.001);
    return normalize(float3(gradient * PIXEL_RELIEF_RISE, 1.0));
}

// The light a sprite pixel's relief adds to its lit colour (Docs/Lighting.md, Phase 11): colour, its colour before it is
// lit, facing normal (x right and y up the screen, from its normal sheet) at the world position centre, is lit by the
// local light (PixelLocalLight) as much more as it faces where that light comes from (PixelLocalLightDirection), to
// twice as much, and as much less as it faces away, to none, by PIXEL_RELIEF_SPRITES; the sky and the sun keep the
// painted light. None while relief is off (_LightRelief.w) or without USE_SHAPE_LIGHT_TYPE_0.
half3 PixelSpriteRelief(half3 colour, float3 normal, float2 centre)
{
    half3 added = half3(0.0, 0.0, 0.0);
#if USE_SHAPE_LIGHT_TYPE_0
    [branch]
    if (_LightRelief.w > 0.5)
    {
        half3 local = PixelLocalLight(centre);
        half here = PixelBrightest(local);
        [branch]
        if (PixelLocalLightReaches(here))
        {
            float3 light = PixelLocalLightDirection(centre, here);
            half change = clamp(dot(normal.xy, light.xy) / max(light.z, 0.05) * PIXEL_RELIEF_SPRITES, -1.0, 1.0);
            added = _HDREmulationScale * colour * local * _ShapeLightBlendFactors0.x * change;
        }
    }
#endif
    return added;
}

#endif
