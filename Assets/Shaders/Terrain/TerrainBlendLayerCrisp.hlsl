#ifndef TOPDOWNRPG_TERRAIN_BLEND_LAYER_CRISP_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_LAYER_CRISP_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendInterior.hlsl"

// Layer m's colour at a pixel, premultiplied: from the outside in, the translucent shadow, the antialiased edge pixel
// (the outline, or the rim's outer colour where the pair draws none, or a sunken layer's bank: TBEdgeColour), then the
// interior under the rim (TBInside, with a sunken layer's bank), with the outline fading into it over the first pixel.
// The interior is worked out once for the edge pixel and the inside (TBLayerInterior).
// Where the pair has tufts, pixels of the edge and of the band outside it that a tuft covers are opaque: the outline
// along the tuft's border, the rim's outer colour inside it.
float4 TBLayer(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float d, float2 pixel, float2 worldPixel, TBCorner corner)
{
    float3 edge = lerp(e.fill, e.outline, e.outlineWeight);
    float tuft = 0.0;
    [branch]
    if (e.tuftHeight > 0.0 && d > -0.5 && d < 0.5 + e.tuftHeight)
    {
        tuft = TBTuft(ins, corner, m, d, pixel, worldPixel, e.tuftHeight);
    }

    float bank;
    float3 interior = TBLayerInterior(ins, m, e, w, d, tuft <= 0.5 && d > -0.5 && d < 0.5, tuft <= 0.5 && d <= -0.5, pixel, worldPixel, corner, false, (TBCentre)0, bank);
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    [branch]
    if (tuft > 0.5)
    {
        result = float4(tuft > 1.5 ? edge : e.fill, 1.0);
    }
    else if (d >= 0.5 + e.shadowWidth)
    {
        result = float4(0.0, 0.0, 0.0, 0.0);
    }
    else if (d >= 0.5)
    {
        float shadowAlpha = e.shadowAlpha * (1.0 - (d - 0.5) / max(e.shadowWidth, 0.001));
        result = float4(e.shadow * shadowAlpha, shadowAlpha);
    }
    else if (d > -0.5)
    {
        float edgeAlpha = e.shadowAlpha + (1.0 - e.shadowAlpha) * (0.5 - d);
        float3 own = e.seamless > 0.0 ? TBSeamlessEdge(ins, m, e, w, interior) : edge;
        result = float4(TBEdgeColour(ins, m, w, corner, worldPixel, lerp(edge, own, e.seamless)) * edgeAlpha, edgeAlpha);
    }
    else
    {
        float depth = -d;
        float band;
        float3 colour = TBInside(ins, m, e, w, depth, worldPixel, interior, bank, band);
        if (depth < 1.5 && depth >= bank)
        {
            colour = lerp(lerp(colour, e.outline, e.outlineWeight), colour, saturate(depth - 0.5));
        }
        result = float4(colour, 1.0);
    }
    return result;
}

#endif
