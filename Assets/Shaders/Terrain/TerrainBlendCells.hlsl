#ifndef TOPDOWNRPG_TERRAIN_BLEND_CELLS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_CELLS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendLook.hlsl"

// What the chip or stone whose centre is a map pixel sees there (see TBCentre), its pairs weighted as TBPairWeights
// weights them, worked out going down from layer m one layer at a time, and what else its look needs (TBCentreOutward:
// in the soft look the outward direction when asked for).
TBCentre TBCentreAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel, bool outward)
{
    TBCentre centre = (TBCentre)0;
    TBCorner corner = TBLoadCorner(ins, pixel);
    float4 terms = TBDistanceTerms(ins, corner, worldPixel);
    float up = TBLayerDistance(ins, corner, terms, m);
    centre.depth = -up;
    int firstRow = TBFirstPairRow(ins, m);
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
        up = below < TB_FAR ? below : up;
    }
    [branch]
    if (remaining > 0.0)
    {
        TBAddCentrePair(ins, firstRow, remaining, true, centre);
    }
    TBCentreOutward(ins, corner, m, worldPixel, outward, centre);
    return centre;
}

#endif
