#ifndef TOPDOWNRPG_TERRAIN_BLEND_WAVES_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_WAVES_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"

#define TB_COAST_MIN -64.0
#define TB_COAST_RANGE 1024.0
#define TB_WAVE_FADE_START 1.0
#define TB_WAVE_FADE 5.0
#define TB_WAVE_WHOLE_BLEND 30.0
#define TB_WASH_ADVANCE 0.35
#define TB_WASH_FOAM_FADE 0.15
#define TB_WASH_FOAM 2.5
#define TB_WASH_FILM_BACK 5.0
#define TB_WASH_WET_FRAMES 7

TEXTURE2D(_TerrainCoast);
SAMPLER(sampler_TerrainCoast);

// The signed distance in pixels from a pixel to the coast of the area's layer with waves (TerrainCoastMap), positive
// at sea, read bilinearly from the coast map, which covers the control map's cells.
float TBCoastDistance(TBInputs ins, float2 worldPixel)
{
    float2 pixel = worldPixel - ins.rect.xy * ART_PIXELS_PER_UNIT;
    float2 uv = (pixel + 0.5) / (ins.rect.zw * ART_PIXELS_PER_UNIT);
    return SAMPLE_TEXTURE2D_LOD(_TerrainCoast, sampler_TerrainCoast, uv, 0.0).r * TB_COAST_RANGE + TB_COAST_MIN;
}

// The waves' phase at a pixel lying coast pixels out at sea: a crest wherever it is a whole number, rising a wave per
// spacing further out and per period of time, so the crests roll in toward the coast, turned by the waves' angle
// (waves: spacing, period, width, tangent of the angle) so they reach a straight shore at one end first.
float TBWavePhase(TBInputs ins, float4 waves, float framesPerSecond, float coast, float2 worldPixel)
{
    return (max(coast, 0.0) + worldPixel.x * waves.w) / waves.x + TBAnimationTime(ins, framesPerSecond) / waves.y;
}

// How much of a wave shows along its front at x, for the wave whose own place is z: smooth noise over both, drifting
// slowly in time t, raised by bias, so out at sea a wave breaks into stretches that grow and fade as it travels.
float TBWaveStretches(float x, float z, float t, float bias)
{
    float n = sin(6.28318530718 * (0.0071 * x + 0.0172 * z) + 0.4 + 0.31 * t)
            + sin(6.28318530718 * (-0.0058 * x + 0.0241 * z) + 2.1 - 0.23 * t)
            + sin(6.28318530718 * (0.0089 * x - 0.0137 * z) + 4.4 + 0.17 * t)
            + sin(6.28318530718 * (0.0034 * x + 0.0203 * z) + 1.3 - 0.37 * t)
            + sin(6.28318530718 * (-0.0081 * x - 0.0158 * z) + 5.2 + 0.27 * t)
            + sin(6.28318530718 * (0.0049 * x + 0.0119 * z) + 3.0 - 0.13 * t);
    return saturate(n * 0.52 + bias);
}

// How strongly a wave whose crest lies crestAt pixels from the coast shows: its stretches (bias as TBWaveStretches),
// whole within wholeWithin pixels of the coast, fading out over its last pixels before the coast.
float TBWaveStrength(float crestAt, float z, float x, float t, float wholeWithin, float bias)
{
    float shown = max(TBWaveStretches(x, z, t, bias), saturate((wholeWithin - crestAt) / TB_WAVE_WHOLE_BLEND));
    return shown * saturate((crestAt - TB_WAVE_FADE_START) / TB_WAVE_FADE);
}

// Short noise along a crest or the swash's front, about 1 high, for its gaps.
float TBWaveBreak(float2 worldPixel)
{
    return 0.94 * sin(worldPixel.x * 0.897 + 1.1) + 0.7 * sin(worldPixel.x * 0.53 + 3.7) + 0.59 * sin(worldPixel.x * 1.61 + 0.4) + 0.53 * sin(dot(worldPixel, float2(0.93, 0.71)) + 2.2);
}

// Layer m's waves over its colour at a pixel inside it (TBWavePhase): each wave a band of lighter water, its wave colour
// at the crest fading back out to sea over its width in three dithered steps, a broken crest one pixel thick along its
// shoreward face and a darker trough one pixel thick just ahead of it, all as strong as the wave shows there
// (TBWaveStrength, its stretches raised by the crest colour's alpha). Unchanged for a layer without waves.
float3 TBWaves(TBInputs ins, uint m, float3 colour, float2 worldPixel)
{
    int row = TBLayerRow(m);
    float4 waves = TBLut(ins, TB_LAYER_WAVES_COLUMN, row);
    [branch]
    if (waves.x > 0.0)
    {
        float framesPerSecond = TBLut(ins, TB_LAYER_ANIMATION_COLUMN, row).x;
        float4 body = TBLut(ins, TB_LAYER_WAVE_COLOUR_COLUMN, row);
        float4 crest = TBLut(ins, TB_LAYER_WAVE_CREST_COLUMN, row);
        float coast = TBCoastDistance(ins, worldPixel);
        float t = TBAnimationTime(ins, framesPerSecond);
        float phase = TBWavePhase(ins, waves, framesPerSecond, coast, worldPixel);
        float q = frac(phase) * waves.x;
        float z = floor(phase) * waves.x - worldPixel.x * waves.w;
        float strength = TBWaveStrength(coast - q, z, worldPixel.x, t, body.a, crest.a);
        [branch]
        if (q < waves.z)
        {
            float checker = frac((worldPixel.x + worldPixel.y) * 0.5) * 2.0;
            float amount = pow(saturate(1.0 - q / waves.z), 1.3) * strength;
            colour = lerp(colour, body.rgb, floor(amount * 3.0 + 0.5 * checker) / 3.0);
        }

        [branch]
        if (q > waves.x - 1.0 && TBWaveStrength(coast - q + waves.x, z + waves.x, worldPixel.x, t, body.a, crest.a) > 0.4)
        {
            colour = lerp(colour, TBLut(ins, TB_LAYER_WAVE_TROUGH_COLUMN, row).rgb, 0.5);
        }

        if (q < 1.0 && strength > 0.5 && TBWaveBreak(worldPixel) > 0.35)
        {
            colour = crest.rgb;
        }
    }
    return colour;
}

// How far up the shore the swash has run at phase u of a wave's period, from 0 to 1: quickly up over the first
// TB_WASH_ADVANCE of it, slowing at the top, then slowly back.
float TBWashRise(float u)
{
    return u < TB_WASH_ADVANCE ? sin(u / TB_WASH_ADVANCE * 1.57079632679) : 0.5 + 0.5 * cos(3.14159265359 * (u - TB_WASH_ADVANCE) / (1.0 - TB_WASH_ADVANCE));
}

// Layer m's swash at a pixel outside distance outside past its edge, premultiplied: each wave, as it reaches the coast,
// runs up over the layer it meets as far as the layer's swash (less in places along the shore, by the edge noise) times
// its rise (TBWashRise), never over nothing nor along a seamless pair. Covered pixels show the film of water, its back
// colour more than TB_WASH_FILM_BACK pixels behind the front; at the front, while the wave runs up and briefly after,
// foam broken by TBWaveBreak, its front pixel the foam colour and the next its shade. Pixels the swash left within its
// last frames are darkened, the wetter the more recently. Clear for a layer without swash or waves.
float4 TBWash(TBInputs ins, uint m TB_W_PARAM, float outside, float2 worldPixel)
{
    int row = TBLayerRow(m);
    float swash = TBLut(ins, TB_LAYER_SPARKLE_TIMING_COLUMN, row).y;
    float4 waves = TBLut(ins, TB_LAYER_WAVES_COLUMN, row);
    float4 wash = float4(0.0, 0.0, 0.0, 0.0);
    [branch]
    if (swash > 0.0 && waves.x > 0.0 && outside < swash)
    {
        float framesPerSecond = TBLut(ins, TB_LAYER_ANIMATION_COLUMN, row).x;
        float along = saturate((1.0 - w[0]) - TBSeamlessBelow(ins, m TB_W_ARG));
        float reachMax = swash * along * (0.85 + 0.15 * PeriodicEdgeNoise(worldPixel * 0.5));
        float u = frac(TBWavePhase(ins, waves, framesPerSecond, 0.0, worldPixel));
        float reach = reachMax * TBWashRise(u);
        float4 foam = TBLut(ins, TB_LAYER_WASH_FOAM_COLUMN, row);
        [branch]
        if (outside < reach)
        {
            float behind = reach - outside;
            float foamness = saturate(1.0 - (u - TB_WASH_ADVANCE) / TB_WASH_FOAM_FADE);
            float3 colour = TBLut(ins, behind > TB_WASH_FILM_BACK ? TB_LAYER_WASH_FILM_BACK_COLUMN : TB_LAYER_WASH_FILM_COLUMN, row).rgb;
            if (behind < TB_WASH_FOAM && TBWaveBreak(worldPixel) < 2.2 * foamness - 1.0)
            {
                colour = behind < 1.5 ? foam.rgb : TBLut(ins, TB_LAYER_WASH_FOAM_SHADE_COLUMN, row).rgb;
            }
            wash = float4(colour, 1.0);
        }
        else
        {
            float wet = 0.0;
            float frame = 1.0 / (max(framesPerSecond, 1.0) * waves.y);
            [loop]
            for (int k = 1; k <= TB_WASH_WET_FRAMES; k++)
            {
                if (outside < reachMax * TBWashRise(frac(u - k * frame)))
                {
                    wet = max(wet, 1.0 - k / (TB_WASH_WET_FRAMES + 1.0));
                }
            }
            wash = float4(0.0, 0.0, 0.0, foam.a * wet);
        }
    }
    return wash;
}

#endif
