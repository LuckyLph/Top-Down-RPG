#ifndef TOPDOWNRPG_TERRAIN_BLEND_ANIMATION_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_ANIMATION_INCLUDED

#include "Assets/Shaders/Include/PeriodicNoise.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"

// The time an animated material is drawn at, in seconds, stepped to its frames per second, so every animated pixel
// changes on the same frames, like a sprite animation.
float TBAnimationTime(TBInputs ins, float framesPerSecond)
{
    return floor(ins.time * framesPerSecond) / framesPerSecond;
}

// An animated layer's glint or sparkle at a texel of its textures. The height texture's blue channel holds 0 for
// neither, 1 + 127 times a glint's phase, or 128 + 128 times a sparkle's phase; the albedo lights up with the glint
// colour or the sparkle colour for the duty fraction of each of that one's periods, at half strength over the first and
// last quarter of it. Other layers and texels keep their colour.
float3 TBGlint(TBInputs ins, uint m, int2 texel, float3 colour)
{
    int row = TBLayerRow(m);
    float4 animation = TBLut(ins, TB_LAYER_ANIMATION_COLUMN, row);
    [branch]
    if (animation.x > 0.0 && TBLut(ins, TB_LAYER_SWAY_COLUMN, row).x <= 0.0)
    {
        float code = round(LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, m).b * 255.0);
        [branch]
        if (code > 0.5)
        {
            bool sparkle = code > 127.5;
            float4 glint = TBLut(ins, TB_LAYER_GLINT_COLUMN, row);
            float4 sparkleLook = TBLut(ins, TB_LAYER_SPARKLE_COLUMN, row);
            float phase = sparkle ? (code - 128.0) / 128.0 : (code - 1.0) / 127.0;
            float period = sparkle ? sparkleLook.a : animation.y;
            float duty = sparkle ? TBLut(ins, TB_LAYER_SPARKLE_TIMING_COLUMN, row).x : animation.z;
            float cycle = frac(TBAnimationTime(ins, animation.x) / period + phase);
            if (cycle < duty)
            {
                float along = cycle / duty;
                colour = lerp(colour, sparkle ? sparkleLook.rgb : glint.rgb, along < 0.25 || along >= 0.75 ? 0.5 : 1.0);
            }
        }
    }
    return colour;
}

// How many pixels further in an animated layer's rims reach at a pixel: up to its rim swell, in and out over its
// swell period, at a phase that drifts along the edge with the shared edge noise's pattern, so the swell rolls along a
// shore. 0 for a still layer. The edge itself never moves.
float TBSwell(TBInputs ins, uint m, float2 worldPixel)
{
    float4 animation = TBLut(ins, TB_LAYER_ANIMATION_COLUMN, TBLayerRow(m));
    float swell = 0.0;
    [branch]
    if (animation.x > 0.0 && animation.w > 0.0)
    {
        float period = TBLut(ins, TB_LAYER_GLINT_COLUMN, TBLayerRow(m)).a;
        float phase = TBAnimationTime(ins, animation.x) / period + 0.6 * PeriodicEdgeNoise(worldPixel);
        swell = animation.w * (0.5 - 0.5 * cos(6.28318530718 * phase));
    }
    return swell;
}

#endif
