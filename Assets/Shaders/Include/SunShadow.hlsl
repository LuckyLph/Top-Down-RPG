#ifndef TOPDOWNRPG_SUN_SHADOW_INCLUDED
#define TOPDOWNRPG_SUN_SHADOW_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"

#define SUN_SHADOW_READ_OFFSET 2.0

// The sun (Docs/Lighting.md, Phase 4), global and set by the day and night only when it changes (SunShadowGlobals).
// _SunShadow.xy is where a point one unit above the ground lands on the ground, as an offset on the screen in units,
// and _SunShadow.z is 1 while the sun casts shadows on the world shown (by day, outdoors). _SunColour is the sun's light,
// which the world's global light holds with the sky's and a sun shadow takes away. The sun shadow mask, drawn each frame
// by SunShadowFeature, holds 1 at every art pixel of the world a sun shadow covers, from the art pixel _SunShadowRect.xy
// over _SunShadowRect.zw art pixels; with no mask its size is 0. _SunShadowMaskMatrix draws world positions into the
// mask's texels, and renderers on the rendering layers _SunShadowFeetLayers (the casters) read their shadow at their
// feet (SunShadowUnder), every other one at each art pixel it draws.
float4 _SunShadow;
float4 _SunColour;
float4 _SunShadowRect;
float4x4 _SunShadowMaskMatrix;
uint _SunShadowFeetLayers;
TEXTURE2D(_SunShadowMask);

// Whether a sun shadow covers the art pixel at whole art pixel coordinates worldPixel: 1 under one, 0 elsewhere,
// outside the mask and while the sun casts none.
half SunShadowAt(float2 worldPixel)
{
    int2 texel = (int2)(worldPixel - _SunShadowRect.xy);
    half shadowed = 0.0;
    [branch]
    if (_SunShadow.z > 0.5 && texel.x >= 0 && texel.y >= 0 && texel.x < (int)_SunShadowRect.z && texel.y < (int)_SunShadowRect.w)
    {
        shadowed = LOAD_TEXTURE2D_LOD(_SunShadowMask, texel, 0).r;
    }
    return shadowed;
}

// Whether a sun shadow covers a caster standing with its feet at a world position: read SUN_SHADOW_READ_OFFSET art
// pixels toward the sun from its feet, past where its own shadow starts, so a caster never darkens itself and one
// standing in another's shadow darkens whole.
half SunShadowUnder(float2 feet)
{
    half shadowed = 0.0;
    [branch]
    if (_SunShadow.z > 0.5)
    {
        float2 sunward = -normalize(_SunShadow.xy);
        shadowed = SunShadowAt(floor(feet * ART_PIXELS_PER_UNIT + sunward * SUN_SHADOW_READ_OFFSET));
    }
    return shadowed;
}

#endif
