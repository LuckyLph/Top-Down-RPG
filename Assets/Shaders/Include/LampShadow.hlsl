#ifndef TOPDOWNRPG_LAMP_SHADOW_INCLUDED
#define TOPDOWNRPG_LAMP_SHADOW_INCLUDED

#include "Assets/Shaders/Include/PixelRelief.hlsl"

// The lamps' shadows on the casters' sorting layer (Docs/LightingGaps.md, Phase 2), which LampShadowPass hands over
// before that sorting layer draws: the ground's light texture, with the lamp shadows of the props and the walls
// (Docs/KnownGaps.md, Phase 7) the casters' own leaves out, and the rendering layers of the casters (none where no pass
// hands them over, and before that sorting layer) and of the units among them, which read the lamps' shadows, and the
// sun's (Phase 3), by column.
TEXTURE2D(_LampShadowGround);
uint _CasterLayers;
uint _CasterUnitLayers;

// How a sprite of rendering layers `layers` reads the lamps' shadows: 0 not at all (no pass handed them over), 1 a prop,
// whole at its feet, 2 a unit, each column at its feet' row, 3 any other sprite of the casters' sorting layer (a flat
// prop, an arrow, an effect), at each art pixel it draws, as the ground does (Docs/KnownGaps.md, Phase 7).
float CasterReading(uint layers)
{
    return _CasterLayers == 0u ? 0.0 : (layers & _CasterLayers) == 0u ? 3.0 : (layers & _CasterUnitLayers) != 0u ? 2.0 : 1.0;
}

// Whether a sprite reading the lamps' shadows as `reading` (CasterReading) is a unit, which reads its sun shadows by
// column too.
bool CasterReadsByColumn(float reading)
{
    return reading > 1.5 && reading < 2.5;
}

// The ground's light at a world position, read from the one texel holding that point, as PixelLightAt reads the
// sprite's own.
half3 LampShadowGroundAt(float2 worldPosition)
{
    uint lightWidth;
    uint lightHeight;
    _LampShadowGround.GetDimensions(lightWidth, lightHeight);
    int2 lightTexel = clamp((int2)floor(PixelLightScreenUV(worldPosition) * float2(lightWidth, lightHeight)), int2(0, 0), int2(lightWidth, lightHeight) - 1);
    return LOAD_TEXTURE2D_LOD(_LampShadowGround, lightTexel, 0).rgb;
}

// The share of the lights other than the sky a sprite keeps at the art pixel whose centre is `centre`: feet.xy its
// feet, feet.z how it reads the lamps' shadows (CasterReading). Read half an art pixel above its feet, under the
// pixel's column for a unit and under its middle for a prop, and at the art pixel itself for any other sprite: the
// ground's local light (less _LightRelief.xyz) over the sprites' own there, brightest channels, so a lamp's shadow on
// the ground there takes that lamp's light away. All of it for a sprite that reads none, where no local light reaches
// the point read, and without lights.
half LampShadowKept(float3 feet, float2 centre)
{
#if USE_SHAPE_LIGHT_TYPE_0
    if (feet.z < 0.5)
    {
        return 1.0;
    }

    float2 at = feet.z > 2.5 ? centre : float2(CasterReadsByColumn(feet.z) ? centre.x : feet.x, feet.y + 0.5 / ART_PIXELS_PER_UNIT);
    half own = PixelBrightest(PixelLocalLight(at));
    if (!PixelLocalLightReaches(own))
    {
        return 1.0;
    }

    half ground = PixelBrightest(max(0.0, LampShadowGroundAt(at) - (half3)_LightRelief.rgb));
    return saturate(ground / own);
#else
    return 1.0;
#endif
}

// A caster's colour lit as PixelLitShadowed lights it, keeping `kept` (LampShadowKept) of the lights other than the
// sky (_LightRelief.xyz); exactly PixelLitShadowed's colour where it keeps all of them.
half3 LampShadowLit(half3 colour, float2 worldPosition, half shadowed, half kept)
{
#if USE_SHAPE_LIGHT_TYPE_0
    half3 light = max(0.0, PixelLightAt(worldPosition) - (half3)_SunColour.rgb * shadowed);
    [branch]
    if (kept < 1.0)
    {
        half3 sky = (half3)_LightRelief.rgb;
        light = min(light, sky) + max(0.0, light - sky) * kept;
    }

    half3 modulate = light * _ShapeLightBlendFactors0.x;
    half3 additive = light * _ShapeLightBlendFactors0.y;
    return max(0.0, _HDREmulationScale * (colour * modulate + additive));
#else
    return colour;
#endif
}

#endif
