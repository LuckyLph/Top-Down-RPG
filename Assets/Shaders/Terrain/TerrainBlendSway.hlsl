#ifndef TOPDOWNRPG_TERRAIN_BLEND_SWAY_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_SWAY_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendLook.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"

// The texel a swaying layer m (sway amplitude above 0, grass) shows at a world pixel: the pixel's own, moved left by
// how far it leans, so the blade standing there leans right. A gust from 0 to 1 rolls over the cells from the bottom
// left to the top right, a gust length of cells per sway period, each cell a little out of step with its neighbours;
// it picks a pose, how many whole pixels the tips lean (0 up to the sway amplitude), and a pixel leans by the pose
// times the square of its weight (the height texture's blue channel), rounded, so the foot stays put, the blade bends
// most near its tip, and every pose has one fixed shape: the blade steps between a few drawn poses, like a sprite
// animation, instead of a kink climbing up and down it as the gust grows. A swaying recipe gives every pixel of a
// weed's box the weight of its row, so the pixels of a row lean together, and keeps each box inside one cell column,
// so the gust is the same across it. A layer whose clumps lean whole (its sway row's last value) takes the gust at the
// cell of the weed's foot (its base row, the pixel's row less its box row from the height's green channel, less 2),
// so a weed that crosses a cell row leans as one instead of shearing there. Only the albedo is moved: the height and
// the edges stay where they are. lean is how many pixels left the texel was read from.
int2 TBSwayTexel(TBInputs ins, uint m, float2 worldPixel, int2 texel, out float lean)
{
    int row = TBLayerRow(m);
    float4 sway = TBLut(ins, TB_LAYER_SWAY_COLUMN, row);
    lean = 0.0;
    [branch]
    if (sway.x > 0.0)
    {
        float4 h = LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, TBSlice(ins, m));
        float weight = h.b;
        [branch]
        if (weight > 0.0)
        {
            float2 cell = floor(worldPixel / ART_PIXELS_PER_UNIT);
            if (sway.w > 0.5)
            {
                cell.y = floor((worldPixel.y - (round(h.g * 255.0) - 2.0)) / ART_PIXELS_PER_UNIT);
            }

            float jitter = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
            float time = TBAnimationTime(ins, TBLut(ins, TB_LAYER_ANIMATION_COLUMN, row).x);
            float phase = time / sway.y - (cell.x + 0.5 * cell.y) / sway.z + 0.15 * jitter;
            float pose = round(sway.x * (0.5 - 0.5 * cos(6.28318530718 * phase)));
            lean = round(pose * weight * weight);
            texel = (int2)WrapPixel(worldPixel - float2(lean, 0.0), TBShape(ins, m).z);
        }
    }
    return texel;
}

// Whether a map pixel lies on swaying layer m where it shows: inside layer m, past its edge pixel, with no layer above it
// covering the pixel (the layer above's cumulative shape, which counts every layer over m, keeps its edge pixel off it).
// A weed is drawn only where both its foot and its top do, so one rooted under a path or beyond its layer's edge is left
// out instead of its top showing alone, and one growing under a path or past its layer's edge instead of standing cut.
bool TBOpenOn(TBInputs ins, uint m, float2 pixel, float2 worldPixel)
{
    TBCorner corner = TBLoadCorner(ins, pixel);
    float4 terms = TBDistanceTerms(ins, corner, worldPixel);
    bool rooted = TBLayerDistance(ins, corner, terms, m) <= -0.5;
    uint up = TBNextPresent(TBPresentLayers(corner), m);
    [branch]
    if (rooted && up != m)
    {
        rooted = TBLayerDistance(ins, corner, terms, up) >= 0.5;
    }
    return rooted;
}

#endif
