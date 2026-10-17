#ifndef TOPDOWNRPG_TERRAIN_BLEND_BANKS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_BANKS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendEdges.hlsl"

// How many pixels of bank show at a pixel inside sunken layer m's edge (its sink depth above 0, water): the sink depth
// times how much the edge faces up the screen there and how much of it lies over another layer along a pair that is not
// seamless (1 - w[0], less TBSeamlessBelow), so the bank is highest along edges facing up, thins as they turn sideways
// and is gone along edges facing down, over nothing or over a layer of the same surface. The direction is
// TBEdgeDirection's (in crisp the bare shape's, so the bank keeps one height along a straight edge and its foot follows
// the edge's noise). 0 for a level layer, and deeper inside than the bank and its shadow.
float TBBankHeight(TBInputs ins, uint m TB_W_PARAM, TBCorner corner, float2 worldPixel, float depth)
{
    float sink = TBLut(ins, TB_LAYER_BANK_COLUMN, TBLayerRow(m)).x;
    [branch]
    if (sink > 0.0)
    {
        sink *= (1.0 - w[0]) - TBSeamlessBelow(ins, m TB_W_ARG);
    }
    float bank = 0.0;
    [branch]
    if (sink > 0.0 && depth < 2.0 * sink + 1.0)
    {
        float2 n = TBEdgeDirection(ins, corner, m, worldPixel);
        bank = sink * saturate(n.y);
    }
    return bank;
}

// The colour of sunken layer m's bank at a pixel: the bank colours of the layers it meets there (what each shows when
// cut, such as the dirt under grass), blended by their pair weights, which leave out nothing (w[0]) and the layers met
// along a seamless pair, which show no bank.
float3 TBBankFace(TBInputs ins, uint m TB_W_PARAM)
{
    int firstRow = TBFirstPairRow(ins, m);
    float3 face = float3(0.0, 0.0, 0.0);
    float seamless = 0.0;
    [loop]
    for (uint k = 1u; k <= m; k++)
    {
        [branch]
        if (w[k] > 0.0)
        {
            float open = TBLut(ins, TB_PAIR_FILL_COLUMN, firstRow + (int)k).a;
            face += w[k] * open * TBLut(ins, TB_LAYER_BANK_COLOUR_COLUMN, TBLayerRow(k - 1u)).rgb;
            seamless += w[k] * (1.0 - open);
        }
    }
    return face / max(1.0 - w[0] - seamless, 1e-4);
}

// The colour of layer m's edge pixel: edge (the layer's own interior on a seamless pair), or where a sunken layer
// shows a bank there, the bank's face, so the bank starts at the edge instead of under the layer's outline or rim;
// blended in where the bank is under a pixel high.
float3 TBEdgeColour(TBInputs ins, uint m TB_W_PARAM, TBCorner corner, float2 worldPixel, float3 edge)
{
    float bank = TBBankHeight(ins, m TB_W_ARG, corner, worldPixel, 0.0);
    [branch]
    if (bank > 0.0)
    {
        edge = lerp(edge, TBBankFace(ins, m TB_W_ARG), saturate(bank));
    }
    return edge;
}

#endif
