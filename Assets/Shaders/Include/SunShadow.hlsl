#ifndef TOPDOWNRPG_SUN_SHADOW_INCLUDED
#define TOPDOWNRPG_SUN_SHADOW_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"

#define SUN_SHADOW_READ_OFFSET 2.0

// The sun (Docs/Lighting.md, Phase 4), global and set by the day and night only when it changes (SunShadowGlobals).
// _SunShadow.xy is where a point one unit above the ground lands on the ground, as an offset on the screen in units,
// and _SunShadow.z is 1 while the sun casts shadows on the world shown (by day, outdoors). _SunColour is the sun's light,
// which the world's global light holds with the sky's and a sun shadow takes away. The sun shadow mask, drawn each frame
// by SunShadowFeature, covers the world's art pixels from _SunShadowRect.xy over _SunShadowRect.zw (its size 0 with no
// mask), one texel each, holding 1 where a shadow lies (Phase 11): in red on the level ground (the level ground's
// casters', raised ground's cliffs' and the clouds'), in green on raised ground's top (the casters standing on it and
// the clouds), in blue where a level caster's shadow comes from its parts higher than the cliff, and in alpha the clouds
// alone. The ground beside it (_SunShadowGround, drawn with the props' shadows) holds in red what the ground is at each
// art pixel, SUN_SHADOW_LEVEL, SUN_SHADOW_FACE or SUN_SHADOW_TOP, and in green the cliff's height in art pixels / 255
// within reach of raised ground (0 elsewhere). _SunShadowMaskMatrix draws world positions into both textures' texels,
// and renderers on the rendering layers _SunShadowFeetLayers (the casters) read their shadow at their feet
// (SunShadowUnder), every other one at each art pixel it draws. _SunShadowProps is the mask without the units' shadows
// and the clouds (the props' and the cliffs' alone, in the same channels), which a unit reads under each of its columns
// (SunShadowColumn, Docs/LightingGaps.md, Phase 3).
float4 _SunShadow;
float4 _SunColour;
float4 _SunShadowRect;
float4x4 _SunShadowMaskMatrix;
uint _SunShadowFeetLayers;
TEXTURE2D(_SunShadowMask);
TEXTURE2D(_SunShadowGround);
TEXTURE2D(_SunShadowProps);

#define SUN_SHADOW_LEVEL 0.0
#define SUN_SHADOW_FACE 0.5
#define SUN_SHADOW_TOP 1.0

// Whether the art pixel at whole art pixel coordinates worldPixel lies in the mask while the sun casts.
bool SunShadowCovers(float2 worldPixel)
{
    int2 texel = (int2)(worldPixel - _SunShadowRect.xy);
    return _SunShadow.z > 0.5 && texel.x >= 0 && texel.y >= 0 && texel.x < (int)_SunShadowRect.z && texel.y < (int)_SunShadowRect.w;
}

// The sun shadow mask's texel at whole art pixel coordinates worldPixel, or with props that of the mask without the
// units' shadows and the clouds (_SunShadowProps): zero outside it and while the sun casts none.
half4 SunShadowTexelAt(float2 worldPixel, bool props)
{
    half4 texel = half4(0.0, 0.0, 0.0, 0.0);
    [branch]
    if (SunShadowCovers(worldPixel))
    {
        int2 at = (int2)(worldPixel - _SunShadowRect.xy);
        texel = props ? LOAD_TEXTURE2D_LOD(_SunShadowProps, at, 0) : LOAD_TEXTURE2D_LOD(_SunShadowMask, at, 0);
    }
    return texel;
}

// The sun shadow mask's texel at whole art pixel coordinates worldPixel: zero outside it and while the sun casts none.
half4 SunShadowMaskAt(float2 worldPixel)
{
    return SunShadowTexelAt(worldPixel, false);
}

// The ground at whole art pixel coordinates worldPixel (_SunShadowGround): x what it is (SUN_SHADOW_LEVEL,
// SUN_SHADOW_FACE or SUN_SHADOW_TOP), y the cliff's height in art pixels within reach of raised ground, 0 elsewhere;
// level ground outside the mask and while the sun casts none.
half2 SunShadowGroundAt(float2 worldPixel)
{
    half2 ground = half2(0.0, 0.0);
    [branch]
    if (SunShadowCovers(worldPixel))
    {
        half2 texel = LOAD_TEXTURE2D_LOD(_SunShadowGround, (int2)(worldPixel - _SunShadowRect.xy), 0).rg;
        ground = half2(texel.x, round(texel.y * 255.0));
    }
    return ground;
}

// How far a shadow drops, in whole art pixels on the screen, from a plateau's top to the level ground below it, for a
// cliff of cliff art pixels (Docs/Lighting.md, Phase 11): a top stands the cliff's height above the level ground in
// front of it and is drawn that height up the screen, so a shadow running off it lands where the sun takes it from that
// much higher, the sun's shadow vector times the height further, and that height lower on the screen.
float2 SunShadowDrop(float cliff)
{
    return round(cliff * (_SunShadow.xy - float2(0.0, 1.0)));
}

// Whether a sun shadow covers the art pixel at whole art pixel coordinates worldPixel, from the mask or with props
// from the mask without the units' shadows and the clouds: 1 under one, 0 elsewhere, outside the mask and while the
// sun casts none. It knows the ground's height (Docs/Lighting.md, Phase 11): level ground takes the level ground's
// shadows, and within reach of raised ground also the shadows running off a top, read where they leave it, one drop
// (SunShadowDrop) up the screen, unless that lies on a top; a top takes the shadows on tops, and the parts of a level
// caster's shadow higher than the cliff, read where they would have landed below, one drop down the screen; a cliff
// face, which faces away from the sun in this view, only the clouds.
half SunShadowFrom(float2 worldPixel, bool props)
{
    half shadowed = 0.0;
    [branch]
    if (SunShadowCovers(worldPixel))
    {
        half4 mask = SunShadowTexelAt(worldPixel, props);
        half2 ground = SunShadowGroundAt(worldPixel);
        shadowed = mask.r;
        [branch]
        if (ground.y > 0.0)
        {
            float2 drop = SunShadowDrop(ground.y);
            if (ground.x > 0.75)
            {
                shadowed = max(mask.g, SunShadowTexelAt(worldPixel + drop, props).b);
            }
            else if (ground.x > 0.25)
            {
                shadowed = mask.a;
            }
            else if (SunShadowGroundAt(worldPixel - drop).x < 0.75)
            {
                shadowed = max(shadowed, SunShadowTexelAt(worldPixel - drop, props).g);
            }
        }
    }
    return shadowed;
}

// Whether a sun shadow covers the art pixel at whole art pixel coordinates worldPixel (SunShadowFrom, from the mask).
half SunShadowAt(float2 worldPixel)
{
    return SunShadowFrom(worldPixel, false);
}

// Whether a sun shadow covers a unit's column (Docs/LightingGaps.md, Phase 3): read on the ground at a world position
// on its feet' row under that column, from the mask without the units' shadows, so a unit never darkens itself, with
// the clouds over it; a shadow's edge then sweeps across the unit as it walks.
half SunShadowColumn(float2 worldPosition)
{
    float2 worldPixel = floor(worldPosition * ART_PIXELS_PER_UNIT);
    return max(SunShadowFrom(worldPixel, true), SunShadowMaskAt(worldPixel).a);
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
