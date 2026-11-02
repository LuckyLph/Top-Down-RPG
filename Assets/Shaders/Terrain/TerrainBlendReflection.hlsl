#ifndef TOPDOWNRPG_TERRAIN_BLEND_REFLECTION_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_REFLECTION_INCLUDED

#include "Assets/Shaders/Include/PixelLight.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"

#define TB_REFLECTION_FRAMES 8.0
#define TB_REFLECTION_FROM 0.2
#define TB_REFLECTION_REACH 8.0
#define TB_REFLECTION_SAMPLES 20
#define TB_REFLECTION_SCOUTS 5
#define TB_REFLECTION_FAR 0.3
#define TB_REFLECTION_FULL 0.7
#define TB_REFLECTION_SHARPEN 3.0
#define TB_REFLECTION_STRENGTH 0.7
#define TB_REFLECTION_RISE 3.0
#define TB_REFLECTION_SWAY 1.0
#define TB_REFLECTION_SWAY_ROWS 5.0
#define TB_REFLECTION_SWAY_PERIOD 1.2
#define TB_REFLECTION_DASH 4.0
#define TB_REFLECTION_DASH_RATE 4.0
#define TB_REFLECTION_GAPS 0.3
#define TB_REFLECTION_GLINT 3.0
#define TB_REFLECTION_GLINT_FROM 0.3

// Water's reflections of the lights (Docs/Lighting.md, Phase 10), set by DayNightPresenter: xyz the light the global
// light of the world shown puts in the light texture, the sky every other light adds to, and w how strongly water
// reflects now, 0 for not at all, as in the editor.
float4 _TerrainReflection;

// The light the lights other than the sky put at a world position.
half3 TBLightAbove(float2 worldPosition)
{
    return max(0.0, PixelLightAt(worldPosition) - (half3)_TerrainReflection.rgb);
}

// The brightest channel of a light.
half TBBrightest(half3 light)
{
    return max(light.r, max(light.g, light.b));
}

// The light layer m reflects at a pixel of it whose unlit colour (in the output's space) is colour, added to its lit
// colour: none for a layer that reflects nothing (its material's reflection) or while water reflects nothing
// (_TerrainReflection.w), else as strongly as both say. Each row of pixels sways sideways by whole art pixels at
// TB_REFLECTION_FRAMES, and from there the pixel looks up the screen, north, from TB_REFLECTION_FROM to
// TB_REFLECTION_REACH units over TB_REFLECTION_SAMPLES art-pixel centres, for the brightest light other than the sky,
// fading to TB_REFLECTION_FAR of it at the far end. It reflects that light only as far as it is stronger than the
// light at the pixel itself (TB_REFLECTION_RISE), so a light's glitter path runs from it down the screen toward the
// camera, as a reflection does in this view, never up from it; sharpened to its bright core (full at
// TB_REFLECTION_FULL, to the power TB_REFLECTION_SHARPEN), in the light's own hue, at TB_REFLECTION_STRENGTH, and broken
// into dashes of TB_REFLECTION_DASH pixels a share TB_REFLECTION_GAPS of which a hash of the dash and the step hides,
// stepping TB_REFLECTION_DASH_RATE times a second. Its glints and highlights (an unlit colour brighter than
// TB_REFLECTION_GLINT_FROM) also catch the light falling on them, TB_REFLECTION_GLINT times their colour. A pixel finds
// no light on TB_REFLECTION_SCOUTS points spread over the reach and at itself before it reads them all, which leaves
// out only a pool too faint at that reach to show after the sharpening.
half3 TBReflection(TBInputs ins, uint m, half3 colour, float2 worldPixel)
{
    half strength = TBLut(ins, TB_LAYER_REFLECTION_COLUMN, TBLayerRow(m)).x * _TerrainReflection.w;
    half3 reflection = half3(0.0, 0.0, 0.0);
    [branch]
    if (strength > 0.0)
    {
        float t = TBAnimationTime(ins, TB_REFLECTION_FRAMES);
        float row = worldPixel.y;
        float swing = 0.7 * sin(6.28318530718 * (row / TB_REFLECTION_SWAY_ROWS + t / TB_REFLECTION_SWAY_PERIOD))
                    + 0.3 * sin(6.28318530718 * (row / (TB_REFLECTION_SWAY_ROWS * 0.43) - t / (TB_REFLECTION_SWAY_PERIOD * 0.61)));
        float2 shifted = worldPixel + float2(round(TB_REFLECTION_SWAY * swing), 0.0);
        float2 centre = (shifted + 0.5) / ART_PIXELS_PER_UNIT;
        half3 here = TBLightAbove(centre);
        half scouted = TBBrightest(here);
        [unroll]
        for (int s = 0; s < TB_REFLECTION_SCOUTS; s++)
        {
            float reach = round(lerp(TB_REFLECTION_FROM, TB_REFLECTION_REACH, s / (TB_REFLECTION_SCOUTS - 1.0)) * ART_PIXELS_PER_UNIT) / ART_PIXELS_PER_UNIT;
            scouted = max(scouted, TBBrightest(TBLightAbove(centre + float2(0.0, reach))));
        }

        [branch]
        if (scouted > 0.0)
        {
            half3 best = half3(0.0, 0.0, 0.0);
            half bestPeak = 0.0;
            [loop]
            for (int i = 0; i < TB_REFLECTION_SAMPLES; i++)
            {
                float along = i / (TB_REFLECTION_SAMPLES - 1.0);
                float reach = round(lerp(TB_REFLECTION_FROM, TB_REFLECTION_REACH, along) * ART_PIXELS_PER_UNIT) / ART_PIXELS_PER_UNIT;
                half3 light = TBLightAbove(centre + float2(0.0, reach)) * lerp(1.0, TB_REFLECTION_FAR, along);
                half peak = TBBrightest(light);
                if (peak > bestPeak)
                {
                    bestPeak = peak;
                    best = light;
                }
            }

            half rising = saturate((bestPeak - TBBrightest(here)) / max(bestPeak, 0.0001) * TB_REFLECTION_RISE);
            half shaped = pow(saturate(bestPeak * rising / TB_REFLECTION_FULL), TB_REFLECTION_SHARPEN);
            half3 hue = best / max(bestPeak, 0.0001);
            uint hash = PixelLightHash(int2((int)floor(shifted.x / TB_REFLECTION_DASH), (int)row), (uint)floor(t * TB_REFLECTION_DASH_RATE));
            half dash = hash / 16777216.0 < TB_REFLECTION_GAPS ? 0.0 : 1.0;
            half highlight = saturate((dot(colour, half3(0.3333, 0.3333, 0.3333)) - TB_REFLECTION_GLINT_FROM) / 0.08);
            reflection = TB_REFLECTION_STRENGTH * shaped * dash * hue + TB_REFLECTION_GLINT * highlight * colour * here;
        }
    }
    return reflection * strength;
}

#endif
