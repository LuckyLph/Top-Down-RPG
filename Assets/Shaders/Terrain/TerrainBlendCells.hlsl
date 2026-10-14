#ifndef TOPDOWNRPG_TERRAIN_BLEND_CELLS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_CELLS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

// What a chip or stone sees at its centre pixel, decided from the corner patch around that centre, so every pixel of
// it agrees: how deep inside layer m the centre lies, the rim width of the pair drawn there (in the soft look the
// band's width where the pair has one, and none where it shows whole stones), how much of that pair shows whole
// stones in the soft look, and the outward direction of the shape there (soft only). A pair shows whole stones only
// where the centre lies at least TB_WHOLE_CLEARANCE pixels inside the lower layer, so a stone never reaches past a
// strip of the lower layer that thins out (where stone, gravel and grass meet) and cuts it into pieces; nearer, the
// pair's rim cuts the stones as in crisp.
struct TBCentre
{
    float depth;
    float rim;
    float whole;
    float2 outward;
};

// Adds one pair's part to what a chip or stone sees at its centre (TBCentre): the rim width of the pair at row, by its
// weight; in the soft look the band's width where it has one, none where it shows whole stones, which it does only
// when clear says the centre lies TB_WHOLE_CLEARANCE inside the lower layer (always over nothing).
void TBAddCentrePair(TBInputs ins, int row, float weight, bool clear, inout TBCentre centre)
{
    float4 widths = TBLut(ins, 2, row);
#ifdef _TERRAIN_SOFT
    float band = TBLut(ins, 4, row).a;
    float whole = widths.w > 0.5 && clear ? 1.0 : 0.0;
    centre.rim += weight * (whole > 0.5 ? 0.0 : (band > 0.0 ? band : widths.y));
    centre.whole += weight * whole;
#else
    centre.rim += weight * widths.y;
#endif
}

// What the chip or stone whose centre is a map pixel sees there (see TBCentre), its pairs weighted as TBPairWeights
// weights them, worked out going down from layer m one layer at a time; in the soft look the outward direction only
// when asked for, since only whole chips use it.
TBCentre TBCentreAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel, bool outward)
{
    TBCentre centre = (TBCentre)0;
    TBCorner corner = TBLoadCorner(ins, pixel);
    float4 terms = TBDistanceTerms(ins, corner, worldPixel);
    float up = TBLayerDistance(ins, corner, terms, m);
    centre.depth = -up;
    int firstRow = 1 + (int)ins.layers + (int)m * ((int)ins.layers + 1);
    float remaining = 1.0;
    float blend = max(ins.globals.z, 0.001);
    [loop]
    for (int j = (int)m - 1; j >= 0; j--)
    {
        float below = TBLayerDistance(ins, corner, terms, (uint)j);
        if (below < 0.5)
        {
            float weight = remaining * saturate((up - below - ins.globals.y) / blend + 0.5);
            remaining -= weight;
            [branch]
            if (weight > 0.0)
            {
                TBAddCentrePair(ins, firstRow + j + 1, weight, below <= -TB_WHOLE_CLEARANCE, centre);
            }
        }
        up = below;
    }
    [branch]
    if (remaining > 0.0)
    {
        TBAddCentrePair(ins, firstRow, remaining, true, centre);
    }
#ifdef _TERRAIN_SOFT
    [branch]
    if (outward)
    {
        centre.outward = TBOutward(ins, corner, m, worldPixel);
    }
#endif
    return centre;
}

#endif
